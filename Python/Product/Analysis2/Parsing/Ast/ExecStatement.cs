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


using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    
    public class ExecStatement : StatementWithExpression {
        private Expression _code, _locals, _globals;

        protected override void OnFreeze() {
            base.OnFreeze();
            _code = Code;
            _locals = Locals;
            _globals = Globals;
            _code?.Freeze();
            _locals?.Freeze();
            _globals?.Freeze();
        }

        internal void CheckSyntax(Parser parser) {
            if (Expression == null) {
                parser.ReportError();
                return;
            }

            TupleExpression te;
            ParenthesisExpression pe;
            BinaryExpression be;
            if ((te = Expression as TupleExpression) != null) {
                if (te.Count == 2) {
                    if ((be = te.Items[0] as BinaryExpression) != null && be.Operator == PythonOperator.In) {
                        // exec code in globals, locals
                        return;
                    }
                    // exec(code, globals, locals)
                    return;
                }
            }

            if ((be = Expression as BinaryExpression) != null) {
                if (be.Operator == PythonOperator.In) {
                    // exec code in globals
                } else if (be.Operator == PythonOperator.And ||
                    be.Operator == PythonOperator.Equal ||
                    be.Operator == PythonOperator.GreaterThan ||
                    be.Operator == PythonOperator.GreaterThanOrEqual ||
                    be.Operator == PythonOperator.Is ||
                    be.Operator == PythonOperator.IsNot ||
                    be.Operator == PythonOperator.LessThan ||
                    be.Operator == PythonOperator.LessThanOrEqual ||
                    be.Operator == PythonOperator.NotEqual ||
                    be.Operator == PythonOperator.NotIn ||
                    be.Operator == PythonOperator.Or) {
                    parser.ReportError(errorAt: be.OperatorSpan);
                }
                // exec code_expr
                return;
            }

            if ((pe = Expression as ParenthesisExpression) != null) {
                if ((te = pe.Expression as TupleExpression) != null) {
                    // exec(code, globals, locals)
                    if (te.Count > 3) {
                        parser.ReportError(errorAt: new SourceSpan(te.Items[3].Span.Start, te.Span.End));
                    }
                    return;
                }
                // exec(code)
                return;
            }

            // exec code
        }

        public Expression Code {
            get {
                if (Expression == null || _code != null) {
                    return _code;
                }

                TupleExpression te;
                ParenthesisExpression pe;
                BinaryExpression be;
                te = (Expression as TupleExpression) ??
                    ((Expression as ParenthesisExpression)?.Expression as TupleExpression);
                if (te != null) {
                    if (te.Count >= 1) {
                        if ((be = te.Items[0] as BinaryExpression) != null && be.Operator == PythonOperator.In) {
                            // exec code in globals, locals
                            return be.Left;
                        }
                        // exec(code, globals, locals)
                        return te.Items[0];
                    }
                }

                if ((pe = Expression as ParenthesisExpression) != null) {
                    // exec(code)
                    return pe.Expression;
                }

                if ((be = Expression as BinaryExpression) != null && be.Operator == PythonOperator.In) {
                    // exec code in globals
                    return be.Left;
                }

                return Expression;
            }
        }

        public Expression Globals {
            get {
                if (Expression == null || _globals != null) {
                    return _globals;
                }

                TupleExpression te;
                BinaryExpression be;
                te = (Expression as TupleExpression) ??
                    ((Expression as ParenthesisExpression)?.Expression as TupleExpression);
                if (te != null) {
                    if (te.Count >= 1 && (be = te.Items[0] as BinaryExpression) != null &&
                        be.Operator == PythonOperator.In) {
                        // exec code in globals, locals
                        return be.Right;
                    } else if (te.Count >= 2) {
                        // exec(code, globals, locals)
                        return te.Items[1];
                    }
                }

                if ((be = Expression as BinaryExpression) != null) {
                    // exec code in globals
                    return be.Right;
                }

                return null;
            }
        }

        public Expression Locals {
            get {
                if (Expression == null || _locals != null) {
                    return _locals;
                }

                TupleExpression te;
                BinaryExpression be;
                te = (Expression as TupleExpression) ??
                    ((Expression as ParenthesisExpression)?.Expression as TupleExpression);
                if (te != null) {
                    if (te.Count >= 2 && (be = te.Items[0] as BinaryExpression) != null &&
                        be.Operator == PythonOperator.In) {
                        // exec code in globals, locals
                        return te.Items[1];
                    } else if (te.Count >= 3) {
                        // exec(code, globals, locals)
                        return te.Items[2];
                    }
                }

                return null;
            }
        }

        public bool NeedsLocalsDictionary() {
            return Globals == null && Locals == null;
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                base.Walk(walker);
                _locals?.Walk(walker);
                _globals?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
