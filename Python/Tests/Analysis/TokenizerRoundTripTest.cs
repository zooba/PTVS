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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.PythonTools.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestUtilities;

namespace AnalysisTests {
    /// <summary>
    /// Test cases to verify that the tokenizer successfully preserves all information for round tripping source code.
    /// </summary>
    [TestClass]
    public class TokenizerRoundTripTest {
        // TODO: Add an explicit test for grouping characters and white space, e.g.:
        // (a, b, [whitespace]
        //  [more whitespace]   c, d)
        //
        [TestMethod, Priority(0)]
        public void SimpleTest() {
            foreach (var optionSet in new[] { TokenizerOptions.Verbatim }) {
                foreach (var version in PythonPaths.Versions) {
                    Console.WriteLine("Testing version {0} {1} w/ Option Set {2}", version.Version, version.LibPath, optionSet);
                    int ran = 0, succeeded = 0;
                    string[] files;
                    try {
                        files = Directory.GetFiles(version.LibPath);
                    } catch (DirectoryNotFoundException) {
                        continue;
                    }

                    foreach (var file in files) {
                        try {
                            if (file.EndsWith(".py")) {
                                ran++;
                                TestOneFile(file, version.Version, optionSet);
                                succeeded++;
                            }
                        } catch (Exception e) {
                            Console.WriteLine(e);
                            Console.WriteLine("Failed: {0}", file);
                        }
                    }

                    Assert.AreEqual(ran, succeeded);
                }
            }
        }

        struct ExpectedToken {
            public readonly TokenKind Kind;
            public readonly IndexSpan Span;
            public readonly string Image;

            public ExpectedToken(TokenKind kind, IndexSpan span, string image) {
                Kind = kind;
                Span = span;
                Image = image;
            }
        }

        [TestMethod, Priority(0)]
        public void TrailingBackSlash() {
            var tokens = TestOneString(
                PythonLanguageVersion.V27, 
                TokenizerOptions.Verbatim,
                "fob\r\n\\"
            );
            AssertEqualTokens(
                tokens, 
                new[] { 
                    new ExpectedToken(TokenKind.Name, new IndexSpan(0, 3), "fob"), 
                    new ExpectedToken(TokenKind.NewLine, new IndexSpan(3, 2), "\r\n"), 
                    new ExpectedToken(TokenKind.EndOfFile, new IndexSpan(5, 1), "\\"),
                }
            );

            tokens = TestOneString(
                PythonLanguageVersion.V27,
                TokenizerOptions.Verbatim,
                "fob\r\n\\b"
            );
            AssertEqualTokens(
                tokens,
                new[] { 
                    new ExpectedToken(TokenKind.Name, new IndexSpan(0, 3), "fob"), 
                    new ExpectedToken(TokenKind.NewLine, new IndexSpan(3, 2), "\r\n"), 
                    new ExpectedToken(TokenKind.Error, new IndexSpan(5, 1), "\\"), 
                    new ExpectedToken(TokenKind.Name, new IndexSpan(6, 1), "b"),
                    new ExpectedToken(TokenKind.EndOfFile, new IndexSpan(7, 0), "")
                }
            );
        }

        private static void AssertEqualTokens(List<TokenWithSpan> tokens, ExpectedToken[] expectedTokens) {
            try {
                Assert.AreEqual(expectedTokens.Length, tokens.Count);
                for (int i = 0; i < tokens.Count; i++) {
                    Assert.AreEqual(expectedTokens[i].Kind, tokens[i].Token.Kind);
                    Assert.AreEqual(expectedTokens[i].Span, tokens[i].Span);
                    Assert.AreEqual(expectedTokens[i].Image, tokens[i].Token.VerbatimImage);
                }
            } finally {
                foreach (var token in tokens) {
                    var sb = new StringBuilder("new ExpectedToken(TokenKind.");
                    sb.Append(token.Token.Kind);
                    sb.Append(", new IndexSpan");
                    sb.AppendFormat("({0}, {1})", token.Span.Start, token.Span.Length);
                    sb.Append(", \"");
                    sb.AppendRepr(token.Token.VerbatimImage);
                    sb.Append("\"), ");
                    Trace.TraceInformation(sb.ToString());
                }
            }
        }

