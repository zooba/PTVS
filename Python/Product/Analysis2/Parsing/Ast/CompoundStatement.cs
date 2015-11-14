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
    /// <summary>
    /// Represents any statement that has a colon followed by a suite.
    /// </summary>
    public class CompoundStatement : StatementWithExpression {
        private readonly TokenKind _kind;
        private Statement _body;
        private SourceSpan _afterAsync, _beforeColon, _afterComment, _afterBody;

        public CompoundStatement(TokenKind kind) {
            _kind = kind;
            Span = SourceSpan.Invalid;
        }

        public TokenKind Kind => _kind;

        public bool IsAsync => _afterAsync.Length > 0;

        protected override void OnFreeze() {
            base.OnFreeze();
            _body?.Freeze();
        }

        public Statement Body {
            get { return _body; }
            set { ThrowIfFrozen(); _body = value; }
        }

        public SourceSpan AfterAsync {
            get { return _afterAsync; }
            set { ThrowIfFrozen(); _afterAsync = value; }
        }

        public SourceSpan BeforeColon {
            get { return _beforeColon; }
            set { ThrowIfFrozen(); _beforeColon = value; }
        }

        public SourceSpan AfterComment {
            get { return _afterComment; }
            set { ThrowIfFrozen(); _afterComment = value; }
        }

        public SourceSpan AfterBody {
            get { return _afterBody; }
            set { ThrowIfFrozen(); _afterBody = value; }
        }

        internal override void AppendCodeString(StringBuilder output, PythonAst ast, CodeFormattingOptions format) {
            BeforeNode.AppendCodeString(output, ast);

            if (AfterAsync.Length > 0) {
                output.Append("async");
                AfterAsync.AppendCodeString(output, ast);
            }

            switch (_kind) {
                case TokenKind.KeywordIf:
                    output.Append("if");
                    break;
                case TokenKind.KeywordElseIf:
                    output.Append("elif");
                    break;
                case TokenKind.KeywordElse:
                    output.Append("else");
                    break;
                case TokenKind.KeywordTry:
                    output.Append("try");
                    break;
                case TokenKind.KeywordExcept:
                    output.Append("except");
                    break;
                case TokenKind.KeywordFinally:
                    output.Append("finally");
                    break;
                case TokenKind.KeywordWith:
                    output.Append("with");
                    break;
                default:
                    throw new NotSupportedException("Cannot format statement " + _kind);
            }
            Expression?.AppendCodeString(output, ast, format);
            BeforeColon.AppendCodeString(output, ast);
            output.Append(":");
            Comment?.AppendCodeString(output, ast, format);
            AfterComment.AppendCodeString(output, ast);
            Body?.AppendCodeString(output, ast, format);
            AfterBody.AppendCodeString(output, ast);
        }

        public override void Walk(PythonWalker walker) {
            base.Walk(walker);
            Body?.Walk(walker);
        }
    }
}
