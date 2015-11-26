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
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class ParenthesisExpression : ExpressionWithExpression {
        private CommentExpression _firstComment;

        internal CommentExpression FirstComment {
            get { return _firstComment; }
            set { ThrowIfFrozen(); _firstComment = value; }
        }

        protected override void OnFreeze() {
            base.OnFreeze();
            _firstComment?.Freeze();
        }

        internal override void CheckAssign(Parser parser) {
            Expression?.CheckAssign(parser);
        }

        internal override void CheckAugmentedAssign(Parser parser) {
            Expression?.CheckAugmentedAssign(parser);
        }

        internal override void CheckDelete(Parser parser) {
            Expression?.CheckDelete(parser);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                base.Walk(walker);
            }
            walker.PostWalk(this);
        }

        internal override string CheckName => null;
    }
}
