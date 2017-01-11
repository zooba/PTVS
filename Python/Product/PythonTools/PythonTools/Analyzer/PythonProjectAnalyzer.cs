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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Ipc.Json;
using Microsoft.PythonTools.Parsing;
using Microsoft.PythonTools.Projects;
using Microsoft.VisualStudioTools;
using AP = Microsoft.PythonTools.Intellisense.AnalysisProtocol;

namespace Microsoft.PythonTools.Analyzer {
    sealed class PythonProjectAnalyzer : IPythonAnalyzer {
        private readonly IServiceProvider _site;
        private IPythonInterpreterFactory _factory;
        private VsProjectAnalyzer _analyzer;

        private readonly CancellationTokenSource _onDispose = new CancellationTokenSource();
        private readonly SemaphoreSlim _actionLock = new SemaphoreSlim(1);

        internal readonly Dictionary<string, PythonFileView> _pendingViews =
            new Dictionary<string, PythonFileView>(StringComparer.OrdinalIgnoreCase);

        private static readonly object _entryViewKey = new object();

        public PythonProjectAnalyzer(IServiceProvider site, string moniker) {
            Moniker = moniker;
        }

        private VsProjectAnalyzer CreateNewAnalyzer() {
            var a = new VsProjectAnalyzer(_site, _factory);

            a.AnalysisStarted += Analyzer_AnalysisStarted;
            a.AnalysisComplete += Analyzer_AnalysisComplete;
            a.AnalyzerNeedsRestart += Analyzer_NeedsRestart;

            return a;
        }

        private void Analyzer_NeedsRestart(object sender, EventArgs e) {
            _site.GetUIThread().InvokeTask(() => ResetAllAsync())
                .SilenceException<OperationCanceledException>()
                .HandleAllExceptions(_site, GetType())
                .DoNotWait();
        }

        private void Analyzer_AnalysisStarted(object sender, EventArgs e) {
            AnalysisStarted?.Invoke(this, EventArgs.Empty);
        }

        private void Analyzer_AnalysisComplete(object sender, AnalysisCompleteEventArgs e) {
            var entry = ((VsProjectAnalyzer)sender).GetAnalysisEntryFromPath(e.Path);
            if (entry == null) {
                return;
            }

            object o;
            PythonFileView view;
            if (entry.Properties.TryGetValue(_entryViewKey, out o) && (view = o as PythonFileView) != null) {
                view.OnAnalysisComplete();
            }
        }

        internal VsProjectAnalyzer GetAnalyzer() {
            if (_analyzer != null) {
                return _analyzer;
            }
            if (_factory == null) {
                throw new InvalidOperationException();
            }

            return CreateNewAnalyzer();
        }

        private void DisposeAnalyzer() {
            var a = _analyzer;
            _analyzer = null;
            if (a == null) {
                return;
            }

            a.AnalysisStarted -= Analyzer_AnalysisStarted;
            a.AnalysisComplete -= Analyzer_AnalysisComplete;
            a.AnalyzerNeedsRestart -= Analyzer_NeedsRestart;

            if (a.RemoveUser()) {
                a.Dispose();
            }
        }

        public void Dispose() {
            if (!_onDispose.IsCancellationRequested) {
                _onDispose.Cancel();
                _onDispose.Dispose();
                DisposeAnalyzer();
            }
        }

        internal Task<T> SendRequestAsync<T>(
            Request<T> request,
            T defaultValue = null,
            TimeSpan? timeout=null
        ) where T : Response, new() {
            return GetAnalyzer().SendRequestAsync(request, defaultValue, timeout);
        }

        internal Task<IDisposable> LockAsync() {
            CancellationToken ct;
            try {
                ct = _onDispose.Token;
            } catch (ObjectDisposedException) {
                throw new OperationCanceledException();
            }
            return _actionLock.LockAsync(ct);
        }

        public string Moniker { get; }

        public PythonLanguageVersion LanguageVersion => CurrentInterpreter?.GetLanguageVersion() ?? PythonLanguageVersion.None;
        public IPythonInterpreterFactory CurrentInterpreter => _analyzer?.InterpreterFactory ?? _factory;
        public event EventHandler CurrentInterpreterChanged;
        public event EventHandler AnalysisReset;
        public event EventHandler AnalysisStarted;
        public event EventHandler AnalysisCompleted;

        public async Task AddFileAsync(string moniker) {
            using (await LockAsync()) {
                await AddFileAsync(GetAnalyzer(), moniker);
            }
        }

        private async Task AddFileAsync(VsProjectAnalyzer analyzer, string moniker) {
            var entry = await analyzer.AnalyzeFileAsync(moniker);
            PythonFileView view;
            lock (_pendingViews) {
                if (_pendingViews.TryGetValue(moniker, out view)) {
                    _pendingViews.Remove(moniker);
                }
            }
            if (view != null) {
                entry.Properties[_entryViewKey] = view;
                view.SetEntry(entry);
            }
        }

        public async Task AddFilesAsync(IReadOnlyList<string> monikers) {
            using (await LockAsync()) {
                await AddFilesAsync(GetAnalyzer(), monikers);
            }
        }

        private async Task AddFilesAsync(VsProjectAnalyzer analyzer, IReadOnlyList<string> monikers) {
            var entries = await analyzer.AnalyzeFileAsync(monikers);
            lock (_pendingViews) {
                foreach (var entry in entries) {
                    PythonFileView view;
                    if (_pendingViews.TryGetValue(entry.Path, out view)) {
                        _pendingViews.Remove(entry.Path);
                    }
                    if (view != null) {
                        entry.Properties[_entryViewKey] = view;
                        view.SetEntry(entry);
                    }
                }
            }
        }

        public Task RenameFileAsync(string oldMoniker, string newMoniker) {
            throw new NotImplementedException();
        }

        public async Task ForgetFileAsync(string moniker) {
            using (await LockAsync()) {
                await ForgetFileAsync(GetAnalyzer(), moniker);
            }
        }

        private async Task ForgetFileAsync(VsProjectAnalyzer analyzer, string moniker) {
            var entry = analyzer.GetAnalysisEntryFromPath(moniker);

            if (entry != null) {
                await analyzer.UnloadFileAsync(entry);
            }
        }

        public async Task ResetAllAsync() {
            using (await LockAsync()) {
                var a = _analyzer;
                _analyzer = null;
                var b = CreateNewAnalyzer();
                if (a == null) {
                    return;
                }

                var files = a.LoadedFiles.Keys().ToArray();

                AnalysisReset?.Invoke(this, EventArgs.Empty);

                await AddFilesAsync(b, files);

                // TODO: Transfer existing files from a to b
                if (a.RemoveUser()) {
                    a.Dispose();
                }
            }
        }

        public void SetInterpreter(IPythonInterpreterFactory factory) {
            _factory = factory;
        }

        public async Task<IPythonFileView> GetFileViewAsync(string moniker) {
            var entry = _analyzer?.GetAnalysisEntryFromPath(moniker);
            var view = new PythonFileView(this, moniker);
            if (entry != null) {
                view.SetEntry(entry);
                entry.Properties[_entryViewKey] = view;
            } else {
                lock (_pendingViews) {
                    _pendingViews[moniker] = view;
                }
            }
            return view;
        }
    }
}
