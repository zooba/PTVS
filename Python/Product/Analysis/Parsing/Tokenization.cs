using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;

namespace Microsoft.PythonTools.Parsing {
    public sealed class Tokenization {
        private static readonly TokenInfo[] EmptyTokenInfoArray = new TokenInfo[0];

        private readonly Token[][] _tokens;
        private readonly TokenInfo[][] _lines;
        private readonly int[] _lineStarts;
        private readonly string[][] _precedingWhitespace;
        private readonly ErrorResult[] _errors;
        private readonly PythonLanguageVersion _languageVersion;
        private readonly TokenizerOptions _options;

        public static async Task<Tokenization> TokenizeAsync(
            ISourceDocument document,
            PythonLanguageVersion languageVersion,
            TokenizerOptions options,
            Severity indentationInconsistency
        ) {
            using (var stream = await document.ReadAsync()) {
                var errors = new List<ErrorResult>();
                var reader = PythonEncoding.GetStreamReaderWithEncoding(
                    stream,
                    languageVersion,
                    new CollectingErrorSink(errors)
                );
                return await TokenizeAsync(reader, languageVersion, options, indentationInconsistency, errors);
            }
        }

        private static async Task<Tokenization> TokenizeAsync(
            TextReader reader,
            PythonLanguageVersion languageVersion,
            TokenizerOptions options,
            Severity indentationInconsistency
        ) {
            var errors = new List<ErrorResult>();
            return await TokenizeAsync(reader, languageVersion, options, indentationInconsistency, errors);
        }

        private static async Task<Tokenization> TokenizeAsync(
            TextReader reader,
            PythonLanguageVersion languageVersion,
            TokenizerOptions options,
            Severity indentationInconsistency,
            List<ErrorResult> errors
        ) {
            var tokenizer = new Tokenizer(
                languageVersion,
                new CollectingErrorSink(errors),
                options,
                indentationInconsistency
            );

            tokenizer.Initialize(null, reader, SourceLocation.MinValue);

            List<Token[]> tokens;
            List<int> lineStarts;
            List<TokenInfo[]> tokenInfos;
            List<string[]> precedingWhitespace;
            tokenizer.ReadAllTokens(out tokens, out lineStarts, out tokenInfos, out precedingWhitespace);

            return new Tokenization(
                tokens.ToArray(),
                tokenInfos.ToArray(),
                lineStarts.ToArray(),
                precedingWhitespace.ToArray(),
                errors.ToArray(),
                languageVersion,
                options
            );
        }

        private Tokenization(
            Token[][] tokens,
            TokenInfo[][] lines,
            int[] lineStarts,
            string[][] precedingWhitespace,
            ErrorResult[] errors,
            PythonLanguageVersion languageVersion,
            TokenizerOptions options
        ) {
            if (tokens != null) {
                if (tokens.Length != lines.Length) {
                    throw new ArgumentException("tokens and lines must be the same length");
                }
            }
            _tokens = tokens;
            _lines = lines;
            _lineStarts = lineStarts;
            _precedingWhitespace = precedingWhitespace;
            _errors = errors;
            _languageVersion = languageVersion;
            _options = options;
        }

        internal IEnumerable<TokenWithSpan> RawTokens {
            get {
                if (_tokens == null) {
                    yield break;
                }

                for (int i = 0; i < _tokens.Length; ++i) {
                    var line = _tokens[i];
                    var lineInfo = _lines[i];
                    var whitespace = _precedingWhitespace[i];
                    Debug.Assert(line.Length == lineInfo.Length);
                    Debug.Assert(line.Length == whitespace.Length);
                    for (int j = 0; j < line.Length && j < lineInfo.Length && j < whitespace.Length; ++j) {
                        yield return new TokenWithSpan(line[j], lineInfo[j].Span, whitespace[j]);
                    }
                }
            }
        }

        private static TokenInfo[] Identity(TokenInfo[] obj) {
            return obj;
        } 

        public IEnumerable<TokenInfo> AllTokens {
            get {
                return _lines.SelectMany(Identity);
            }
        }

        public IEnumerable<IReadOnlyList<TokenInfo>> AllLines {
            get {
                return _lines.Select(Identity);
            }
        }

        public IReadOnlyList<TokenInfo> GetLine(int lineNo) {
            if (lineNo < 0 || lineNo >= _lines.Length) {
                return null;
            }
            return _lines[lineNo];
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

        public IReadOnlyList<TokenInfo> GetLineByIndex(int index) {
            return GetLine(GetLineNumberByIndex(index));
        }

        public PythonLanguageVersion LanguageVersion {
            get { return _languageVersion; }
        }

        public bool Verbatim {
            get { return _options.HasFlag(TokenizerOptions.Verbatim); }
        }
    }
}
