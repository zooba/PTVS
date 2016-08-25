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
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
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
    public sealed class AnalysisEntry {
        private readonly int _fileId;
        private readonly string _path;
        private readonly List<BufferInfo> _textBuffers;

        public readonly PythonLanguageService _analyzer;
        private readonly Dictionary<object, object> _properties = new Dictionary<object, object>();

        internal IIntellisenseCookie _cookie;

        /// <summary>
        /// Raised when a new analysis is available for this AnalyisEntry
        /// </summary>
        public event EventHandler AnalysisComplete;
        public readonly bool IsTemporaryFile;

        private static readonly object _searchPathEntryKey = new { Name = "SearchPathEntry" };

        public AnalysisEntry(PythonLanguageService analyzer, string path, int fileId, bool isTemporaryFile = false) {
            _analyzer = analyzer;
            _path = path;
            _fileId = fileId;
            IsTemporaryFile = isTemporaryFile;
            _textBuffers = new List<BufferInfo>();
        }

        public void AddBuffer(ITextBuffer buffer) {
            lock (_textBuffers) {
                _textBuffers.Add(new BufferInfo {
                    Buffer = buffer,
                    Id = _textBuffers.Count,
                    LastAnalyzed = null
                });
            }
        }

        public PythonLanguageService Analyzer => _analyzer;

        public IIntellisenseCookie AnalysisCookie {
            get { return _cookie; }
            set { _cookie = value; }
        }

        public string Path => _path;

        public int FileId => _fileId;

        public bool IsAnalyzed { get; internal set; }

        public Dictionary<object, object> Properties => _properties;

        public string GetLine(int line) {
            return AnalysisCookie.GetLine(line);
        }

        internal IEnumerable<Tuple<ISourceDocument, int>> GetParseInfo() {
            lock (_textBuffers) {
                return _textBuffers.Select(b => {
                        var snap = b.Buffer?.CurrentSnapshot;
                        if (snap != null) {
                            return Tuple.Create<ISourceDocument, int>(
                                new SnapshotSourceDocument("{0}#{1}".FormatInvariant(Path, b.Id), snap),
                                b.Id
                            );
                        }
                        return Tuple.Create<ISourceDocument, int>(new FileSourceDocument(Path), -1);
                    }).ToArray();
            }
        }

        internal void OnContentChange() {
            _analyzer.BeginParse(this);
        }

        internal void OnParseComplete() {
            foreach (var notify in _textBuffers.SelectMany(tb => tb.GetParseTreeRegistrations())) {
                notify(this);
            }
        }

        internal void OnNewAnalysisEntry() {
            foreach (var notify in _textBuffers.SelectMany(tb => tb.GetNewAnalysisEntryRegistrations())) {
                notify(this);
            }
        }

        internal void OnAnalysisComplete(IEnumerable<AP.BufferVersion> versions) {
            foreach (var v in versions) {
                if (v.bufferId >= _textBuffers.Count) {
                    continue;
                }

                var bufferInfo = _textBuffers[v.bufferId];
                if (bufferInfo?.Buffer == null) {
                    continue;
                }

                if (bufferInfo.LastAnalyzed == null) {
                    bufferInfo.LastAnalyzed = bufferInfo.Buffer.CurrentSnapshot.Version;
                }

                while (bufferInfo.LastAnalyzed.Next != null && bufferInfo.LastAnalyzed.VersionNumber < v.version) {
                    bufferInfo.LastAnalyzed = bufferInfo.LastAnalyzed.Next;
                }
            }

            IsAnalyzed = true;
            AnalysisComplete?.Invoke(this, EventArgs.Empty);

            foreach (var notify in _textBuffers.SelectMany(tb => tb.GetNewAnalysisRegistrations())) {
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

        public int GetBufferId(ITextBuffer buffer) {
            var res = _textBuffers.FirstOrDefault(b => b.Buffer == buffer);
            if (res != null) {
                return res.Id;
            }
            // Race with the buffer closing...

            // No buffer parser associated with the file yet.  This can happen when
            // you have a document that is open but hasn't had focus causing the full
            // load of our intellisense controller.  In that case there is only a single
            // buffer which is buffer 0.  An easy repro for this is to open a IronPython WPF
            // project and close it with the XAML file focused and the .py file still open.
            // Re-open the project, and double click on a button on the XAML page.  The python
            // file isn't loaded and weh ave no BufferParser associated with it.
            return 0;
        }

        public ITextVersion GetAnalysisVersion(ITextBuffer buffer) {
            var res = _textBuffers.FirstOrDefault(b => b.Buffer == buffer);
            if (res != null) {
                return res.LastAnalyzed;
            }
            // Analysis version has gone away, this can happen
            // if the text view is getting closed while we're
            // trying to perform an operation.

            // See GetBufferId above, this is really just defense in depth...
            return buffer.CurrentSnapshot.Version;
        }

        private class BufferInfo {
            public ITextBuffer Buffer { get; set; }
            public ITextVersion LastAnalyzed { get; set; }
            public int Id { get; set; }

            public IEnumerable<Action<AnalysisEntry>> GetNewAnalysisEntryRegistrations() {
                return (Buffer?.GetNewAnalysisEntryRegistrations())?.MaybeEnumerate();
            }

            public IEnumerable<Action<AnalysisEntry>> GetNewAnalysisRegistrations() {
                return (Buffer?.GetNewAnalysisRegistrations())?.MaybeEnumerate();
            }

            public IEnumerable<Action<AnalysisEntry>> GetParseTreeRegistrations() {
                return (Buffer?.GetParseTreeRegistrations())?.MaybeEnumerate();
            }
        }
    }
}
