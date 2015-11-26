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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.PythonTools.Analysis.Values;
using Microsoft.PythonTools.Infrastructure;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    class VariableWalker : PythonWalker {
        private readonly PythonLanguageService _analyzer;
        private readonly AnalysisState _state;
        private readonly Dictionary<string, Variable> _vars;
        private readonly List<AnalysisRule> _rules;

        public VariableWalker(
            PythonLanguageService analyzer,
            AnalysisState state,
            IReadOnlyDictionary<string, Variable> currentVariables,
            IEnumerable<AnalysisRule> currentRules
        ) {
            _analyzer = analyzer;
            _vars = new Dictionary<string, Variable>();
            _rules = new List<AnalysisRule>();
            if (currentVariables != null) {
                foreach (var kv in currentVariables) {
                    _vars[kv.Key] = kv.Value;
                }
            }
            if (currentRules != null) {
                _rules.AddRange(currentRules);
            }
        }

        public IReadOnlyDictionary<string, Variable> Variables => _vars;
        public IReadOnlyCollection<AnalysisRule> Rules => _rules;

        private void Add(string key, AnalysisValue value) {
            Variable v;
            if (!_vars.TryGetValue(key, out v)) {
                _vars[key] = v = new Variable(_state, key);
            }

            if (value != null) {
                v.AddType(value);
            }
        }

        private void Add(AnalysisRule rule) {
            _rules.Add(rule);
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
            Add(node.Name, new ClassInfo(node));

            return false;
        }

        public override bool Walk(FunctionDefinition node) {
            Add(node.Name, new FunctionInfo(node));

            return false;
        }

        public override bool Walk(ImportStatement node) {
            if (node.Names == null) {
                return false;
            }

            foreach (var n in node.Names) {
                var importName = ImportStatement.GetImportName(n);
                var asName = ImportStatement.GetAsName(n);
                var mi = _analyzer.ResolveImportAsync(importName, "", CancellationToken.None).WaitAndUnwrapExceptions();
                Add(asName, null);
                Add(new Rules.ImportFromModule(mi, ModuleInfo.VariableName, asName));
            }

            return false;
        }

        public override bool Walk(FromImportStatement node) {
            if (node.Names == null) {
                return false;
            }

            var import = node.Root.MakeString();
            var mi = _analyzer.ResolveImportAsync(import, "", CancellationToken.None).WaitAndUnwrapExceptions();

            var importNames = new List<string>();
            var asNames = new List<string>();
            foreach(var n in node.Names) {
                var importName = FromImportStatement.GetImportName(n);
                var asName = FromImportStatement.GetAsName(n);
                if (importName == null || asName == null) {
                    continue;
                }

                importNames.Add(importName);
                asNames.Add(asName);
                if (asName != "*") {
                    Add(asName, null);
                }
            }

            Add(new Rules.ImportFromModule(mi, importNames, asNames));

            return false;
        }
    }
}
