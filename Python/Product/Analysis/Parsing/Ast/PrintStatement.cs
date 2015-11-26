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
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class PrintStatement : Statement {
        private Expression _dest;
        private IList<Expression> _expressions;
        private SourceSpan _beforeLeftShift, _beforeDestination, _beforeItems;

        public Expression Destination {
            get { return _dest; }
            set { ThrowIfFrozen(); _dest = value; }
        }

        public IList<Expression> Expressions {
            get { return _expressions; }
            set { ThrowIfFrozen(); _expressions = value; }
        }

        protected override void OnFreeze() {
            base.OnFreeze();
            _expressions = FreezeList(_expressions);
        }

        public void AddExpression(Expression expression) {
            if (_expressions == null) {
                _expressions = new List<Expression>();
            }
            _expressions.Add(expression);
        }

        public SourceSpan BeforeLeftShift {
            get { return _beforeLeftShift; }
            set { ThrowIfFrozen(); _beforeLeftShift = value; }
        }

        public SourceSpan BeforeDestination {
            get { return _beforeDestination; }
            set { ThrowIfFrozen(); _beforeDestination = value; }
        }

        public SourceSpan BeforeItems {
            get { return _beforeItems; }
            set { ThrowIfFrozen(); _beforeItems = value; }
        }


        public bool TrailingComma => Expressions?.LastOrDefault() is EmptyExpression;

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                Destination?.Walk(walker);
                if (Expressions != null) {
                    foreach (Expression expression in Expressions) {
                        expression.Walk(walker);
                    }
                }
            }
            walker.PostWalk(this);
        }
    }
}
