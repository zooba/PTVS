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
    public class ConditionalExpression : ExpressionWithExpression {
        private Expression _trueExpr, _falseExpr;

        protected override void OnFreeze() {
            base.OnFreeze();
            _falseExpr?.Freeze();
            _trueExpr?.Freeze();
        }

        public Expression FalseExpression {
            get { return _falseExpr; }
            set { ThrowIfFrozen(); _falseExpr = value; }
        }

        public Expression TrueExpression {
            get { return _trueExpr; }
            set { ThrowIfFrozen(); _trueExpr = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                base.Walk(walker);
                _trueExpr?.Walk(walker);
                _falseExpr?.Walk(walker);
            }
            walker.PostWalk(this);
        }

        internal override string CheckName => "conditional expression";
    }
}
