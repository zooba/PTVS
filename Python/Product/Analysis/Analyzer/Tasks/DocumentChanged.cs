using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Analyzer.Tasks {
    sealed class DocumentChanged : QueueItem {
        private readonly ISourceDocument _document;

        public DocumentChanged(AnalysisState item, ISourceDocument document)
            : base(item) {
            _document = document;
        }

        public override ThreadPriority Priority {
            get { return ThreadPriority.AboveNormal; }
        }

        public override async Task PerformAsync(
            PythonLanguageService analyzer,
            PythonFileContext context,
            CancellationToken cancellationToken
        ) {
            _item.Document.SetValue(_document);
            await analyzer.EnqueueAsync(context, new UpdateTokenization(_item, _document), cancellationToken);
        }
    }
}
