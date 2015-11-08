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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Parsing {
    sealed class Tokenizer {
        PythonLanguageVersion _version;
        int _lineNumber;
        int _lineStart;
        Stack<TokenKind> _nesting;

        private const string HexDigits = "0123456789ABCDEFabcdef";
        private const string DecimalDigits = "0123456789";
        private const string OctalDigits = "01234567";
        private const string BinaryDigits = "01";

        public Tokenizer(PythonLanguageVersion version) {
            _version = version;
            _lineNumber = 0;
            _lineStart = 0;
            _nesting = new Stack<TokenKind>();
        }

        public string SerializeState() {
            return string.Format(
                "v={0};ln={1};ls={2};nest={3}",
                (int)_version,
                _lineNumber,
                _lineStart,
                Serialize(_nesting)
            );
        }

        private static string Serialize(SourceLocation loc) {
            return string.Format("{0}+{1}+{2}", loc.Index, loc.Line, loc.Column);
        }

        private static string Serialize(Stack<TokenKind> stack) {
            if (stack.Count == 0) {
                return "";
            }

            var sb = new StringBuilder();
            foreach (var k in stack.Reverse()) {
                sb.AppendFormat("{0}+", (uint)k);
            }
            sb.Length -= 1;
            return sb.ToString();
        }

        public void RestoreState(string state) {
            foreach (var line in state.Split(';')) {
                var key = line.Substring(0, line.IndexOf('='));
                var value = line.Substring(line.IndexOf('=') + 1);
                switch (key) {
                    case "v":
                        _version = (PythonLanguageVersion)int.Parse(value);
                        break;
                    case "ln":
                        _lineNumber = int.Parse(value);
                        break;
                    case "ls":
                        _lineStart = int.Parse(value);
                        break;
                    case "nest":
                        _nesting = RestoreTokenKindStack(value);
                        break;
                    default:
                        throw new FormatException("Unrecognized key " + key);
                }
            }
        }

        private static SourceLocation RestoreSourceLocation(string state) {
            var bits = state.Split('+');
            if (bits.Length != 3) {
                throw new FormatException("Cannot read SourceLocation from " + state);
            }
            return new SourceLocation(int.Parse(bits[0]), int.Parse(bits[1]), int.Parse(bits[2]));
        }

        private static Stack<TokenKind> RestoreTokenKindStack(string state) {
            var stack = new Stack<TokenKind>();
            foreach (var bit in state.Split('+')) {
                stack.Push((TokenKind)uint.Parse(bit));
            }
            return stack;
        }

        public IEnumerable<Token> GetTokens(string line) {
            _lineNumber += 1;
            if (string.IsNullOrEmpty(line)) {
                return Enumerable.Empty<Token>();
            }

            var result = GetTokensWorker(line, _lineStart, _lineNumber);
            _lineStart += line?.Length ?? 0;
            return result;
        }

        private IEnumerable<Token> GetTokensWorker(string line, int lineStart, int lineNumber) {
            int c = 0;
            while (c < line.Length) {
                var start = new SourceLocation(lineStart + c, lineNumber, c + 1);
                int len = 0;
                var inGroup = TokenKind.Unknown;

                if (_nesting.Count > 0) {
                    inGroup = _nesting.Peek();
                }

                TokenKind kind = TokenKind.Unknown;

                switch (inGroup) {
                    case TokenKind.RightSingleQuote:
                    case TokenKind.RightDoubleQuote:
                    case TokenKind.RightSingleTripleQuote:
                    case TokenKind.RightDoubleTripleQuote:
                        kind = GetStringLiteralToken(line, c, inGroup, out len);
                        break;
                }

                if (kind == TokenKind.Unknown) {
                    kind = GetNextToken(line, c, out len);
                }

                var token = new Token(kind, start, len);
                yield return token;
                c += len;

                if (inGroup == kind) {
                    _nesting.Pop();
                }
                var endGroup = kind.GetGroupEnding();
                if (endGroup != TokenKind.Unknown) {
                    _nesting.Push(endGroup);
                }
            }
        }

        private TokenKind GetStringLiteralToken(string line, int start, TokenKind inGroup, out int length) {
            length = 0;

            string quote;
            switch (inGroup) {
                case TokenKind.RightSingleQuote:
                case TokenKind.RightDoubleQuote:
                    if (start == 0) {
                        // Unterminated literal, so use regular processing
                        return inGroup;
                    }
                    quote = inGroup == TokenKind.RightSingleQuote ? "'" : "\"";
                    break;
                case TokenKind.RightSingleTripleQuote:
                    quote = "'''";
                    break;
                case TokenKind.RightDoubleTripleQuote:
                    quote = "\"\"\"";
                    break;
                default:
                    return TokenKind.Unknown;
            }

            int end = line.IndexOf(quote, start);
            while (end > 0 && line[end - 1] == '\\') {
                end = line.IndexOf(quote, end + 1);
            }
            if (end == start) {
                length = quote.Length;
                return inGroup;
            } else if (end < 0) {
                length = line.Length - start;
            } else {
                length = end - start;
            }
            return TokenKind.LiteralString;
        }

        private TokenKind GetNextToken(string line, int start, out int length) {
            length = 1;

            char c = line[start];
            switch (c) {
                case ':':
                    return TokenKind.Colon;
                case ';':
                    return TokenKind.SemiColon;
                case ',':
                    return TokenKind.Comma;
                case '.':
                    // Handled below in case it begins a floating-point literal
                    break;
                case '(':
                    return TokenKind.LeftParenthesis;
                case '[':
                    return TokenKind.LeftBracket;
                case '{':
                    return TokenKind.LeftBrace;
                case ')':
                    return TokenKind.RightParenthesis;
                case ']':
                    return TokenKind.RightBracket;
                case '}':
                    return TokenKind.RightBrace;
                case '\'':
                case '"':
                    if (IsNextChar(line, start, c) && IsNextChar(line, start, c, 2)) {
                        length = 3;
                        return c == '\'' ? TokenKind.LeftSingleTripleQuote : TokenKind.LeftDoubleTripleQuote;
                    }
                    return c == '\'' ? TokenKind.LeftSingleQuote : TokenKind.LeftDoubleQuote;
                case '\r':
                case '\n':
                    length = line.Length - start;
                    return TokenKind.NewLine;
                default:
                    break;
            }

            int end = start + 1;
            TokenKind kind = TokenKind.Error;

            if (c == '.') {
                if (end >= line.Length) {
                    return TokenKind.Dot;
                } else if (DecimalDigits.Contains(line[end])) {
                    kind = TokenKind.LiteralDecimal;
                    ReadDecimals(line, ref end);
                    kind = MaybeReadExponent(line, kind, ref end);
                    if (kind != TokenKind.Error) {
                        kind = MaybeReadImaginary(line, kind, ref end);
                    }
                    length = end - start;
                    return kind;
                } else if (end + 2 < line.Length && line[end] == '.' && line[end + 1] == '.') {
                    length = 3;
                    return TokenKind.Ellipsis;
                } else {
                    return TokenKind.Dot;
                }
            }

            if (char.IsNumber(c)) {
                if (c == '0') {
                    if (end + 1 >= line.Length) {
                        length = end - start;
                        return TokenKind.LiteralDecimal;
                    }

                    if (IsNextChar(line, end, 'x') || IsNextChar(line, end, 'X')) {
                        end += 2;
                        ReadWhile(line, ref end, HexDigits);
                        kind = TokenKind.LiteralHex;
                    } else if (IsNextChar(line, end, 'o') || IsNextChar(line, end, 'O')) {
                        end += 2;
                        ReadWhile(line, ref end, OctalDigits);
                        kind = TokenKind.LiteralOctal;
                    } else if (IsNextChar(line, end, 'b') || IsNextChar(line, end, 'B')) {
                        end += 2;
                        ReadWhile(line, ref end, BinaryDigits);
                        kind = TokenKind.LiteralBinary;
                    } else if (_version.Is2x()) {
                        // Numbers starting with '0' in Python 2.x are octal
                        end += 1;
                        ReadWhile(line, ref end, OctalDigits);
                        MaybeReadLongSuffix(line, ref end);
                        length = end - start;
                        return TokenKind.LiteralOctal;
                    } else {
                        // Numbers starting with '0' in Python 3.x are zero
                        end += 1;
                        ReadWhile(line, ref end, '0');
                        length = end - start;
                        return TokenKind.LiteralDecimal;
                    }

                    if (_version.Is2x()) {
                        MaybeReadLongSuffix(line, ref end);
                    }
                    length = end - start;
                    if (length <= 2) {
                        // Expect at least "0[xob]."
                        return TokenKind.Error;
                    }
                    return kind;
                }

                kind = TokenKind.LiteralDecimal;
                ReadDecimals(line, ref end);
                // Will change kind if necessary
                kind = MaybeReadFloatingPoint(line, kind, ref end);
                if (kind == TokenKind.LiteralDecimal && _version.Is2x()) {
                    MaybeReadLongSuffix(line, ref end);
                }
                length = end - start;
                return kind;
            }

            if (char.IsLetter(c) || c == '_') {
                kind = ReadIdentifier(line, ref end);
                length = end - start;
                return kind;
            }

            if (c == '#') {
                while (end < line.Length && line[end] != '\r' && line[end] != '\n') {
                    end += 1;
                }
                length = end - start;
                return TokenKind.Comment;
            }

            if (c == '\\') {
                length = 1;
                return TokenKind.ExplicitLineJoin;
            }

            kind = ReadOperator(line, c, ref end);
            if (kind != TokenKind.Error) {
                length = end - start;
                return kind;
            }

            if (char.IsWhiteSpace(c)) {
                while (end < line.Length && char.IsWhiteSpace(line, end)) {
                    if (line[end] == '\r' || line[end] == '\n') {
                        break;
                    }
                    end += 1;
                }
                length = end - start;
                if (start == 0 && _nesting.Count == 0) {
                    return TokenKind.SignificantWhitespace;
                }
                return TokenKind.Whitespace;
            }

            Debug.Assert(kind == TokenKind.Error, "Unexpected " + kind.ToString());
            return kind;
        }

        private static TokenKind ReadIdentifier(string line, ref int end) {
            int start = end - 1;
            int len = line.Length;
            while (end < len && (char.IsLetterOrDigit(line, end) || line[end] == '_')) {
                end += 1;
            }
            TokenKind kind;
            if (Keywords.TryGetValue(line.Substring(start, end - start), out kind)) {
                return kind;
            }
            return TokenKind.Name;
        }

        private static void ReadDecimals(string line, ref int end) {
            int len = line.Length;
            while (end < len) {
                switch (line[end]) {
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                        end += 1;
                        break;
                    default:
                        return;
                }
            }
        }

        private static TokenKind MaybeReadFloatingPoint(string line, TokenKind kind, ref int end) {
            if (end >= line.Length || kind != TokenKind.LiteralDecimal) {
                return kind;
            }

            char c = line[end];
            if (c == '.') {
                kind = TokenKind.LiteralFloat;
                end += 1;
            }
            ReadDecimals(line, ref end);
            kind = MaybeReadExponent(line, kind, ref end);
            if (kind != TokenKind.Error) {
                kind = MaybeReadImaginary(line, kind, ref end);
            }
            return kind;
        }

        private static void MaybeReadLongSuffix(string line, ref int end) {
            if (end >= line.Length) {
                return;
            }

            if (line[end] == 'l' || line[end] == 'L') {
                end += 1;
            }
        }

        private static TokenKind MaybeReadExponent(string line, TokenKind kind, ref int end) {
            if (end >= line.Length) {
                return kind;
            }

            char c = line[end];
            if (c == 'e' || c == 'E') {
                if (end + 1 >= line.Length) {
                    end = line.Length;
                    return TokenKind.Error;
                }

                char c2 = line[end + 1];
                if (c2 == '+' || c2 == '-') {
                    end += 2;
                } else if (DecimalDigits.Contains(c2)) {
                    end += 1;
                } else {
                    // 'e' belongs to following token
                    return kind;
                }

                if (end >= line.Length) {
                    end = line.Length;
                    return TokenKind.Error;
                }

                kind = TokenKind.LiteralFloat;
                ReadDecimals(line, ref end);
            }
            return kind;
        }

        private static TokenKind MaybeReadImaginary(string line, TokenKind kind, ref int end) {
            if (end >= line.Length ||
                kind != TokenKind.LiteralDecimal && kind != TokenKind.LiteralFloat
            ) {
                return kind;
            }

            char c = line[end];
            if (c == 'j' || c == 'J') {
                end += 1;
                return TokenKind.LiteralImaginary;
            }
            return kind;
        }

        private static TokenKind ReadOperator(string line, char c, ref int end) {
            if (end >= line.Length) {
                return TokenKind.Error;
            }

            char c2 = (end < line.Length) ? line[end] : '\0';
            char c3 = (end + 1 < line.Length) ? line[end + 1] : '\0';
            int len;
            var kind = GetOperatorKind(c, c2, c3, out len, TokenKind.Error);
            if (kind != TokenKind.Error) {
                end += len - 1;
            }
            return kind;
        }


        private static bool IsNextChar(string line, int index, char c, int offset = 1) {
            int i = index + offset;
            return (i < line.Length) && (line[i] == c);
        }

        private static void ReadWhile(string line, ref int end, string allowed) {
            int len = line.Length;
            while (end < len && allowed.Contains(line[end])) {
                end += 1;
            }
        }

        private static void ReadWhile(string line, ref int end, char allowed) {
            int len = line.Length;
            while (end < len && allowed == line[end]) {
                end += 1;
            }
        }

        public IEnumerable<Token> GetRemainingTokens() {
            var eof = new SourceLocation(_lineStart, _lineNumber, 1);

            while (_nesting.Any()) {
                var close = _nesting.Pop();
                switch (close) {
                    case TokenKind.RightSingleQuote:
                    case TokenKind.RightDoubleQuote:
                    case TokenKind.RightSingleTripleQuote:
                    case TokenKind.RightDoubleTripleQuote:
                        break;
                    case TokenKind.RightParenthesis:
                        break;
                    case TokenKind.RightBracket:
                        break;
                    case TokenKind.RightBrace:
                        break;
                    case TokenKind.RightBackQuote:
                        break;
                }
            }

            yield return new Token(TokenKind.EndOfFile, eof, 0);
        }

        #region Token Kind Resolution

        private static readonly IReadOnlyList<Dictionary<char, TokenKind>> OperatorMap = CreateOperatorMap();

        private static Dictionary<char, TokenKind>[] CreateOperatorMap() {
            var topLevelMap = new Dictionary<char, TokenKind>[4];

            // c2 == '\0'
            topLevelMap[0] = new Dictionary<char, TokenKind> {
                { '+', TokenKind.Add },
                { '-', TokenKind.Subtract },
                { '*', TokenKind.Multiply },
                { '@', TokenKind.MatMultiply },
                { '/', TokenKind.Divide },
                { '%', TokenKind.Mod },
                { '&', TokenKind.BitwiseAnd },
                { '|', TokenKind.BitwiseOr },
                { '^', TokenKind.ExclusiveOr },
                { '~', TokenKind.Twiddle },
                { '<', TokenKind.LessThan },
                { '>', TokenKind.GreaterThan },
                { '=', TokenKind.Assign },
            };

            // c2 == '='
            topLevelMap[1] =  new Dictionary<char, TokenKind> {
                { '+', TokenKind.AddEqual },
                { '-', TokenKind.SubtractEqual },
                { '*', TokenKind.MultiplyEqual },
                { '@', TokenKind.MatMultiplyEqual },
                { '/', TokenKind.DivideEqual },
                { '%', TokenKind.ModEqual },
                { '&', TokenKind.BitwiseAndEqual },
                { '|', TokenKind.BitwiseOrEqual },
                { '^', TokenKind.ExclusiveOrEqual },
                { '=', TokenKind.Equals },
                { '!', TokenKind.NotEquals },
                { '<', TokenKind.LessThanOrEqual },
                { '>', TokenKind.GreaterThanOrEqual },
            };

            // c2 == c1
            topLevelMap[2] = new Dictionary<char, TokenKind> {
                { '*', TokenKind.Power },
                { '/', TokenKind.FloorDivide },
                { '<', TokenKind.LeftShift },
                { '>', TokenKind.RightShift },
            };

            // c2 == c1 && c3 == '='
            topLevelMap[3] = new Dictionary<char, TokenKind> {
                { '*', TokenKind.PowerEqual },
                { '/', TokenKind.FloorDivideEqual },
                { '<', TokenKind.LeftShiftEqual },
                { '>', TokenKind.RightShiftEqual },
            };

            return topLevelMap;
        }

        private static TokenKind GetOperatorKind(
            char c1,
            char c2,
            char c3,
            out int operatorLength,
            TokenKind defaultKind = TokenKind.Error
        ) {
            TokenKind result;
            var map = OperatorMap[0];
            operatorLength = 1;

            if (c1 == '<' && c2 == '>') {
                operatorLength = 2;
                return TokenKind.LessThanGreaterThan;
            }

            if (c2 == '=') {
                map = OperatorMap[1];
                operatorLength = 2;
            } else if (c2 == c1) {
                if (c3 == '=') {
                    map = OperatorMap[3];
                    operatorLength = 3;
                } else {
                    map = OperatorMap[2];
                    operatorLength = 2;
                }
            }

            return map.TryGetValue(c1, out result) ? result : defaultKind;
        }

        private static readonly Dictionary<string, TokenKind> Keywords = new Dictionary<string, TokenKind> {
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

        #endregion
    }
}
