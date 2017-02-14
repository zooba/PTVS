// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.PythonTools;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Options;
using Microsoft.PythonTools.Parsing;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.MockVsTests;
using TestUtilities;

namespace PythonToolsMockTests {
    public sealed class PythonEditor : IDisposable {
        private readonly bool _disposeVS, _disposeFactory, _disposeAnalyzer;
        public readonly MockVs VS;
        public readonly IPythonInterpreterFactory Factory;
        public readonly VsProjectAnalyzer Analyzer;
        public readonly MockVsTextView View;
        public readonly AdvancedEditorOptions AdvancedOptions;

        public PythonEditor(
            string content = null,
            PythonLanguageVersion version = PythonLanguageVersion.V27,
            MockVs vs = null,
            IPythonInterpreterFactory factory = null,
            VsProjectAnalyzer analyzer = null,
            string filename = null
        ) {
            if (vs == null) {
                _disposeVS = true;
                vs = new MockVs();
            }
            MockVsTextView view = null;
            try {
                AdvancedEditorOptions advancedOptions = null;
                vs.InvokeSync(() => {
                    advancedOptions = vs.GetPyService().AdvancedOptions;
                    advancedOptions.AutoListMembers = true;
                    advancedOptions.AutoListIdentifiers = false;
                });
                AdvancedOptions = advancedOptions;

                if (factory == null) {
                    _disposeFactory = true;
                    factory = InterpreterFactoryCreator.CreateAnalysisInterpreterFactory(version.ToVersion());
                }
                if (analyzer == null) {
                    _disposeAnalyzer = true;
                    vs.InvokeSync(() => {
                        analyzer = new VsProjectAnalyzer(vs.ServiceProvider, factory);
                    });
                    var task = analyzer.ReloadTask;
                    if (task != null) {
                        task.WaitAndUnwrapExceptions();
                    }
                }

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using (var mre = new ManualResetEventSlim()) {
                    EventHandler evt = (s, e) => mre.SetIfNotDisposed();
                    analyzer.AnalysisStarted += evt;
                    view = vs.CreateTextView(PythonCoreConstants.ContentType, content ?? "", v => {
                        v.TextView.TextBuffer.Properties.AddProperty(typeof(VsProjectAnalyzer), analyzer);
                    }, filename);

                    try {
                        while (!mre.Wait(500, cts.Token) && !vs.HasPendingException) { }
                        analyzer.WaitForCompleteAnalysis(x => !cts.IsCancellationRequested && !vs.HasPendingException);
                    } catch (OperationCanceledException) {
                    } finally {
                        analyzer.AnalysisStarted -= evt;
                    }
                    if (cts.IsCancellationRequested) {
                        Assert.Fail("Timed out waiting for code analysis");
                    }
                    vs.ThrowPendingException();
                }

                View = view;
                view = null;
                Analyzer = analyzer;
                analyzer = null;
                Factory = factory;
                factory = null;
                VS = vs;
                vs = null;
            } finally {
                if (view != null) {
                    view.Dispose();
                }
                if (analyzer != null && _disposeAnalyzer) {
                    analyzer.Dispose();
                }
                if (factory != null && _disposeFactory) {
                    var disp = factory as IDisposable;
                    if (disp != null) {
                        disp.Dispose();
                    }
                }
                if (vs != null && _disposeVS) {
                    vs.Dispose();
                }
            }
        }

        public string Text {
            get { return View.Text; }
            set {
                using (var mre = new ManualResetEventSlim()) {
                    using (var edit = View.TextView.TextBuffer.CreateEdit()) {
                        edit.Delete(0, edit.Snapshot.Length);
                        edit.Apply();
                    }

                    var buffer = View.TextView.TextBuffer;
                    var oldVersion = buffer.CurrentSnapshot;
                    buffer.GetPythonAnalysisClassifier().ClassificationChanged += (s, e) => {
                        var entry = View.TextView.GetAnalysisEntry(buffer, VS.ServiceProvider);
                        if (entry.TryGetBufferParser()?.GetAnalysisVersion(buffer).VersionNumber > oldVersion.Version.VersionNumber) {
                            mre.SetIfNotDisposed();
                        }
                    };
                    
                    using (var edit = View.TextView.TextBuffer.CreateEdit()) {
                        edit.Insert(0, value);
                        edit.Apply();
                    }

                    var analysis = View.TextView.GetAnalysisEntry(buffer, VS.ServiceProvider);
                    analysis.TryGetBufferParser().Requeue();    // force the reparse to happen quickly...

                    if (!mre.Wait(10000)) {
                        throw new TimeoutException("Failed to see buffer start analyzing");
                    }
                    Analyzer.WaitForCompleteAnalysis(_ => true);
                }
            }
        }

        public ITextSnapshot CurrentSnapshot {
            get { return View.TextView.TextSnapshot; }
        }

        public List<Completion> GetCompletionListAfter(string substring, bool assertIfNoCompletions = true) {
            var snapshot = CurrentSnapshot;
            return GetCompletionList(snapshot.GetText().IndexOfEnd(substring), assertIfNoCompletions, snapshot);
        }

        public List<Completion> GetCompletionList(
            int index,
            bool assertIfNoCompletions = true,
            ITextSnapshot snapshot = null
        ) {
            snapshot = snapshot ?? CurrentSnapshot;
            if (index < 0) {
                index += snapshot.Length + 1;
            }
            View.MoveCaret(new SnapshotPoint(snapshot, index));
            VS.Invoke(() => View.MemberList());
            using (var sh = View.WaitForSession<ICompletionSession>(assertIfNoSession: assertIfNoCompletions)) {
                if (sh == null) {
                    return new List<Completion>();
                }
                return sh.Session.CompletionSets.SelectMany(cs => cs.Completions).ToList();
            }
        }

        public void Backspace() => VS.InvokeSync(() => View.Backspace());
        public void Enter() => VS.InvokeSync(() => View.Enter());
        public void Clear() => VS.InvokeSync(() => View.Clear());
        public void MoveCaret(int line, int column) => View.MoveCaret(line, column);
        public void MemberList() => VS.InvokeSync(() => View.MemberList());
        public void ParamInfo() => VS.InvokeSync(() => View.ParamInfo());
        public void Type(string text) => VS.InvokeSync(() => View.Type(text));


        public IEnumerable<string> GetCompletions(int index) {
            return GetCompletionList(index, false).Select(c => c.DisplayText);
        }

        public IEnumerable<string> GetCompletionsAfter(string substring) {
            return GetCompletionListAfter(substring, false).Select(c => c.DisplayText);
        }

        public void Dispose() {
            if (View != null) {
                View.Dispose();
            }
            if (Analyzer != null && _disposeAnalyzer) {
                Analyzer.Dispose();
            }
            if (Factory != null && _disposeFactory) {
                var disp = Factory as IDisposable;
                if (disp != null) {
                    disp.Dispose();
                }
            }
            if (VS != null && _disposeVS) {
                VS.Dispose();
            }
        }
    }
}
