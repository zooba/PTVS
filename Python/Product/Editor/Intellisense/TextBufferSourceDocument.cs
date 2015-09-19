using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Parsing;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.PythonTools.Editor.Intellisense {
    sealed class TextBufferSourceDocument : ISourceDocument {
        private static readonly Encoding DefaultEncoding = new UTF8Encoding(false);
        private readonly string _moniker;
        private readonly ITextBuffer _buffer;
        private readonly ITextDocument _document;

        public TextBufferSourceDocument(ITextBuffer buffer) {
            _buffer = buffer;

            if (!_buffer.Properties.TryGetProperty(typeof(ITextDocument), out _document)) {
                _moniker = string.Format("<string:{0:N}>", Guid.NewGuid());
            } else {
                _moniker = _document.FilePath;
            }
        }

        public string Moniker {
            get { return _moniker; }
        }

        public string ActualFilePath {
            get { return _document == null ? null : _document.FilePath; }
        }

        public Encoding Encoding {
            get { return _document == null ? DefaultEncoding : _document.Encoding; }
        }

        public async Task<Stream> ReadAsync() {
            return new TextSnapshotStream(_buffer.CurrentSnapshot, Encoding);
        }

    }
}
