// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Analyzer.Tasks {
    sealed class UpdateVariables : QueueItem {
        public UpdateVariables(AnalysisState item)
            : base(item) { }

        public override async Task PerformAsync(CancellationToken cancellationToken) {
            var state = _state as AnalysisState;
            if (state == null) {
                return;
            }

            var parser = new Parser(await _state.GetTokenizationAsync(cancellationToken));
            var errors = new CollectingErrorSink();
            var ast = parser.Parse(errors);
            state.SetAst(ast, errors.Errors);

            var walker = new VariableWalker(state.Analyzer, state, state.GetVariables(), state.GetRules());
            var variables = walker.WalkVariables(ast);
            var rules = walker.WalkRules(ast);
            state.SetVariablesAndRules(variables, rules);

            state.Analyzer.Enqueue(state.Context, new UpdateRules(state));
        }
    }
}
