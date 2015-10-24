using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    public interface ISourceDocument {
        Task<Stream> ReadAsync();

        string Moniker { get; }
    }

    public sealed class StringLiteralDocument : ISourceDocument {
        private string _document;
        private readonly string _moniker;

        public StringLiteralDocument(string document, string moniker = "<string>") {
            _document = document;
            _moniker = moniker;
        }

        public string Document {
            get { return _document; }
            set { _document = value; }
        }

        public string Moniker {
            get { return _moniker; }
        }

        public async Task<Stream> ReadAsync() {
            var ms = new MemoryStream();
            using (var sw = new StreamWriter(ms, new UTF8Encoding(false), 4096, true)) {
                await sw.WriteAsync(_document);
            }
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }
    }
}
