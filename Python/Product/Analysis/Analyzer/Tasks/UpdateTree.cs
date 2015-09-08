using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Parsing;
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Analyzer.Tasks {
    sealed class UpdateTree : QueueItem {
        private readonly ISourceDocument _document;

        public UpdateTree(PythonLanguageService.AnalysisState item, ISourceDocument document)
            : base(item) {
            _document = document;
        }

        public override async Task PerformAsync(PythonLanguageService analyzer, CancellationToken cancellationToken) {
            var parser = Parser.CreateParser(
                await _document.ReadAsync(),
                analyzer.Configuration.Version,
                new ParserOptions { Verbatim = true }
            );
            PythonAst tree;
            using (parser) {
                tree = parser.ParseFile();
            }
            _item.Tree.SetValue(tree);

            analyzer.Enqueue(new UpdateMemberList(_item, tree));
            //analyzer.Enqueue(new UpdateAnalysis(_item, tree));
        }
    }
}
