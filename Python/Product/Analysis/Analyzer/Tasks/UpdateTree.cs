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
        public UpdateTree(AnalysisState item)
            : base(item) { }

        public override async Task PerformAsync(PythonLanguageService analyzer, CancellationToken cancellationToken) {
            var parser = new Parser(
                await _item.Tokenization.GetAsync(),
                ParserOptions.Default
            );
            var result = parser.ParseFile();
            _item.Tree.SetValue(result.Tree);
            _item.ParseErrors.SetValue(result.Errors);

            analyzer.Enqueue(new UpdateMemberList(_item, result.Tree));
            //analyzer.Enqueue(new UpdateAnalysis(_item, tree));
        }
    }
}
