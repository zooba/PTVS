/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;

namespace Microsoft.PythonTools.Parsing {
    [Serializable]
    public struct TokenInfo : IEquatable<TokenInfo> {
        public static readonly TokenInfo Empty = new TokenInfo(null, IndexSpan.Empty);

        public TokenCategory Category;
        public TokenTriggers Trigger;
        public int StartIndex;
        public int EndIndex;

        internal TokenInfo(Token token, IndexSpan span) {
            StartIndex = span.Start;
            EndIndex = span.End;

            GetCategory(token, out Category, out Trigger);
        }

        private static void GetCategory(Token token, out TokenCategory category, out TokenTriggers trigger) {
            category = TokenCategory.None;
            trigger = TokenTriggers.None;

            if (token == null) {
                return;
            }

            switch (token.Kind) {
                case TokenKind.EndOfFile:
                    category = TokenCategory.EndOfStream;
                    break;

                case TokenKind.Comment:
                    category = TokenCategory.Comment;
                    break;

                case TokenKind.Name:
                    if ("True".Equals(token.Value) || "False".Equals(token.Value)) {
                        category = TokenCategory.BuiltinIdentifier;
                    } else {
                        category = TokenCategory.Identifier;
                    }
                    break;

                case TokenKind.Error:
                    if (token is IncompleteStringErrorToken) {
                        category = TokenCategory.IncompleteMultiLineStringLiteral;
                    } else {
                        category = TokenCategory.Error;
                    }
                    break;

                case TokenKind.Constant:
                    if (token == Tokens.NoneToken) {
                        category = TokenCategory.BuiltinIdentifier;
                    } else if (token.Value is string || token.Value is AsciiString) {
                        category = TokenCategory.StringLiteral;
                    } else {
                        category = TokenCategory.NumericLiteral;
                    }
                    break;

                case TokenKind.LeftParenthesis:
                    category = TokenCategory.Grouping;
                    trigger = TokenTriggers.MatchBraces | TokenTriggers.ParameterStart;
                    break;

                case TokenKind.RightParenthesis:
                    category = TokenCategory.Grouping;
                    trigger = TokenTriggers.MatchBraces | TokenTriggers.ParameterEnd;
                    break;

                case TokenKind.LeftBracket:
                case TokenKind.LeftBrace:
                case TokenKind.RightBracket:
                case TokenKind.RightBrace:
                    category = TokenCategory.Grouping;
                    trigger = TokenTriggers.MatchBraces;
                    break;

                case TokenKind.Colon:
                    category = TokenCategory.Delimiter;
                    break;

                case TokenKind.Semicolon:
                    category = TokenCategory.Delimiter;
                    break;

                case TokenKind.Comma:
                    category = TokenCategory.Delimiter;
                    trigger = TokenTriggers.ParameterNext;
                    break;

                case TokenKind.Dot:
                    category = TokenCategory.Operator;
                    trigger = TokenTriggers.MemberSelect;
                    break;

                case TokenKind.NewLine:
                case TokenKind.NLToken:
                    category = TokenCategory.WhiteSpace;
                    break;

                case TokenKind.KeywordTrue:
                case TokenKind.KeywordFalse:
                    category = TokenCategory.Keyword;
                    break;

                case TokenKind.KeywordAsync:
                case TokenKind.KeywordAwait:
                    category = TokenCategory.Identifier;
                    break;

                default:
                    if (token.Kind >= TokenKind.FirstKeyword && token.Kind <= TokenKind.KeywordNonlocal) {
                        category = TokenCategory.Keyword;
                        break;
                    }

                    category = TokenCategory.Operator;
                    break;
            }
        }

        public bool IsValid {
            get {
                return EndIndex != StartIndex;
            }
        }

        public IndexSpan Span {
            get { return IndexSpan.FromPoints(StartIndex, EndIndex); }
        }

        public bool Equals(TokenInfo other) {
            return Category == other.Category && Trigger == other.Trigger &&
                StartIndex == other.StartIndex && EndIndex == other.EndIndex;
        }

        public override bool Equals(object obj) {
            return obj is TokenInfo && Equals((TokenInfo)obj);
        }

        public override int GetHashCode() {
            return StartIndex << 16 | EndIndex;
        }

        public override string ToString() {
            return string.Format("TokenInfo: [{0}, {1}), {2}, {3}", StartIndex, EndIndex, Category, Trigger);
        }
    }
}
