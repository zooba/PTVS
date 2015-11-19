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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.PythonTools.Analysis.Values;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    class VariableWalker : PythonWalker {
        private readonly AnalysisState _state;
        private readonly Dictionary<string, Variable> _vars;

        public VariableWalker(
            AnalysisState state,
            IReadOnlyDictionary<string, Variable> current
        ) {
            _vars = new Dictionary<string, Variable>();
            if (current != null) {
                foreach (var kv in current) {
                    _vars[kv.Key] = kv.Value;
                }
            }
        }

        public IReadOnlyDictionary<string, Variable> Variables => _vars;

        private void Add(string key, Func<AnalysisValue> value) {
            if (_vars.ContainsKey(key)) {
                return;
            }

            var v = new Variable(_state, key);
            v.AddType(value?.Invoke());
            _vars[key] = v;
        }

        public override bool Walk(AssignmentStatement node) {
            if (node.Targets != null) {
                foreach (var n in node.Targets.OfType<NameExpression>()) {
                    Add(n.Prefix + n.Name, null);
                }
            }

            return false;
        }

        public override bool Walk(ClassDefinition node) {
            Add(node.Name, () => new ClassInfo(node));

            return false;
        }

        public override bool Walk(FunctionDefinition node) {
            Add(node.Name, () => new FunctionInfo(node));

            return false;
        }
    }
}
