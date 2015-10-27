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

namespace Microsoft.PythonTools.Parsing.Ast {
    public sealed class Arg : Node {
        private readonly Expression _name;
        private readonly Expression _expression;

        public Arg(Expression expression) : this(null, expression) { }

        public Arg(Expression name, Expression expression) {
            _name = name;
            _expression = expression;
        }

        public string Name {
            get {
                var nameExpr = _name as NameExpression;
                if (nameExpr != null) {
                    return nameExpr.Name;
                }
                return null;
            }
        }

        public Expression NameExpression {
            get {
                return _name;
            }
        }

        public Expression Expression {
            get { return _expression; }
        } 

        public override string ToString() {
            return base.ToString() + ":" + _name;
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_expression != null) {
                    _expression.Walk(walker);
                }
            }
            walker.PostWalk(this);
        }

        internal override void AppendCodeString(StringBuilder res, PythonAst ast, CodeFormattingOptions format)
        {
            if (_name != null) {
                if (Name == "*" || Name == "**") {
                    _name.AppendCodeString(res, ast, format);
                    _expression.AppendCodeString(res, ast, format);
                } else {
                    // keyword arg
                    _name.AppendCodeString(res, ast, format);
                    res.Append(this.GetPrecedingWhiteSpace(ast));
                    res.Append('=');
                    _expression.AppendCodeString(res, ast, format);
                }
            } else {
                _expression.AppendCodeString(res, ast, format);
            }
        }
    }
}
