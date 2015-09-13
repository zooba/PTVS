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

            Category = TokenCategory.None;
            Trigger = TokenTriggers.None;

            switch (token.Kind) {
                case TokenKind.EndOfFile:
                    Category = TokenCategory.EndOfStream;
                    break;

                case TokenKind.Comment:
                    Category = TokenCategory.Comment;
                    break;

                case TokenKind.Name:
                    if ("True".Equals(token.Value) || "False".Equals(token.Value)) {
                        Category = TokenCategory.BuiltinIdentifier;
                    } else {
                        Category = TokenCategory.Identifier;
                    }
                    break;

                case TokenKind.Error:
                    if (token is IncompleteStringErrorToken) {
                        Category = TokenCategory.IncompleteMultiLineStringLiteral;
                    } else {
                        Category = TokenCategory.Error;
                    }
                    break;

                case TokenKind.Constant:
                    if (token == Tokens.NoneToken) {
                        Category = TokenCategory.BuiltinIdentifier;
                    } else if (token.Value is string || token.Value is AsciiString) {
                        Category = TokenCategory.StringLiteral;
                    } else {
                        Category = TokenCategory.NumericLiteral;
                    }
                    break;

                case TokenKind.LeftParenthesis:
                    Category = TokenCategory.Grouping;
                    Trigger = TokenTriggers.MatchBraces | TokenTriggers.ParameterStart;
                    break;

                case TokenKind.RightParenthesis:
                    Category = TokenCategory.Grouping;
                    Trigger = TokenTriggers.MatchBraces | TokenTriggers.ParameterEnd;
                    break;

                case TokenKind.LeftBracket:
                case TokenKind.LeftBrace:
                case TokenKind.RightBracket:
                case TokenKind.RightBrace:
                    Category = TokenCategory.Grouping;
                    Trigger = TokenTriggers.MatchBraces;
                    break;

                case TokenKind.Colon:
                    Category = TokenCategory.Delimiter;
                    break;

                case TokenKind.Semicolon:
                    Category = TokenCategory.Delimiter;
                    break;

                case TokenKind.Comma:
                    Category = TokenCategory.Delimiter;
                    Trigger = TokenTriggers.ParameterNext;
                    break;

                case TokenKind.Dot:
                    Category = TokenCategory.Operator;
                    Trigger = TokenTriggers.MemberSelect;
                    break;

                case TokenKind.NewLine:
                case TokenKind.NLToken:
                    Category = TokenCategory.WhiteSpace;
                    break;

                case TokenKind.KeywordTrue:
                case TokenKind.KeywordFalse:
                    Category = TokenCategory.Keyword;
                    break;

                case TokenKind.KeywordAsync:
                case TokenKind.KeywordAwait:
                    Category = TokenCategory.Identifier;
                    break;

                default:
                    if (token.Kind >= TokenKind.FirstKeyword && token.Kind <= TokenKind.KeywordNonlocal) {
                        Category = TokenCategory.Keyword;
                        break;
                    }

                    Category = TokenCategory.Operator;
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
