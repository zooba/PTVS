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
    public class IfStatementTest : Node {
        private Expression _test;
        private Statement _body;
        private SourceSpan _beforeColon, _afterComment;

        public IfStatementTest() {
        }

        public Expression Test {
            get { return _test; }
            set { ThrowIfFrozen(); _test = value; }
        }

        public Statement Body {
            get { return _body; }
            set { ThrowIfFrozen(); _body = value; }
        }

        public SourceSpan BeforeColon {
            get { return _beforeColon; }
            set { ThrowIfFrozen(); _beforeColon = value; }
        }

        public SourceSpan AfterComment {
            get { return _afterComment; }
            set { ThrowIfFrozen(); _afterComment = value; }
        }

        internal override void AppendCodeString(StringBuilder output, PythonAst ast, CodeFormattingOptions format) {
            var t = ast.Tokenization;
            output.Append(t.GetTokenText(BeforeNode));
            output.Append(t.GetTokenText(Span));
            Test.AppendCodeString(output, ast, format);
            output.Append(t.GetTokenText(BeforeColon));
            output.Append(':');
            Comment?.AppendCodeString(output, ast, format);
            output.Append(t.GetTokenText(AfterComment));
            Body.AppendCodeString(output, ast, format);
            output.Append(t.GetTokenText(AfterNode));
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_test != null) {
                    _test.Walk(walker);
                }
                if (_body != null) {
                    _body.Walk(walker);
                }
            }
            walker.PostWalk(this);
        }
    }
}
