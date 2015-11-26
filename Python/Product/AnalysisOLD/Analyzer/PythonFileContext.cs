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

        public event EventHandler<SourceDocumentContentChangedEventArgs> SourceDocumentContentChanged;

        public string ContextRoot {
            get { return _contextRoot; }
        }

        public async Task AddDocumentsAsync(
            IReadOnlyCollection<ISourceDocument> inputFiles,
            CancellationToken cancellationToken
        ) {
            await _filesLock.WaitAsync(cancellationToken);
            try {
                foreach (var file in inputFiles) {
                    _files.Add(file.Moniker, file);
                }
            } finally {
                _filesLock.Release();
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
