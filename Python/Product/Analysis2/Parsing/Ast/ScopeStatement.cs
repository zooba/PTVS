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

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {

    public abstract class ScopeStatement : CompoundStatement {
        private IList<string> _locals, _cells;

        protected ScopeStatement(TokenKind kind) : base(kind) { }

        public virtual string Name => "<unknown>";

        protected override void OnFreeze() {
            base.OnFreeze();
            _locals = FreezeList(_locals);
            _cells = FreezeList(_cells);
        }

        public IList<string> LocalDefinitions => _locals;
        public IList<string> LocalCells => _cells;

        internal void AddLocalDefinition(string name) {
            if (_locals == null) {
                _locals = new List<string> { name };
            } else if (!_locals.Contains(name)) {
                _locals.Add(name);
            }
        }

        internal void AddLocalCell(string name) {
            if (_cells == null) {
                _cells = new List<string> { name };
            } else if (!_cells.Contains(name)) {
                _cells.Add(name);
            }
        }

        //internal virtual bool TryBindOuter(ScopeStatement from, string name, bool allowGlobals, out PythonVariable variable) {
        //    // Hide scope contents by default (only functions expose their locals)
        //    variable = null;
        //    return false;
        //}

        //internal abstract PythonVariable BindReference(PythonNameBinder binder, string name);

        //internal virtual void Bind(PythonNameBinder binder) {
        //    if (_references != null) {
        //        foreach (var refList in _references.Values) {
        //            foreach (var reference in refList) {
        //                PythonVariable variable;
        //                reference.Variable = variable = BindReference(binder, reference.Name);

        //                // Accessing outer scope variable which is being deleted?
        //                if (variable != null) {
        //                    if (variable.Deleted && variable.Scope != this && !variable.Scope.IsGlobal && binder.LanguageVersion < PythonLanguageVersion.V32) {

        //                        // report syntax error
        //                        binder.ReportSyntaxError(
        //                            String.Format(
        //                                System.Globalization.CultureInfo.InvariantCulture,
        //                                "can not delete variable '{0}' referenced in nested scope",
        //                                reference.Name
        //                                ),
        //                            this);
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}

        //internal virtual void FinishBind(PythonNameBinder binder) {
        //    List<ClosureInfo> closureVariables = null;

        //    if (_nonLocalVars != null) {
        //        foreach (var variableName in _nonLocalVars) {
        //            bool bound = false;
        //            for (ScopeStatement parent = Parent; parent != null; parent = parent.Parent) {
        //                PythonVariable variable;

        //                if (parent.TryBindOuter(this, variableName.Name, false, out variable)) {
        //                    bound = !variable.IsGlobal;
        //                    break;
        //                }
        //            }

        //            if (!bound) {
        //                binder.ReportSyntaxError(String.Format("no binding for nonlocal '{0}' found", variableName.Name), variableName);
        //            }
        //        }
        //    }

        //    if (FreeVariables != null && FreeVariables.Count > 0) {
        //        closureVariables = new List<ClosureInfo>();

        //        foreach (var variable in FreeVariables) {
        //            closureVariables.Add(new ClosureInfo(variable, !(this is ClassDefinition)));
        //        }
        //    }

        //    if (Variables != null && Variables.Count > 0) {
        //        if (closureVariables == null) {
        //            closureVariables = new List<ClosureInfo>();
        //        }

        //        foreach (PythonVariable variable in Variables.Values) {
        //            if (!HasClosureVariable(closureVariables, variable) &&
        //                !variable.IsGlobal && (variable.AccessedInNestedScope || ExposesLocalVariable(variable))) {
        //                closureVariables.Add(new ClosureInfo(variable, true));
        //            }

        //            if (variable.Kind == VariableKind.Local) {
        //                Debug.Assert(variable.Scope == this);
        //            }
        //        }
        //    }

        //    if (closureVariables != null) {
        //        _closureVariables = closureVariables.ToArray();
        //    }

        //    // no longer needed
        //    _references = null;
        //}

        //private static bool HasClosureVariable(List<ClosureInfo> closureVariables, PythonVariable variable) {
        //    if (closureVariables == null) {
        //        return false;
        //    }

        //    for (int i = 0; i < closureVariables.Count; i++) {
        //        if (closureVariables[i].Variable == variable) {
        //            return true;
        //        }
        //    }

        //    return false;
        //}

        //private void EnsureVariables() {
        //    if (_variables == null) {
        //        _variables = new Dictionary<string, PythonVariable>(StringComparer.Ordinal);
        //    }
        //}

        //internal void AddVariable(PythonVariable variable) {
        //    EnsureVariables();
        //    _variables[variable.Name] = variable;
        //}

        //internal PythonReference Reference(string/*!*/ name) {
        //    if (_references == null) {
        //        _references = new Dictionary<string, List<PythonReference>>(StringComparer.Ordinal);
        //    }
        //    List<PythonReference> references;
        //    if (!_references.TryGetValue(name, out references)) {
        //        _references[name] = references = new List<PythonReference>();
        //    }
        //    var reference = new PythonReference(name);
        //    references.Add(reference);
        //    return reference;
        //}

        //internal bool IsReferenced(string name) {
        //    return _references != null && _references.ContainsKey(name);
        //}

        //internal PythonVariable/*!*/ CreateVariable(string name, VariableKind kind) {
        //    EnsureVariables();
        //    PythonVariable variable;
        //    _variables[name] = variable = new PythonVariable(name, kind, this);
        //    return variable;
        //}

        //internal PythonVariable/*!*/ EnsureVariable(string/*!*/ name) {
        //    PythonVariable variable;
        //    if (!TryGetVariable(name, out variable)) {
        //        return CreateVariable(name, VariableKind.Local);
        //    }
        //    return variable;
        //}

        //internal PythonVariable DefineParameter(string name) {
        //    return CreateVariable(name, VariableKind.Parameter);
        //}

        //struct ClosureInfo {
        //    public PythonVariable Variable;
        //    public bool AccessedInScope;

        //    public ClosureInfo(PythonVariable variable, bool accessedInScope) {
        //        Variable = variable;
        //        AccessedInScope = accessedInScope;
        //    }
        //}
    }
}
