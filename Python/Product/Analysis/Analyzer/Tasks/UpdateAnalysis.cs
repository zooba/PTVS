using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Analyzer.Tasks {
    sealed class UpdateAnalysis : QueueItem {
        private readonly PythonAst _tree;

        public UpdateAnalysis(PythonLanguageService.AnalysisState item, PythonAst tree)
            : base(item) {
            _tree = tree;
        }

        public override async Task PerformAsync(PythonLanguageService analyzer, CancellationToken cancellationToken) {
            // TODO: Implement
        }
    }
}
