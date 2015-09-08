using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#pragma warning disable 1998    // async method without await

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
