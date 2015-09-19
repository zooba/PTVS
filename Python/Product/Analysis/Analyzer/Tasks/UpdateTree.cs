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

        public override ThreadPriority Priority {
            get { return ThreadPriority.Normal; }
        }

        public override async Task PerformAsync(
            PythonLanguageService analyzer,
            PythonFileContext context,
            CancellationToken cancellationToken
        ) {
            var parser = new Parser(
                await _item.Tokenization.GetAsync(),
                ParserOptions.Default
            );
            var result = parser.ParseFile();
            _item.Tree.SetValue(result.Tree);
            _item.ParseErrors.SetValue(result.Errors);

            await analyzer.EnqueueAsync(context, new UpdateMemberList(_item, result.Tree), cancellationToken);
            //analyzer.Enqueue(context, new UpdateAnalysis(_item, tree));
        }
    }
}
