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
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {

    public class DelStatement : Statement {
        private IList<Expression> _expressions;

        public DelStatement() { }

        protected override void OnFreeze() {
            base.OnFreeze();
            _expressions = FreezeList(_expressions);
        }

        public IList<Expression> Expressions {
            get { return _expressions; }
            set { ThrowIfFrozen(); _expressions = value; }
        }

        public void AddExpression(Expression expr) {
            if (_expressions == null) {
                _expressions = new List<Expression>() { expr };
            } else {
                _expressions.Add(expr);
            }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_expressions != null) {
                    foreach (Expression expression in _expressions) {
                        expression.Walk(walker);
                    }
                }
            }
            walker.PostWalk(this);
        }
    }
}
