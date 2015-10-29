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
    public class WhileStatement : Statement {
        private Expression _test;
        private Statement _body, _else;
        private CommentExpression _elseComment;
        private SourceSpan _beforeElseColon, _afterComment, _afterElseComment;

        public WhileStatement() { }

        public Expression Test {
            get { return _test; }
            set { ThrowIfFrozen(); _test = value; }
        }

        public Statement Body {
            get { return _body; }
            set { ThrowIfFrozen(); _body = value; }
        }

        public Statement Else {
            get { return _else; }
            set { ThrowIfFrozen(); _else = value; }
        }

        public SourceSpan BeforeElseColon {
            get { return _beforeElseColon; }
            set { ThrowIfFrozen(); _beforeElseColon = value; }
        }

        public SourceSpan AfterComment {
            get { return _afterComment; }
            set { ThrowIfFrozen(); _afterComment = value; }
        }

        public CommentExpression ElseComment {
            get { return _elseComment; }
            set { ThrowIfFrozen(); _elseComment = value; }
        }

        public SourceSpan AfterElseComment {
            get { return _afterElseComment; }
            set { ThrowIfFrozen(); _afterElseComment = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _test?.Walk(walker);
                _body?.Walk(walker);
                _else?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