        [TestMethod, Priority(0)]
        public void BinaryTest() {
            var filename = Path.Combine(System.Environment.GetFolderPath(Environment.SpecialFolder.System), "kernel32.dll");
            TestOneFile(filename, PythonLanguageVersion.V27, TokenizerOptions.Verbatim);
        }

        [TestMethod, Priority(0)]
        public void TestErrors() {
            TestOneString(PythonLanguageVersion.V27, TokenizerOptions.Verbatim, "http://xkcd.com/353/\")");
            TestOneString(PythonLanguageVersion.V27, TokenizerOptions.Verbatim, "lambda, U+039B");
        }

        private static void TestOneFile(string filename, PythonLanguageVersion version, TokenizerOptions optionSet) {
            using (var file = new FileStream(filename, FileMode.Open, FileAccess.Read))
            using (var reader = PythonEncoding.GetStreamReaderWithEncoding(file, version, ErrorSink.Null)) {
                TestOneString(version, optionSet, reader.ReadToEnd());
            }
        }

        private static List<TokenWithSpan> TestOneString(PythonLanguageVersion version, TokenizerOptions optionSet, string originalText) {
            StringBuilder output = new StringBuilder();

            var tokenizer = new Tokenizer(version, ErrorSink.Null, options: optionSet, indentationInconsistency: Severity.Ignore);
            tokenizer.Initialize(new StringReader(originalText));
            Token token;
            int prevOffset = 0;

            List<TokenWithSpan> tokens = new List<TokenWithSpan>();
            while ((token = tokenizer.GetNextToken()) != null) {
                tokens.Add(new TokenWithSpan(token, tokenizer.TokenSpan, tokenizer.PreceedingWhiteSpace));

                output.Append(tokenizer.PreceedingWhiteSpace);
                output.Append(token.VerbatimImage);

                const int contextSize = 50;
                for (int i = prevOffset; i < originalText.Length && i < output.Length; i++) {
                    if (originalText[i] != output[i]) {
                        // output some context
                        StringBuilder x = new StringBuilder();
                        StringBuilder y = new StringBuilder();
                        StringBuilder z = new StringBuilder();
                        for (int j = Math.Max(0, i - contextSize); j < Math.Min(Math.Min(originalText.Length, output.Length), i + contextSize); j++) {
                            x.AppendRepr(originalText[j]);
                            y.AppendRepr(output[j]);
                            if (j == i) {
                                z.Append("^");
                            } else {
                                z.Append(" ");
                            }
                        }

                        Trace.TraceInformation("Mismatch context at {0}:", i);
                        Trace.TraceInformation("Original: {0}", x.ToString());
                        Trace.TraceInformation("New     : {0}", y.ToString());
                        Trace.TraceInformation("Differs : {0}", z.ToString());
                        Trace.TraceInformation("Token   : {0}", token);

                        char cx = StringBuilderExtensions.FixChar(originalText[i]);
                        char cy = StringBuilderExtensions.FixChar(output[i]);
                        Assert.AreEqual(originalText[i], output[i], String.Format("Characters differ at {0}, got {1}, expected {2}", i, cy, cx));
                    }
                }

                prevOffset = output.Length;

                if (token.Kind == TokenKind.EndOfFile) {
                    break;
                }
            }
            output.Append(tokenizer.PreceedingWhiteSpace);

            Assert.AreEqual(originalText.Length, output.Length);
            return tokens;
        }
    }

    static class StringBuilderExtensions {
        private static readonly string From = "\t\r\n\f\0";
        private static readonly char[] FromChars = From.ToCharArray();
        private static readonly string To = "\x2192\x00B6\x2193\x00AC\x00B7";

        public static char FixChar(char ch) {
            int i = From.IndexOf(ch);
            return i < 0 ? ch : To[i];
        }

        public static void AppendRepr(this StringBuilder self, char ch) {
            int i = From.IndexOf(ch);
            self.Append(i < 0 ? ch : To[i]);
        }

        public static void AppendRepr(this StringBuilder self, string str) {
            for (int start = 0; start < str.Length; ) {
                int next = str.IndexOfAny(From.ToCharArray(), start);
                if (next < 0) {
                    self.Append(str.Substring(start));
                    return;
                }

                if (next > start) {
                    self.Append(str.Substring(start, next - start));
                }

                int i = From.IndexOf(str[next]);
                self.Append(i < 0 ? str[next] : To[i]);
                start = next + 1;
            }
        }
    }
}
