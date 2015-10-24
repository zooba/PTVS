/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 * ***************************************************************************/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Parsing;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

namespace Microsoft.PythonTools.Editor {
    /// <summary>
    /// Provides classification based upon the DLR TokenCategory enum.
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

        private void BeginUpdateClassifications(ITextSnapshot snapshot) {
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

            ISourceDocument document;
            PythonFileContext context;
            PythonLanguageService service;

            if (!buffer.Properties.TryGetProperty(typeof(ISourceDocument), out document) ||
                !buffer.Properties.TryGetProperty(typeof(PythonFileContext), out context) ||
                !buffer.Properties.TryGetProperty(typeof(PythonLanguageService), out service)) {
                return false;
            }

            //var start = e.Changes.Min(c => c.NewSpan.Start);
            //var end = e.Changes.Max(c => c.NewSpan.End);
            //var span = new SnapshotSpan(e.After, start, end - start);
            var span = new SnapshotSpan(snapshot, 0, snapshot.Length);

            var tokenization = await service.GetNextTokenizationAsync(
                context,
                document.Moniker,
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();

            var newClassifications = new List<ClassificationSpan>();
            foreach (var token in tokenization.AllTokens) {
                var clas = ClassifyToken(tokenization, span, token);
                if (clas != null) {
                    newClassifications.Add(clas);
                }
            }

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

        private ClassificationSpan ClassifyToken(Tokenization tokenization, SnapshotSpan span, TokenInfo token) {
            IClassificationType classification = null;

            if (token.Category == TokenCategory.Operator) {
                if (token.Trigger == TokenTriggers.MemberSelect) {
                    classification = _provider.DotClassification;
                }
            } else if (token.Category == TokenCategory.Grouping) {
                if ((token.Trigger & TokenTriggers.MatchBraces) != 0) {
                    classification = _provider.GroupingClassification;
                }
            } else if (token.Category == TokenCategory.Delimiter) {
                if (token.Trigger == TokenTriggers.ParameterNext) {
                    classification = _provider.CommaClassification;
                }
            }

            if (classification == null) {
                CategoryMap.TryGetValue(token.Category, out classification);
            }

            if (classification != null) {
                var tokenSpan = new Span(token.StartIndex, token.EndIndex - token.StartIndex);
                var intersection = span.Intersection(tokenSpan);

                if (intersection != null && intersection.Value.Length > 0 ||
                    // handle zero-length spans which Intersect and Overlap won't return true on ever.
                    (span.Length == 0 && tokenSpan.Contains(span.Span))) {
                    return new ClassificationSpan(new SnapshotSpan(span.Snapshot, tokenSpan), classification);
                }
            }

            return null;
        }
    }

    internal static partial class ClassifierExtensions {
        public static PythonClassifier GetPythonClassifier(this ITextBuffer buffer) {
            PythonClassifier res;
            if (buffer.Properties.TryGetProperty<PythonClassifier>(typeof(PythonClassifier), out res)) {
                return res;
            }
            return null;
        }
    }
}
