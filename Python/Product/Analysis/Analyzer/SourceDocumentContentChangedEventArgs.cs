using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    public class SourceDocumentContentChangedEventArgs : EventArgs {
        private readonly ISourceDocument _document;

        public SourceDocumentContentChangedEventArgs(ISourceDocument document) {
            _document = document;
        }

        public ISourceDocument Document {
            get { return _document; }
        }
    }
}
