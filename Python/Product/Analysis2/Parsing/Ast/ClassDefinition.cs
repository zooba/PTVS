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
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class ClassDefinition : ScopeStatement {
        private NameExpression _name;
        private IList<Arg> _bases;
        private DecoratorStatement _decorators;

        private PythonVariable _variable;           // Variable corresponding to the class name
        private PythonVariable _modVariable;        // Variable for the the __module__ (module name)
        private PythonVariable _docVariable;        // Variable for the __doc__ attribute
        private PythonVariable _modNameVariable;    // Variable for the module's __name__
        private PythonVariable _classVariable;      // Variable for the classes __class__ cell var on 3.x

        public ClassDefinition() : base(TokenKind.KeywordClass) { }

        protected override void OnFreeze() {
            base.OnFreeze();
            _bases = FreezeList(_bases);
        }

        public override string Name {
            get { return _name?.Name ?? ""; }
        }

        public NameExpression NameExpression {
            get { return _name; }
            set { ThrowIfFrozen(); _name = value; }
        }

        public IList<Arg> Bases {
            get { return _bases; }
            set { ThrowIfFrozen(); _bases = value; }
        }

        public DecoratorStatement Decorators {
            get { return _decorators; }
            internal set { ThrowIfFrozen(); _decorators = value; }
        }

        /// <summary>
        /// Gets the variable that this class definition was assigned to.
        /// </summary>
        public PythonVariable Variable {
            get { return _variable; }
            set { _variable = value; }
        }

        /// <summary>
        /// Gets the variable reference for the specific assignment to the variable for this class definition.
        /// </summary>
        public PythonReference GetVariableReference(PythonAst ast) {
            return GetVariableReference(this, ast);
        }

        internal PythonVariable ClassVariable {
            get { return _classVariable; }
            set { _classVariable = value; }
        }

        internal PythonVariable ModVariable {
            get { return _modVariable; }
            set { _modVariable = value; }
        }

        internal PythonVariable DocVariable {
            get { return _docVariable; }
            set { _docVariable = value; }
        }

        internal PythonVariable ModuleNameVariable {
            get { return _modNameVariable; }
            set { _modNameVariable = value; }
        }

        internal override bool HasLateBoundVariableSets {
            get {
                return base.HasLateBoundVariableSets || NeedsLocalsDictionary;
            }
            set {
                base.HasLateBoundVariableSets = value;
            }
        }
        
        internal override bool NeedsLocalContext {
            get {
                return true;
            }
        }

        internal override bool ExposesLocalVariable(PythonVariable variable) {
            return true;
        }

        internal override bool TryBindOuter(ScopeStatement from, string name, bool allowGlobals, out PythonVariable variable) {
            if (name == "__class__" && _classVariable != null) {
                // 3.x has a cell var called __class__ which can be bound by inner scopes
                variable = _classVariable;
                return true;
            }

            return base.TryBindOuter(from, name, allowGlobals, out variable);
        }

        //internal override PythonVariable BindReference(PythonNameBinder binder, string name) {
        //    PythonVariable variable;

        //    // Python semantics: The variables bound local in the class
        //    // scope are accessed by name - the dictionary behavior of classes
        //    if (TryGetVariable(name, out variable)) {
        //        // TODO: This results in doing a dictionary lookup to get/set the local,
        //        // when it should probably be an uninitialized check / global lookup for gets
        //        // and a direct set
        //        if (variable.Kind == VariableKind.Global) {
        //            AddReferencedGlobal(name);
        //        } else if (variable.Kind == VariableKind.Local) {
        //            return null;
        //        }

        //        return variable;
        //    }

        //    // Try to bind in outer scopes, if we have an unqualified exec we need to leave the
        //    // variables as free for the same reason that locals are accessed by name.
        //    for (ScopeStatement parent = Parent; parent != null; parent = parent.Parent) {
        //        if (parent.TryBindOuter(this, name, true, out variable)) {
        //            return variable;
        //        }
        //    }

        //    return null;
        //}

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _decorators?.Walk(walker);
                if (_bases != null) {
                    foreach (var b in _bases) {
                        b.Walk(walker);
                    }
                }
                Body?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
