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
using System.Threading.Tasks;
using Microsoft.PythonTools.Intellisense;
using Microsoft.VisualStudio.Text;
using AP = Microsoft.PythonTools.Intellisense.AnalysisProtocol;

namespace Microsoft.PythonTools.Analyzer {
    class PythonFileView : IPythonFileView {
        internal readonly PythonProjectAnalyzer _analyzer;

        private readonly object _entryLock = new object();
        private AnalysisEntry _entry;
        private TaskCompletionSource<AnalysisEntry> _entryTask;

        public PythonFileView(PythonProjectAnalyzer analyzer, string moniker) {
            _analyzer = analyzer;
            Moniker = moniker;
        }

        public int CurrentVersion { get; private set; }

        public string Moniker { get; }

        public event EventHandler ParseComplete;

        internal void OnParseComplete() {
            ParseComplete?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler AnalysisComplete;

        internal void OnAnalysisComplete() {
            AnalysisComplete?.Invoke(this, EventArgs.Empty);
        }

        internal void SetEntry(AnalysisEntry entry) {
            lock (_entryLock) {
                _entry = entry;
                _entryTask?.TrySetResult(entry);
                _entryTask = null;
            }
        }

        internal Task<AnalysisEntry> GetEntryAsync() {
            if (_entry != null) {
                return Task.FromResult(_entry);
            }

            lock (_entryLock) {
                if (_entry != null) {
                    return Task.FromResult(_entry);
                }

                if (_entryTask == null) {
                    _entryTask = new TaskCompletionSource<AnalysisEntry>();
                }

                return _entryTask.Task;
            }
        }
    }

    class PythonFileBuffer : IPythonFileBuffer {
        private readonly PythonFileView _view;
        private readonly int _id;

        public PythonFileBuffer(PythonFileView view, ITextBuffer buffer, int id) {
            _view = view;
            _id = id;
        }

        public int LastKnownVersion { get; private set; }

        public async Task AddChangesAsync(IEnumerable<BufferChangeInfo> changes) {
            var updates = new List<AP.FileUpdate>();
            int lastVersion = LastKnownVersion;

            foreach (var changeGroup in changes.GroupBy(c => c.Version).OrderBy(g => g.Key)) {
                var update = new AP.FileUpdate {
                    bufferId = _id,
                    versions = new[] { new AP.VersionChanges {
                        changes = changeGroup.Select(c => new AP.ChangeInfo {
                            start = c.Start,
                            length = c.Length,
                            newText = c.NewText
                        }).ToArray()
                    } },
                    version = changeGroup.Key,
                    kind = AP.FileUpdateKind.changes
                };
                if (changeGroup.Key > lastVersion) {
                    lastVersion = changeGroup.Key;
                }
            }

            var resp = await _view._analyzer.SendRequestAsync(new AP.FileUpdateRequest {
                fileId = (await _view.GetEntryAsync()).FileId,
                updates = updates.ToArray()
            });

            LastKnownVersion = lastVersion;
        }

        public async Task ResetContentAsync(int version, string fullContent) {
        }
    }
}
