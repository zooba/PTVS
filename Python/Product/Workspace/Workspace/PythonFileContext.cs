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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Intellisense;

namespace Microsoft.PythonTools.Workspace {
    class PythonFileContext : IDisposable {
        public const string ContextType = "F4096454-72BD-4129-AE8A-B53AED1ECD2B";
        public static readonly Guid ContextGuid = new Guid(ContextType);

        private readonly string _packageName;
        private readonly PathSet<ISourceDocument> _files;

        public PythonFileContext(
            string packageName,
            PathSet<ISourceDocument> files
        ) {
            _packageName = packageName;
            _files = files;
        }

        public void Dispose() {
            Disposed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler Disposed;

        public event EventHandler DocumentsChanged;

        //public event EventHandler<SourceDocumentEventArgs> SourceDocumentContentChanged;

        //public event EventHandler<SourceDocumentEventArgs> SourceDocumentAnalysisChanged;

        public string PackageName => _packageName;

        public async Task AddDocumentsAsync(
            IReadOnlyCollection<ISourceDocument> inputFiles,
            CancellationToken cancellationToken
        ) {
            bool anyAdded = false;
            lock (_files) {
                foreach (var file in inputFiles) {
                    anyAdded |= _files.Add(file.Moniker, file);
                }
            }
            if (anyAdded) {
                DocumentsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task<IReadOnlyCollection<ISourceDocument>> GetDocumentsAsync(CancellationToken cancellationToken) {
            lock (_files) {
                return _files.GetValues().ToList();
            }
        }

        public bool Contains(string filePath) {
            lock (_files) {
                return _files.Contains(filePath);
            }
        }

        //public void NotifyDocumentContentChanged(ISourceDocument document) {
        //    SourceDocumentContentChanged?.Invoke(this, new SourceDocumentEventArgs(document));
        //}
        //
        //public void NotifyDocumentAnalysisChanged(ISourceDocument document) {
        //    SourceDocumentAnalysisChanged?.Invoke(this, new SourceDocumentEventArgs(document));
        //}
    }
}
