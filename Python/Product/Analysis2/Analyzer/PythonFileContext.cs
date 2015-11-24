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
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    public sealed class PythonFileContext : IDisposable {
        private readonly string _contextRoot;
        private readonly string _packageName;
        private readonly PathSet<ISourceDocument> _files;
        private readonly SemaphoreSlim _filesLock = new SemaphoreSlim(1, 1);

        public PythonFileContext(
            string contextRoot,
            string packageName
        ) {
            _contextRoot = contextRoot;
            _packageName = packageName;
            _files = new PathSet<ISourceDocument>(_contextRoot);
        }

        public void Dispose() {
            Disposed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler Disposed;

        public event EventHandler DocumentsChanged;

        public event EventHandler<SourceDocumentContentChangedEventArgs> SourceDocumentContentChanged;

        public string ContextRoot {
            get { return _contextRoot; }
        }

        public async Task AddDocumentsAsync(
            IReadOnlyCollection<ISourceDocument> inputFiles,
            CancellationToken cancellationToken
        ) {
            bool anyAdded = false;
            await _filesLock.WaitAsync(cancellationToken);
            try {
                foreach (var file in inputFiles) {
                    anyAdded |= _files.Add(file.Moniker, file);
                }
            } finally {
                _filesLock.Release();
            }
            if (anyAdded) {
                DocumentsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task<IReadOnlyCollection<ISourceDocument>> GetDocumentsAsync(CancellationToken cancellationToken) {
            await _filesLock.WaitAsync(cancellationToken);
            try {
                return _files.GetValues().ToList();
            } finally {
                _filesLock.Release();
            }
        }

        public async Task<bool> ContainsAsync(string filePath, CancellationToken cancellationToken) {
            await _filesLock.WaitAsync(cancellationToken);
            try {
                return _files.Contains(filePath);
            } finally {
                _filesLock.Release();
            }
        }

        public void NotifyDocumentContentChanged(ISourceDocument document) {
            SourceDocumentContentChanged?.Invoke(this, new SourceDocumentContentChangedEventArgs(document));
        }

        public object First() {
            throw new NotImplementedException();
        }
    }
}
