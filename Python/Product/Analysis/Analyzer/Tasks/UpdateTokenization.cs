using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Parsing;
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Analyzer.Tasks {
    sealed class UpdateTokenization : QueueItem {
        private readonly ISourceDocument _document;

        public UpdateTokenization(AnalysisState item, ISourceDocument document)
            : base(item) {
            _document = document;
        }

        public override async Task PerformAsync(PythonLanguageService analyzer, CancellationToken cancellationToken) {
            var tokenizer = await Tokenization.TokenizeAsync(
                _document,
                analyzer.Configuration.Version,
                TokenizerOptions.Verbatim,
                // TODO: Get correct severeity
                Severity.Warning
            );
            _item.Tokenization.SetValue(tokenizer);

            analyzer.Enqueue(new UpdateTree(_item));
        }
    }
}
