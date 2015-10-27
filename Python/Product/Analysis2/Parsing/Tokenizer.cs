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
        string _multilineStringQuotes;
        SourceLocation _multilineStringStart;

        private const string HexDigits = "0123456789ABCDEFabcdef";
        private const string DecimalDigits = "0123456789";
        private const string OctalDigits = "01234567";
        private const string BinaryDigits = "01";

        public Tokenizer(PythonLanguageVersion version) {
            _version = version;
            _lineNumber = 0;
            _lineStart = 0;
        }

        public string SerializeState() {
            return string.Format(
                "v={0};ln={1};ls={2};mlsq={3};mlss={4}",
                (int)_version,
                _lineNumber,
                _lineStart,
                _multilineStringQuotes ?? "",
                Serialize(_multilineStringStart)
            );
        }

        private static string Serialize(SourceLocation loc) {
            return string.Format("{0}+{1}+{2}", loc.Index, loc.Line, loc.Column);
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
                    case "mlsq":
                        _multilineStringQuotes = value;
                        break;
                    case "mlss":
                        _multilineStringStart = RestoreSourceLocation(value);
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
                // Handle a multiline string
                if (!string.IsNullOrEmpty(_multilineStringQuotes)) {
                    int i = line.IndexOf(_multilineStringQuotes, c);
                    while (i > 0 && line[i - 1] == '\\') {
                        i = line.IndexOf(_multilineStringQuotes, i + 1);
                    }
                    if (i < 0) {
                        // not at the end of the string yet
                        yield break;
                    }
                    var stringEnd = new SourceLocation(lineStart + i, lineNumber, i + 1);
                    yield return new Token(TokenCategory.StringLiteral, _multilineStringStart, stringEnd);
                    yield return new Token(TokenCategory.CloseQuote, stringEnd, _multilineStringQuotes.Length);

                    c = i + _multilineStringQuotes.Length;
                    _multilineStringQuotes = null;
                    continue;
                }

                int len;
                TokenCategory category;
                GetNextToken(line, c, out len, out category);
                var token = new Token(category, new SourceLocation(lineStart, lineNumber, c + 1), len);
                yield return token;
                c += len;

                if (category == TokenCategory.OpenQuote) {
                    if (len >= 3) {
                        _multilineStringStart = token.Span.End;
                        _multilineStringQuotes = line.Substring(c - len, len);
                    } else {
                        char q = line[c];
                        int c2 = line.IndexOf(q, c + 1);
                        while (c2 > 0 && line[c2 - 1] == '\\') {
                            c2 = line.IndexOf(q, c2 + 1);
                        }
                        if (c2 < c) {
                            yield return new Token(TokenCategory.StringLiteral, token.Span.End, line.Length - c);
                            c = line.Length;
                        } else {
                            token = new Token(TokenCategory.StringLiteral, token.Span.End, c2 - c);
                            yield return token;
                            yield return new Token(TokenCategory.CloseQuote, token.Span.End, 1);
                            c = c2 + 1;
                        }
                    }
                }

            }
        }

        private void GetNextToken(string line, int start, out int length, out TokenCategory category) {
            length = 1;
            category = TokenCategory.Error;

            char c = line[start];
            switch (c) {
                case ':':
                    category = TokenCategory.Colon;
                    return;
                case ';':
                    category = TokenCategory.SemiColon;
                    return;
                case ',':
                    category = TokenCategory.Comma;
                    return;
                case '.':
                    // Handled below in case it begins a floating-point literal
                    break;
                case '(':
                case '[':
                case '{':
                    category = TokenCategory.OpenGrouping;
                    return;
                case ')':
                case ']':
                case '}':
                    category = TokenCategory.CloseGrouping;
                    return;
                case '\'':
                case '"':
                    if (IsNextChar(line, start, c) && IsNextChar(line, start, c, 2)) {
                        length = 3;
                    }
                    // TokenCategory.CloseQuote is not generated by this function
                    category = TokenCategory.OpenQuote;
                    return;
                case '\r':
                case '\n':
                    length = line.Length - start;
                    category = TokenCategory.EndOfLine;
                    return;
                default:
                    break;
            }

            int end = start + 1;

            if (c == '.') {
                if (end >= line.Length) {
                    category = TokenCategory.Period;
                    return;
                } else if (DecimalDigits.Contains(line[end])) {
                    ReadDecimals(line, ref end);
                    MaybeReadExponent(line, ref end, ref category);
                    if (category != TokenCategory.Error) {
                        MaybeReadImaginary(line, ref end, ref category);
                    }
                } else if (end + 2 < line.Length && line[end] == '.' && line[end + 1] == '.') {
                    end += 2;
                    category = TokenCategory.Identifier;
                    return;
                } else {
                    category = TokenCategory.Period;
                    return;
                }
            }

            if (char.IsNumber(c)) {
                if (c == '0') {
                    if (end + 1 >= line.Length) {
                        length = end - start;
                        category = TokenCategory.DecimalIntegerLiteral;
                        return;
                    }

                    if (IsNextChar(line, end, 'x') || IsNextChar(line, end, 'X')) {
                        end += 2;
                        ReadWhile(line, ref end, HexDigits);
                        category = TokenCategory.HexadecimalIntegerLiteral;
                    } else if (IsNextChar(line, end, 'o') || IsNextChar(line, end, 'O')) {
                        end += 2;
                        ReadWhile(line, ref end, OctalDigits);
                        category = TokenCategory.OctalIntegerLiteral;
                    } else if (IsNextChar(line, end, 'b') || IsNextChar(line, end, 'B')) {
                        end += 2;
                        ReadWhile(line, ref end, BinaryDigits);
                        category = TokenCategory.BinaryIntegerLiteral;
                    } else if (_version.Is2x()) {
                        // Numbers starting with '0' in Python 2.x are octal
                        end += 1;
                        ReadWhile(line, ref end, OctalDigits);
                        category = TokenCategory.OctalIntegerLiteral;
                        MaybeReadLongSuffix(line, ref end);
                        length = end - start;
                        return;
                    } else {
                        // Numbers starting with '0' in Python 3.x are zero
                        end += 1;
                        ReadWhile(line, ref end, '0');
                        length = end - start;
                        category = TokenCategory.DecimalIntegerLiteral;
                        return;
                    }

                    if (_version.Is2x()) {
                        MaybeReadLongSuffix(line, ref end);
                    }
                    length = end - start;
                    if (length <= 2) {
                        // Expect at least "0[xob]."
                        category = TokenCategory.Error;
                    }
                    return;
                }

                category = TokenCategory.DecimalIntegerLiteral;
                ReadDecimals(line, ref end);
                // Will change category if necessary
                MaybeReadFloatingPoint(line, ref end, ref category);
                if (category == TokenCategory.DecimalIntegerLiteral && _version.Is2x()) {
                    MaybeReadLongSuffix(line, ref end);
                }
                length = end - start;
                return;
            }

            if (char.IsLetter(c) || c == '_') {
                ReadIdentifier(line, ref end);
                length = end - start;
                category = TokenCategory.Identifier;
                return;
            }

            if (c == '#') {
                while (end < line.Length && line[end] != '\r' && line[end] != '\n') {
                    end += 1;
                }
                length = end - start;
                category = TokenCategory.Comment;
                return;
            }

            MaybeReadOperator(line, c, ref end, ref category);
            if (category != TokenCategory.Error) {
                length = end - start;
                return;
            }

            if (c == '\\') {
                length = 1;
                category = TokenCategory.IgnoreEndOfLine;
                return;
            }

            if (char.IsWhiteSpace(c)) {
                while (end < line.Length && line[end] != '\r' && line[end] != '\n' && char.IsWhiteSpace(line, end)) {
                    end += 1;
                }
                length = end - start;
                category = TokenCategory.WhiteSpace;
                return;
            }

            Debug.Assert(category == TokenCategory.Error, "Unexpected " + category.ToString());
        }

        private static void ReadIdentifier(string line, ref int end) {
            int len = line.Length;
            while (end < len && (char.IsLetterOrDigit(line, end) || line[end] == '_')) {
                end += 1;
            }
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

        private static void MaybeReadFloatingPoint(string line, ref int end, ref TokenCategory category) {
            if (end >= line.Length) {
                return;
            }

            char c = line[end];
            if (c != '.') {
                return;
            }

            category = TokenCategory.FloatingPointLiteral;
            end += 1;
            ReadDecimals(line, ref end);
            MaybeReadExponent(line, ref end, ref category);
            if (category != TokenCategory.Error) {
                MaybeReadImaginary(line, ref end, ref category);
            }
        }

        private static void MaybeReadLongSuffix(string line, ref int end) {
            if (end >= line.Length) {
                return;
            }

            if (line[end] == 'l' || line[end] == 'L') {
                end += 1;
            }
        }

        private static void MaybeReadExponent(string line, ref int end, ref TokenCategory category) {
            if (end >= line.Length) {
                return;
            }

            char c = line[end];
            if (c == 'e' || c == 'E') {
                if (end + 1 >= line.Length) {
                    category = TokenCategory.Error;
                    end = line.Length;
                    return;
                }

                char c2 = line[end + 1];
                if (c2 == '+' || c2 == '-') {
                    end += 2;
                } else if (DecimalDigits.Contains(c2)) {
                    end += 1;
                } else {
                    // 'e' belongs to following token
                    return;
                }

                if (end >= line.Length) {
                    category = TokenCategory.Error;
                    end = line.Length;
                    return;
                }
                ReadDecimals(line, ref end);
            }
        }

        private static void MaybeReadImaginary(string line, ref int end, ref TokenCategory category) {
            if (end >= line.Length) {
                return;
            }

            char c = line[end];
            if (c == 'j' || c == 'J') {
                category = TokenCategory.ImaginaryLiteral;
                end += 1;
            }
        }

        private static void MaybeReadOperator(string line, char c, ref int end, ref TokenCategory category) {
            if (end >= line.Length) {
                return;
            }

            char c2 = (end < line.Length) ? line[end] : '\0';
            char c3 = (end + 1 < line.Length) ? line[end + 1] : '\0';
            switch (c) {
                // X= operators
                case '!':
                    if (c2 == '=') {
                        category = TokenCategory.Operator;
                        end += 1;
                        return;
                    }
                    break;

                // X operators
                case '~':
                    category = TokenCategory.Operator;
                    return;

                // X or X= operators
                case '+':
                case '=':
                case '&':
                case '%':
                case '@':
                case '^':
                case '|':
                    category = TokenCategory.Operator;
                    if (c2 == '=') {
                        end += 1;
                    }
                    return;

                // X or XX or X= or XX= operators
                case '*':
                case '/':
                case '<':
                case '>':
                    category = TokenCategory.Operator;
                    if (c2 == '=') {
                        end += 1;
                    } else if (c2 == c) {
                        if (c3 == '=') {
                            end += 2;
                        } else {
                            end += 1;
                        }
                    }
                    return;

                // X or X= or X> operators
                case '-':
                    category = TokenCategory.Operator;
                    if (c2 == '=' || c2 == '>') {
                        end += 1;
                    }
                    return;
            }

            end -= 1;
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

            if (!string.IsNullOrEmpty(_multilineStringQuotes)) {
                yield return new Token(TokenCategory.StringLiteral, _multilineStringStart, eof);
            }

            yield return Token.EOF;
        }
    }
}
