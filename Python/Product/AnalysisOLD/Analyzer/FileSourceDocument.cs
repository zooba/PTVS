using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    sealed class FileSourceDocument : ISourceDocument {
        private readonly string _fullPath;

        public FileSourceDocument(string fullPath) {
            _fullPath = fullPath;
        }

        public string Moniker {
            get { return _fullPath; }
        }

        public async Task<Stream> ReadAsync() {
            return new FileStream(_fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
        }
    }
}
