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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;

namespace Microsoft.PythonTools.Analysis.Rules {
    class NameLookup : AnalysisRule {
        private readonly VariableKey _name;

        public NameLookup(AnalysisState state, VariableKey name, string target) : base(state, target) {
            _name = name;
        }

        public NameLookup(AnalysisState state, VariableKey name, IEnumerable<string> targets) : base(state, targets) {
            _name = name;
        }

        protected override async Task ApplyWorkerAsync(
            PythonLanguageService analyzer,
            AnalysisState state,
            RuleResults results,
            CancellationToken cancellationToken
        ) {
            var types = _name.GetTypes(state) ?? await _name.GetTypesAsync(cancellationToken);
            foreach (var t in Targets) {
                await results.AddTypesAsync(t, types, cancellationToken);
            }
        }

        public override string ToString() {
            return string.Format("{0} -> {{{1}}}", _name, string.Join(", ", Targets));
        }
    }
}
