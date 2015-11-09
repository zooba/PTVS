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
    public class FunctionDefinition : ScopeStatement {
        protected Statement _body;
        private NameExpression _name;
        private ParameterList _parameters;
        private SourceSpan _beforeReturnAnnotation;
        private Expression _returnAnnotation;
        private DecoratorStatement _decorators;
        private bool _generator, _coroutine;
        private IList<Expression> _returns;

        private PythonVariable _variable;      // The variable corresponding to the function name or null for lambdas
        internal PythonVariable _nameVariable; // the variable that refers to the global __name__

        public FunctionDefinition(TokenKind kind) : base(kind) { }

        protected override void OnFreeze() {
            base.OnFreeze();
            _parameters?.Freeze();
            _returns = FreezeList(_returns);
        }

        public bool IsLambda => Kind == TokenKind.KeywordLambda;

        public ParameterList Parameters {
            get { return _parameters; }
            set { ThrowIfFrozen(); _parameters = value; }
        }

        internal override int ArgCount => _parameters?.Parameters?.Count ?? 0;

        public SourceSpan BeforeReturnAnnotation {
            get { return _beforeReturnAnnotation; }
            set { ThrowIfFrozen(); _beforeReturnAnnotation = value; }
        }

        public Expression ReturnAnnotation {
            get { return _returnAnnotation; }
            set { ThrowIfFrozen(); _returnAnnotation = value; }
        }

        public override string Name {
            get { return _name?.Name ?? ""; }
        }

        public NameExpression NameExpression {
            get { return _name; }
            set { ThrowIfFrozen(); _name = value; }
        }

        public DecoratorStatement Decorators {
            get { return _decorators; }
            internal set { ThrowIfFrozen(); _decorators = value; }
        }

        public IList<Expression> Returns {
            get { return _returns; }
            set { ThrowIfFrozen(); _returns = value; }
        }

        internal void AddReturn(Expression expr) {
            if (Returns == null) {
                Returns = new List<Expression> { expr };
            } else {
                Returns.Add(expr);
            }
        }

        /// <summary>
        /// True if the function is a generator.  Generators contain at least one yield
        /// expression and instead of returning a value when called they return a generator
        /// object which implements the iterator protocol.
        /// </summary>
        public bool IsGenerator {
            get { return _generator; }
            set { ThrowIfFrozen(); _generator = value; }
        }

        /// <summary>
        /// True if the function is a coroutine. Coroutines are defined using
        /// 'async def'.
        /// </summary>
        public bool IsCoroutine {
            get { return _coroutine; }
            set { ThrowIfFrozen(); _coroutine = value; }
        }

        /// <summary>
        /// Gets the variable that this function is assigned to.
        /// </summary>
        public PythonVariable Variable {
            get { return _variable; }
            set { _variable = value; }
        }

        /// <summary>
        /// Gets the variable reference for the specific assignment to the variable for this function definition.
        /// </summary>
        public PythonReference GetVariableReference(PythonAst ast) {
            return GetVariableReference(this, ast);
        }

        internal override bool ExposesLocalVariable(PythonVariable variable) {
            return NeedsLocalsDictionary;
        }

        internal override bool TryBindOuter(ScopeStatement from, string name, bool allowGlobals, out PythonVariable variable) {
            // Functions expose their locals to direct access
            ContainsNestedFreeVariables = true;
            if (TryGetVariable(name, out variable)) {
                variable.AccessedInNestedScope = true;

                if (variable.Kind == VariableKind.Local || variable.Kind == VariableKind.Parameter) {
                    from.AddFreeVariable(variable, true);

                    for (ScopeStatement scope = from.Parent; scope != this; scope = scope.Parent) {
                        scope.AddFreeVariable(variable, false);
                    }

                    AddCellVariable(variable);
                } else if (allowGlobals) {
                    from.AddReferencedGlobal(name);
                }
                return true;
            }
            return false;
        }

        //internal override PythonVariable BindReference(PythonNameBinder binder, string name) {
        //    PythonVariable variable;

        //    // First try variables local to this scope
        //    if (TryGetVariable(name, out variable) && variable.Kind != VariableKind.Nonlocal) {
        //        if (variable.Kind == VariableKind.Global) {
        //            AddReferencedGlobal(name);
        //        }
        //        return variable;
        //    }

        //    // Try to bind in outer scopes
        //    for (ScopeStatement parent = Parent; parent != null; parent = parent.Parent) {
        //        if (parent.TryBindOuter(this, name, true, out variable)) {
        //            return variable;
        //        }
        //    }

        //    return null;
        //}


        //internal override void Bind(PythonNameBinder binder) {
        //    base.Bind(binder);
        //    Verify(binder);
        //}

        //private void Verify(PythonNameBinder binder) {
        //    if (ContainsImportStar && IsClosure) {
        //        binder.ReportSyntaxError(
        //            String.Format(
        //                System.Globalization.CultureInfo.InvariantCulture,
        //                "import * is not allowed in function '{0}' because it is a nested function",
        //                Name),
        //            this);
        //    }
        //    if (ContainsImportStar && Parent is FunctionDefinition) {
        //        binder.ReportSyntaxError(
        //            String.Format(
        //                System.Globalization.CultureInfo.InvariantCulture,
        //                "import * is not allowed in function '{0}' because it is a nested function",
        //                Name),
        //            this);
        //    }
        //    if (ContainsImportStar && ContainsNestedFreeVariables) {
        //        binder.ReportSyntaxError(
        //            String.Format(
        //                System.Globalization.CultureInfo.InvariantCulture,
        //                "import * is not allowed in function '{0}' because it contains a nested function with free variables",
        //                Name),
        //            this);
        //    }
        //    if (ContainsUnqualifiedExec && ContainsNestedFreeVariables) {
        //        binder.ReportSyntaxError(
        //            String.Format(
        //                System.Globalization.CultureInfo.InvariantCulture,
        //                "unqualified exec is not allowed in function '{0}' because it contains a nested function with free variables",
        //                Name),
        //            this);
        //    }
        //    if (ContainsUnqualifiedExec && IsClosure) {
        //        binder.ReportSyntaxError(
        //            String.Format(
        //                System.Globalization.CultureInfo.InvariantCulture,
        //                "unqualified exec is not allowed in function '{0}' because it is a nested function",
        //                Name),
        //            this);
        //    }
        //}

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _parameters?.Walk(walker);
                _decorators?.Walk(walker);
                _body?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
