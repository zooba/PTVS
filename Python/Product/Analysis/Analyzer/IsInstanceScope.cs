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
using System.Linq;
using Microsoft.PythonTools.Analysis.Values;
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    sealed class IsInstanceScope : InterpreterScope {
        public int _startIndex, _endIndex;
        public SuiteStatement _effectiveSuite;

        public IsInstanceScope(int startIndex, SuiteStatement effectiveSuite, InterpreterScope outerScope)
            : base(null, null, outerScope) {
            _startIndex = _endIndex = startIndex;
            _effectiveSuite = effectiveSuite;
        }

        public override string Name {
            get { return "<isinstance scope>"; }
        }

        public override int GetStart(PythonAst ast) {
            return ast.IndexToLocation(_startIndex).Index;
        }

        public override int GetStop(PythonAst ast) {
            return ast.IndexToLocation(_endIndex).Index;
        }

        public override bool AssignVariable(string name, Node location, AnalysisUnit unit, IAnalysisSet values) {
            var vars = CreateVariable(location, unit, name, false);

            var res = AssignVariableWorker(location, unit, values, vars);

            if (OuterScope != null) {
                var outerVar = OuterScope.GetVariable(location, unit, name, false);
                if (outerVar != null && outerVar != vars) {
                    outerVar.AddAssignment(location, unit);
                    outerVar.AddTypes(unit, values);
                }
            }

            return res;
        }

        public override VariableDef GetVariable(Node node, AnalysisUnit unit, string name, bool addRef = true) {
            return base.GetVariable(node, unit, name, addRef) ?? OuterScope.GetVariable(node, unit, name, addRef);
        }

        public override IEnumerable<VariableDef> GetMergedVariables(string name) {
            return base.GetMergedVariables(name).Concat(OuterScope.GetMergedVariables(name));
        }

        public override IAnalysisSet GetMergedVariableTypes(string name) {
            VariableDef res;
            if (TryGetVariable(name, out res)) {
                return res.Types;
            }
            return AnalysisSet.Empty;
        }

        public override IEnumerable<KeyValuePair<string, VariableDef>> GetAllMergedVariables() {
            return base.GetAllMergedVariables().Concat(OuterScope.GetAllMergedVariables());
        }

        public override VariableDef AddVariable(string name, VariableDef variable = null) {
            return OuterScope.AddVariable(name, variable);
        }

        internal override bool RemoveVariable(string name) {
            return OuterScope.RemoveVariable(name);
        }

        internal override void ClearVariables() {
            OuterScope.ClearVariables();
        }

        internal VariableDef CreateTypedVariable(Node node, AnalysisUnit unit, string name, IAnalysisSet types, bool addRef = true) {
            VariableDef res, outer, immediateOuter;
            if (!TryGetVariable(name, out res)) {
                // Normal CreateVariable would use AddVariable, which will put
                // the typed one in the wrong scope.
                res = base.AddVariable(name);
            }
            
            if (addRef) {
                res.AddReference(node, unit);
            }
            PropagateIsInstanceTypes(node, unit, types, res);

            foreach (var scope in OuterScope.EnumerateTowardsGlobal) {
                outer = scope.GetVariable(node, unit, name, addRef);
                if (scope.TryGetVariable(name, out immediateOuter) && immediateOuter != res) {
                    if (addRef && immediateOuter != outer) {
                        res.AddReference(node, unit);
                    }
                    PropagateIsInstanceTypes(node, unit, types, immediateOuter);

                    scope.AddLinkedVariable(name, res);
                }

                if (!(scope is IsInstanceScope)) {
                    break;
                }
            }
            return res;
        }

        private void PropagateIsInstanceTypes(Node node, AnalysisUnit unit, IAnalysisSet typeSet, VariableDef variable) {
            foreach (var typeObj in typeSet) {
                ClassInfo classInfo;
                BuiltinClassInfo builtinClassInfo;
                SequenceInfo seqInfo;

                if ((classInfo = typeObj as ClassInfo) != null) {
                    variable.AddTypes(unit, classInfo.Instance, false);
                } else if ((builtinClassInfo = typeObj as BuiltinClassInfo) != null) {
                    variable.AddTypes(unit, builtinClassInfo.Instance, false);
                } else if ((seqInfo = typeObj as SequenceInfo) != null) {
                    if (seqInfo.Push()) {
                        try {
                            foreach (var indexVar in seqInfo.IndexTypes) {
                                PropagateIsInstanceTypes(node, unit, indexVar.Types, variable);
                            }
                        } finally {
                            seqInfo.Pop();
                        }
                    }
                }
            }
        }

        public override IAnalysisSet AddNodeValue(Node node, IAnalysisSet variable) {
            return OuterScope.AddNodeValue(node, variable);
        }

        internal override bool RemoveNodeValue(Node node) {
            return OuterScope.RemoveNodeValue(node);
        }

        internal override void ClearNodeValues() {
            OuterScope.ClearNodeValues();
        }

        public int EndIndex {
            set {
                _endIndex = value;
            }
        }
    }
}
