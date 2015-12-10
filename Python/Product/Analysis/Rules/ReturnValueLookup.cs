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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Values;
using Microsoft.PythonTools.Common.Infrastructure;

namespace Microsoft.PythonTools.Analysis.Rules {
    class ReturnValueLookup : AnalysisRule {
        private readonly VariableKey _function;

        public ReturnValueLookup(VariableKey function, string target) : base(target) {
            _function = function;
        }

        public ReturnValueLookup(VariableKey function, IEnumerable<string> targets) : base(targets) {
            _function = function;
        }

        protected override async Task<Dictionary<string, IAnalysisSet>> ApplyWorkerAsync(
            PythonLanguageService analyzer,
            AnalysisState state,
            IReadOnlyDictionary<string, IAnalysisSet> priorResults,
            CancellationToken cancellationToken
        ) {
            var callables = _function.GetTypes(state) ?? await _function.GetTypesAsync(cancellationToken);
            if (callables == null) {
                return null;
            }

            var vars = state.GetVariables();
            var values = new AnalysisSet();
            foreach (var func in callables.OfType<FunctionValue>()) {
                var returnKey = func.Key + "#$r";
                var types = returnKey.GetTypes(state) ?? await returnKey.GetTypesAsync(cancellationToken);
                foreach (var t in types.MaybeEnumerate()) {
                    var p = t as ParameterValue;
                    if (p != null) {
                        var pTypes = p.Key.GetTypes(state) ?? await p.Key.GetTypesAsync(cancellationToken);
                        if (pTypes != null) {
                            values.AddRange(pTypes);
                        }
                        var pKey = p.GetCallKey(_function);
                        pTypes = pKey.GetTypes(state) ?? await pKey.GetTypesAsync(cancellationToken);
                        if (pTypes != null) {
                            values.AddRange(pTypes);
                        }
                    } else {
                        values.Add(t);
                    }
                }
            }

            foreach (var t in callables.OfType<BuiltinFunctionInfo>()) {
                
            }

            var result = new Dictionary<string, IAnalysisSet>();
            bool anyChanged = false;
            foreach (var target in Targets) {
                result[target] = values;

                if (!anyChanged && !AreSame(target, priorResults, values)) {
                    anyChanged = true;
                }
            }

            return anyChanged ? result : null;
        }

        public override string ToString() {
            return string.Format("Call{{{0}}} -> {{{1}}}", _function, string.Join(", ", Targets));
        }
    }
}
