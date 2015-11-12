﻿// Python Tools for Visual Studio
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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;

namespace Microsoft.PythonTools.Analysis.Parsing {
    public sealed class Tokenization {
        // Not RO as it may be cleared to reduce memory usage
        private Token[][] _tokens;

        private readonly string[] _lines;
        private readonly int[] _lineStarts;
        private readonly PythonLanguageVersion _languageVersion;
        private readonly Encoding _encoding;

        public static async Task<Tokenization> TokenizeAsync(
            ISourceDocument document,
            PythonLanguageVersion languageVersion
        ) {
            using (var stream = await document.ReadAsync()) {
                var reader = new PythonSourceStreamReader(stream, false);
                return await TokenizeAsync(reader, languageVersion, reader.Encoding);
            }
        }

        private static async Task<Tokenization> TokenizeAsync(
            TextReader reader,
            PythonLanguageVersion languageVersion,
            Encoding encoding
        ) {
            var tokenizer = new Tokenizer(languageVersion);

            var lines = new List<string>();
            var tokens = new List<Token[]>();
            var lineStarts = new List<int>() { 0 };
            string line;
            while ((line = await reader.ReadLineAsync()) != null) {
                lines.Add(line);
                var tok = tokenizer.GetTokens(line).ToArray();
                tokens.Add(tok);
                Debug.Assert(tok.Length > 0);
                if (tok.Last().Is(TokenKind.NewLine)) {
                    lineStarts.Add(tok.Last().Span.End.Index);
                }
            }
            if (tokens.Count == 0) {
                lines.Add(string.Empty);
                tokens.Add(tokenizer.GetRemainingTokens().ToArray());
            } else {
                tokens[tokens.Count - 1] = tokens.Last().Concat(tokenizer.GetRemainingTokens()).ToArray();
            }

            return new Tokenization(
                lines.ToArray(),
                tokens.ToArray(),
                lineStarts.ToArray(),
                languageVersion,
                encoding
            );
        }

        private Tokenization(
            string[] lines,
            Token[][] tokens,
            int[] lineStarts,
            PythonLanguageVersion languageVersion,
            Encoding encoding
        ) {
            _lines = lines;
            _tokens = tokens;
            _lineStarts = lineStarts;
            _languageVersion = languageVersion;
            _encoding = encoding;
        }

        public Encoding Encoding => _encoding;

        public string GetTokenText(SourceSpan span) {
            if (span.End.Index == int.MaxValue) {
                return string.Empty;
            }

            var firstLine = span.Start.Line - 1;
            var lastLine = span.End.Line - 1;
            int start = span.Start.Column - 1;
            int end = span.End.Column - 1;
            string line;

            if (firstLine == lastLine) {
                if (end <= start) {
                    return string.Empty;
                }

                line = _lines[firstLine];
                if (start < 0 || start >= line.Length) {
                    throw new IndexOutOfRangeException();
                }

                int length = end - start;
                if (end > line.Length) {
                    length = line.Length - start;
                }
                return line.Substring(start, length);
            }

            var sb = new StringBuilder();

            for (int lineNo = firstLine; lineNo < lastLine; ++lineNo) {
                line = _lines[lineNo];
                if (start < 0 || start >= line.Length) {
                    throw new IndexOutOfRangeException();
                }

                sb.Append(line.Substring(start));
                start = 0;
            }

            line = _lines[lastLine];
            if (end > line.Length) {
                sb.Append(line);
            } else {
                sb.Append(line.Substring(0, end));
            }

            return sb.ToString();
        }

        public string GetTokenText(Token token) {
            if (token.Is(TokenKind.EndOfFile)) {
                return string.Empty;
            }

            return GetTokenText(token.Span);
        }

        public IEnumerable<Token> AllTokens {
            get {
                if (_tokens == null) {
                    return Enumerable.Empty<Token>();
                }

                return _tokens.SelectMany(Identity);
            }
        }

        private static Token[] Identity(Token[] obj) {
            return obj;
        }

        public IReadOnlyList<Token> GetLine(int lineNo) {
            if (lineNo < 0 || lineNo >= _lines.Length) {
                return null;
            }
            return _tokens[lineNo];
        }

        public int GetLineStartIndex(int lineNo) {
            if (lineNo < 0 || lineNo >= _lines.Length) {
                return -1;
            }
            return _lineStarts[lineNo];
        }

        public int GetLineNumberByIndex(int index) {
            if (index < 0 || _lineStarts.Length == 0) {
                return -1;
            }
            int match = Array.BinarySearch(_lineStarts, index);
            if (match < 0) {
                // If our index = -1, assume we're on the first line.
                if (match == -1) {
                    Debug.Fail("Invalid index");
                    return 0;
                }
                // If we couldn't find an exact match for this line number, get the nearest
                // matching line number less than this one
                match = ~match - 1;
            }

            Debug.Assert(0 <= match && match < _lineStarts.Length);
            return match;
        }

        public IReadOnlyList<Token> GetLineByIndex(int index) {
            return GetLine(GetLineNumberByIndex(index));
        }

        public PythonLanguageVersion LanguageVersion {
            get { return _languageVersion; }
        }
    }
}
