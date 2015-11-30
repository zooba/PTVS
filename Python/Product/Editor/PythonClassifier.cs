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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

namespace Microsoft.PythonTools.Editor {
    /// <summary>
    /// Provides classification based upon the TokenCategory enum.
    /// </summary>
    internal sealed class PythonClassifier : IClassifier, IDisposable {
        private readonly PythonClassifierProvider _provider;
        private readonly ITextBuffer _buffer;

        private IVsTask _currentTask;

        private readonly object _classificationsLock = new object();
        private ITextVersion _classificationsVersion;
        private List<ClassificationSpan> _classifications;

        internal PythonClassifier(PythonClassifierProvider provider, ITextBuffer buffer) {
            _provider = provider;
            _buffer = buffer;
            _classifications = new List<ClassificationSpan>();

            _buffer.Changed += Buffer_Changed;
            _buffer.ContentTypeChanged += BufferContentTypeChanged;

            BeginUpdateClassifications(_buffer.CurrentSnapshot);
        }

        private void Buffer_Changed(object sender, TextContentChangedEventArgs e) {
            BeginUpdateClassifications(e.After);
        }

        internal void BeginUpdateClassifications(ITextSnapshot snapshot) {
            var task = ThreadHelper.JoinableTaskFactory.RunAsyncAsVsTask(
                VsTaskRunContext.UIThreadBackgroundPriority,
                ct => UpdateClassifications(_buffer.CurrentSnapshot, ct)
            );
            task.Description = GetType().FullName;
            var oldTask = Interlocked.Exchange(ref _currentTask, task);
            if (oldTask != null) {
                oldTask.Cancel();
            }
        }

        private async Task<bool> UpdateClassifications(ITextSnapshot snapshot, CancellationToken cancellationToken) {
            var buffer = snapshot.TextBuffer;
            var version = snapshot.Version;

            var tokenization = snapshot.GetTokenization(cancellationToken);
            if (tokenization == null) {
                return false;
            }

            //var start = e.Changes.Min(c => c.NewSpan.Start);
            //var end = e.Changes.Max(c => c.NewSpan.End);
            //var span = new SnapshotSpan(e.After, start, end - start);
            var span = new SnapshotSpan(snapshot, 0, snapshot.Length);

            var newClassifications = new List<ClassificationSpan>();
            foreach (var token in tokenization.AllTokens) {
                var clas = ClassifyToken(tokenization, span, token);
                if (clas != null) {
                    newClassifications.Add(clas);
                }
                if (newClassifications.Count % 100 == 0) {
                    await System.Threading.Tasks.Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            await System.Threading.Tasks.Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            bool changed = false;
            lock (_classificationsLock) {
                if (_classificationsVersion == null ||
                    _classificationsVersion.VersionNumber < version.VersionNumber) {
                    _classificationsVersion = version;
                    _classifications = newClassifications;
                    changed = true;
                }
            }

            if (changed) {
                ClassificationChanged?.Invoke(this, new ClassificationChangedEventArgs(span));
            }
            return changed;
        }

        void IDisposable.Dispose() {
            _buffer.Changed -= Buffer_Changed;
            _buffer.ContentTypeChanged -= BufferContentTypeChanged;
        }

        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;

        /// <summary>
        /// This method classifies the given snapshot span.
        /// </summary>
        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span) {
            lock (_classificationsLock) {
                return _classifications;
            }
        }

        public PythonClassifierProvider Provider {
            get {
                return _provider;
            }
        }

        private Dictionary<TokenCategory, IClassificationType> CategoryMap {
            get {
                return _provider.CategoryMap;
            }
        }

        private void BufferContentTypeChanged(object sender, ContentTypeChangedEventArgs e) {
            _buffer.Changed -= Buffer_Changed;
            _buffer.ContentTypeChanged -= BufferContentTypeChanged;
            _buffer.Properties.RemoveProperty(typeof(PythonClassifier));
        }

        private ClassificationSpan ClassifyToken(Tokenization tokenization, SnapshotSpan span, Token token) {
            IClassificationType classification = null;

            if (token.Is(TokenKind.Dot)) {
                classification = _provider.DotClassification;
            } else if (token.Is(TokenKind.Comma)) {
                classification = _provider.CommaClassification;
            } else {
                CategoryMap.TryGetValue(token.Kind.GetCategory(), out classification);
            }

            if (classification == null || token.Span.Length == 0) {
                return null;
            }

            var tokenSnapshot = tokenization.Cookie as ITextSnapshot ?? span.Snapshot;

            SnapshotSpan tokenSpan;
            try {
                if (tokenSnapshot == span.Snapshot) {
                    tokenSpan = new SnapshotSpan(tokenSnapshot, token.Span.Start.Index, token.Span.Length);
                } else {
                    tokenSpan = tokenSnapshot
                        .CreateTrackingSpan(token.Span.Start.Index, token.Span.Length, SpanTrackingMode.EdgeInclusive)
                        .GetSpan(span.Snapshot);
                }
            } catch (ArgumentException) {
                return null;
            }

            if (span.Length == 0 && tokenSpan.Span.Contains(span) ||
                (span.Intersection(tokenSpan) ?? default(Span)).Length > 0) {
                return new ClassificationSpan(tokenSpan, classification);
            }
            return null;
        }
    }

    internal static partial class ClassifierExtensions {
        public static PythonClassifier GetPythonClassifier(this ITextBuffer buffer) {
            PythonClassifier res;
            if (buffer.Properties.TryGetProperty(typeof(PythonClassifier), out res)) {
                return res;
            }
            return null;
        }
    }
}
