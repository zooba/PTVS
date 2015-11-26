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
    public class StarredExpression : Expression {
        private readonly TokenKind _kind;
        private readonly Expression _expr;

        public StarredExpression(TokenKind kind, Expression expr) {
            _kind = kind;
            _expr = expr;
        }

        public bool IsStar => _kind == TokenKind.Multiply;
        public bool IsDoubleStar => _kind == TokenKind.Power;

        public Expression Expression {
            get { return _expr; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _expr.Walk(walker);
            }
        }

        internal override void CheckAssign(Parser parser) {
            if (!parser.HasStarUnpacking) {
                parser.ReportError("invalid syntax", Span);
            }
        }

        internal override void CheckAugmentedAssign(Parser parser) {
            parser.ReportError("illegal expression for augmented assignment", Span);
        }

        internal override void CheckDelete(Parser parser) {
            if (parser.HasGeneralUnpacking) {
                parser.ReportError("can't use starred expression here", Span);
            } else {
                parser.ReportError("invalid syntax", Span);
            }
        }

        internal override string CheckName => null;
    }
}
