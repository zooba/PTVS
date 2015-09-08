using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Analyzer.Tasks {
    sealed class DocumentChanged : QueueItem {
        private readonly ISourceDocument _document;

        public DocumentChanged(PythonLanguageService.AnalysisState item, ISourceDocument document)
            : base(item) {
            _document = document;
        }

        public override async Task PerformAsync(PythonLanguageService analyzer, CancellationToken cancellationToken) {
            _item.Document.SetValue(_document);
            analyzer.Enqueue(new UpdateTree(_item, _document));
        }
    }
}
