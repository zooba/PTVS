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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Parsing {
    public struct Token : IEquatable<Token> {
        public static readonly Token Empty = new Token();

        public TokenCategory Category;
        public SourceSpan Span;

        public Token(TokenCategory category, SourceLocation start, SourceLocation end) {
            Category = category;
            Span = new SourceSpan(start, end);
        }

        public Token(TokenCategory category, SourceLocation start, int length) {
            Category = category;
            Span = new SourceSpan(start, new SourceLocation(start.Index + length, start.Line, start.Column + length));
        }

        public override string ToString() {
            return $"{Category} ({Span})";
        }

        public override bool Equals(object obj) {
            if (!(obj is Token)) {
                return false;
            }
            return Equals((Token)obj);
        }

        public bool Equals(Token other) {
            return Category == other.Category && Span == other.Span;
        }

        public override int GetHashCode() {
            return Span.GetHashCode();
        }

        /// <summary>
        /// Gets the most specific token kind for this token.
        /// </summary>
        internal TokenKind GetTokenKind(Tokenization tokenization) {
            switch (Category) {
                case TokenCategory.None:
                    return TokenKind.Unknown;
                case TokenCategory.EndOfStream:
                    return TokenKind.EndOfFile;
                case TokenCategory.EndOfLine:
                    return TokenKind.NewLine;
                case TokenCategory.IgnoreEndOfLine:
                    return TokenKind.ExplicitLineJoin;
                case TokenCategory.Comment:
                    return TokenKind.Comment;
                case TokenCategory.DecimalIntegerLiteral:
                case TokenCategory.OctalIntegerLiteral:
                case TokenCategory.HexadecimalIntegerLiteral:
                case TokenCategory.BinaryIntegerLiteral:
                case TokenCategory.FloatingPointLiteral:
                case TokenCategory.ImaginaryLiteral:
                case TokenCategory.StringLiteral:
                    return TokenKind.Constant;
                case TokenCategory.Comma:
                    return TokenKind.Comma;
                case TokenCategory.Period:
                    return TokenKind.Dot;
                case TokenCategory.SemiColon:
                    return TokenKind.Semicolon;
                case TokenCategory.Colon:
                    return TokenKind.Colon;
                case TokenCategory.Error:
                    return TokenKind.Error;
                case TokenCategory.WhiteSpace:
                    return TokenKind.Whitespace;
                case TokenCategory.Operator:
                    return GetOperatorTokenKind(tokenization, this);
                case TokenCategory.Identifier:
                    return GetIdentifierTokenKind(tokenization, this);
                case TokenCategory.OpenGrouping:
                case TokenCategory.CloseGrouping:
                    return GetGroupingTokenKind(tokenization, this);
                case TokenCategory.OpenQuote:
                    return TokenKind.LeftQuote;
                case TokenCategory.CloseQuote:
                    return TokenKind.RightQuote;
                default:
                    return TokenKind.Unknown;
            }
        }

        // TODO: Maybe optimize token kind resolution?

        private static readonly Dictionary<string, TokenKind> Operators = new Dictionary<string, TokenKind> {
            { "+", TokenKind.Add },
            { "+=", TokenKind.AddEqual },
            { "-", TokenKind.Subtract },
            { "-=", TokenKind.SubtractEqual },
            { "**", TokenKind.Power },
            { "**=", TokenKind.PowerEqual },
            { "*", TokenKind.Multiply },
            { "*=", TokenKind.MultiplyEqual },
            { "@", TokenKind.MatMultiply },
            { "@=", TokenKind.MatMultiplyEqual },
            { "//", TokenKind.FloorDivide },
            { "//=", TokenKind.FloorDivideEqual },
            { "/", TokenKind.Divide },
            { "/=", TokenKind.DivideEqual },
            { "%", TokenKind.Mod },
            { "%=", TokenKind.ModEqual },
            { "<<", TokenKind.LeftShift },
            { "<<=", TokenKind.LeftShiftEqual },
            { ">>", TokenKind.RightShift },
            { ">>=", TokenKind.RightShiftEqual },
            { "&", TokenKind.BitwiseAnd },
            { "&=", TokenKind.BitwiseAndEqual },
            { "|", TokenKind.BitwiseOr },
            { "|=", TokenKind.BitwiseOrEqual },
            { "^", TokenKind.ExclusiveOr },
            { "^=", TokenKind.ExclusiveOrEqual },
            { "<", TokenKind.LessThan },
            { ">", TokenKind.GreaterThan },
            { "<=", TokenKind.LessThanOrEqual },
            { ">=", TokenKind.GreaterThanOrEqual },
            { "==", TokenKind.Equals },
            { "!=", TokenKind.NotEquals },
            { "<>", TokenKind.LessThanGreaterThan },
        };

        private static TokenKind GetOperatorTokenKind(Tokenization tokenization, Token token) {
            try {
                return Operators[tokenization.GetTokenText(token)];
            } catch (KeyNotFoundException) {
                Debug.Fail("Unhandled operator: " + tokenization.GetTokenText(token));
                return TokenKind.Unknown;
            }
        }

        private static readonly Dictionary<string, TokenKind> Groupings = new Dictionary<string, TokenKind> {
            { "(", TokenKind.LeftParenthesis },
            { ")", TokenKind.RightParenthesis },
            { "[", TokenKind.LeftBracket },
            { "]", TokenKind.RightBracket },
            { "{", TokenKind.LeftBrace },
            { "}", TokenKind.RightBrace },
        };

        private static TokenKind GetGroupingTokenKind(Tokenization tokenization, Token token) {
            try {
                return Groupings[tokenization.GetTokenText(token)];
            } catch (KeyNotFoundException) {
                Debug.Fail("Unhandled grouping: " + tokenization.GetTokenText(token));
                return TokenKind.Unknown;
            }
        }

        private static readonly Dictionary<string, TokenKind> Identifiers = new Dictionary<string, TokenKind> {
            { "and", TokenKind.KeywordAnd },
            { "assert", TokenKind.KeywordAssert },
            { "async", TokenKind.KeywordAsync },
            { "await", TokenKind.KeywordAwait },
            { "break", TokenKind.KeywordBreak },
            { "class", TokenKind.KeywordClass },
            { "continue", TokenKind.KeywordContinue },
            { "def", TokenKind.KeywordDef },
            { "del", TokenKind.KeywordDel },
            { "elif", TokenKind.KeywordElseIf },
            { "else", TokenKind.KeywordElse },
            { "except", TokenKind.KeywordExcept },
            { "exec", TokenKind.KeywordExec },
            { "finally", TokenKind.KeywordFinally },
            { "for", TokenKind.KeywordFor },
            { "from", TokenKind.KeywordFrom },
            { "global", TokenKind.KeywordGlobal },
            { "if", TokenKind.KeywordIf },
            { "import", TokenKind.KeywordImport },
            { "in", TokenKind.KeywordIn },
            { "is", TokenKind.KeywordIs },
            { "lambda", TokenKind.KeywordLambda },
            { "not", TokenKind.KeywordNot },
            { "or", TokenKind.KeywordOr },
            { "pass", TokenKind.KeywordPass },
            { "print", TokenKind.KeywordPrint },
            { "raise", TokenKind.KeywordRaise },
            { "return", TokenKind.KeywordReturn },
            { "try", TokenKind.KeywordTry },
            { "while", TokenKind.KeywordWhile },
            { "yield", TokenKind.KeywordYield },
            { "as", TokenKind.KeywordAs },
            { "with", TokenKind.KeywordWith },
            { "True", TokenKind.KeywordTrue },
            { "False", TokenKind.KeywordFalse },
            { "nonlocal", TokenKind.KeywordNonlocal },
        };

        private static TokenKind GetIdentifierTokenKind(Tokenization tokenization, Token token) {
            TokenKind kind;
            if (Identifiers.TryGetValue(tokenization.GetTokenText(token), out kind)) {
                return kind;
            }
            return TokenKind.Name;
        }
    }
}
