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
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Workspace;
using Microsoft.PythonTools.Workspace.Intellisense;
using Microsoft.VisualStudio.Text;
using AP = Microsoft.PythonTools.Intellisense.AnalysisProtocol;

namespace Microsoft.PythonTools.Intellisense {
    /// <summary>
    /// Represents a file which is being analyzed.  Tracks the file ID in the out of proc analysis,
    /// the path to the file, the analyzer, and the buffer parser being used to track changes to edits
    /// amongst o
    /// </summary>
    public sealed class AnalysisEntry : IDisposable {
        private readonly int _fileId;
        private ISourceDocument _document;
        private ISourceDocumentSnapshot _snapshot;

        public readonly PythonLanguageService _analyzer;
        private readonly Dictionary<object, object> _properties = new Dictionary<object, object>();

        internal IIntellisenseCookie _cookie;

        /// <summary>
        /// Raised when a new analysis is available for this AnalyisEntry
        /// </summary>
        public event EventHandler AnalysisComplete;

        public event EventHandler Disposed;

        public readonly bool IsTemporaryFile;

        private static readonly object _searchPathEntryKey = new { Name = "SearchPathEntry" };

        public AnalysisEntry(
            PythonLanguageService analyzer,
            int fileId,
            bool isTemporaryFile,
            string path,
            ITextBuffer buffer, 
            AnalysisEntry priorEntry
        ) {
            _analyzer = analyzer;
            _fileId = fileId;
            IsTemporaryFile = isTemporaryFile;
            if (buffer == null) {
                Document = new FileSourceDocument(path);
                AnalysisCookie = null;
            } else {
                Document = new TextBufferSourceDocument(path, buffer);
                AnalysisCookie = new SnapshotCookie(buffer.CurrentSnapshot);
            }

            if (priorEntry != null) {
                priorEntry.AnalysisComplete += PriorEntry_AnalysisComplete;
                priorEntry.Disposed += PriorEntry_Disposed;
            }
        }

        private void PriorEntry_Disposed(object sender, EventArgs e) {
            var entry = sender as AnalysisEntry;
            if (entry != null) {
                entry.AnalysisComplete -= PriorEntry_AnalysisComplete;
                entry.Disposed -= PriorEntry_Disposed;
            }
        }

        private void PriorEntry_AnalysisComplete(object sender, EventArgs e) {
            OnContentChangeAsync()
                .HandleAllExceptions(null, GetType())
                .DoNotWait();
        }

        public void Dispose() {
            Document = null;
            LastSentSnapshot = null;
            Disposed?.Invoke(this, EventArgs.Empty);
        }

        public async Task SetBufferAsync(ITextBuffer buffer) {
            if (buffer == null) {
                Document = new FileSourceDocument(Document.Moniker);
                AnalysisCookie = null;
            } else {
                Document = new TextBufferSourceDocument(Document.Moniker, buffer);
                AnalysisCookie = new SnapshotCookie(buffer.CurrentSnapshot);
            }
            await OnContentChangeAsync();
        }

        public PythonLanguageService Analyzer => _analyzer;

        public ISourceDocument Document {
            get { return _document; }
            private set {
                var oldDoc = Interlocked.Exchange(ref _document, value);
                LastSentSnapshot = null;
                LastAnalyzedVersion = -1;
            }
        }

        public ISourceDocumentSnapshot LastSentSnapshot {
            get { return _snapshot; }
            private set {
                var oldSnap = Interlocked.Exchange(ref _snapshot, value);
                oldSnap?.Dispose();
            }
        }

        public IIntellisenseCookie AnalysisCookie {
            get { return _cookie; }
            set { _cookie = value; }
        }

        public string Path => _document.Moniker;

        public int FileId => _fileId;

        private static Task<string> GetFullContentAsync(ISourceDocumentSnapshot snapshot) {
            var reader = snapshot.Reader ?? new PythonSourceStreamReader(snapshot.Stream, false, null);
            return reader.ReadToEndAsync();
        }

        private static AP.VersionChanges[] GetChanges(ISourceDocumentSnapshot from, ISourceDocumentSnapshot to) {
            if (from == null) {
                // No previous snapshot
                return null;
            }
            if (from.Equals(to)) {
                // Same version
                return Array.Empty<AP.VersionChanges>();
            }

            var fromSnap = from.GetTextSnapshot();
            var toSnap = to.GetTextSnapshot();
            if (fromSnap == null || toSnap == null) {
                // Can only track changes between two text buffers
                return null;
            }

            var versions = new List<AP.VersionChanges>();
            for (var ver = fromSnap.Version; ver != toSnap.Version; ver = ver.Next) {
                if (ver.Changes == null || !ver.Changes.Any()) {
                    continue;
                }
                versions.Add(new AP.VersionChanges {
                    changes = ver.Changes.Select(c => new AP.ChangeInfo {
                        start = c.OldPosition,
                        length = c.OldLength,
                        newText = c.NewText
                    }).ToArray()
                });
            }
            return versions.ToArray();
        }

        internal async Task OnContentChangeAsync() {
            var lastSnap = LastSentSnapshot;
            var snapshot = await _document.ReadAsync(CancellationToken.None);
            AP.FileUpdate update = null;

            var changes = GetChanges(lastSnap, snapshot);
            if (changes == null) {
                update = new AP.FileUpdate {
                    content = await GetFullContentAsync(snapshot),
                    version = snapshot.Version,
                    kind = AP.FileUpdateKind.reset
                };
            } else if (changes.Length > 0) {
                update = new AP.FileUpdate {
                    versions = changes,
                    version = snapshot.Version,
                    kind = AP.FileUpdateKind.changes
                };
            } else {
                return;
            }

            LastSentSnapshot = snapshot;

            await _analyzer.BeginParseAsync(this, snapshot, update);
        }


        public bool IsAnalyzed => LastAnalyzedVersion >= 0;

        public int LastAnalyzedVersion { get; private set; }

        public Dictionary<object, object> Properties => _properties;

        //public string GetLine(int line) {
        //    return AnalysisCookie.GetLine(line);
        //}

        internal bool OnParseComplete(ISourceDocumentSnapshot snapshot) {
            if (snapshot.Version < (LastSentSnapshot?.Version ?? int.MaxValue)) {
                return false;
            }

            var listeners = Document.GetTextBuffer()?.GetParseTreeRegistrations();
            foreach (var notify in listeners.MaybeEnumerate()) {
                notify(this);
            }
            return true;
        }

        internal void OnNewAnalysisEntry() {
            var listeners = Document.GetTextBuffer()?.GetNewAnalysisEntryRegistrations();
            foreach (var notify in listeners.MaybeEnumerate()) {
                notify(this);
            }
        }

        internal void OnAnalysisComplete(ISourceDocumentSnapshot snapshot) {
            LastAnalyzedVersion = snapshot.Version;
            AnalysisComplete?.Invoke(this, EventArgs.Empty);

            var textSnapshot = snapshot.GetTextSnapshot();
            AnalysisCookie = textSnapshot == null ? null : new SnapshotCookie(textSnapshot);

            var listeners = Document.GetTextBuffer()?.GetNewAnalysisRegistrations();
            foreach (var notify in listeners.MaybeEnumerate()) {
                notify(this);
            }
        }

        public string SearchPathEntry {
            get {
                object result;
                Properties.TryGetValue(_searchPathEntryKey, out result);
                return (string)result;
            }
            set {
                Properties[_searchPathEntryKey] = value;
            }
        }
    }
}
