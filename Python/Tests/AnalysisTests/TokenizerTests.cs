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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestUtilities;
using TestUtilities.Python;

namespace AnalysisTests {
    [TestClass]
    public class TokenizerTest {
        [ClassInitialize]
        public static void DoDeployment(TestContext context) {
            AssertListener.Initialize();
            // Don't deploy test data because we read directly from the source.
            PythonTestData.Deploy(includeTestData: false);
        }

        private static Tokenization Tokenize(ISourceDocument document, PythonLanguageVersion version) {
            return Tokenization.TokenizeAsync(
                document,
                version,
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }

        private static Tokenization Tokenize(string text, PythonLanguageVersion version) {
            return Tokenization.TokenizeAsync(
                new StringLiteralDocument(text),
                version,
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }

        private static string MakeString(Tokenization tokenization, Token t) {
            string s;
            if (t.Is(TokenKind.SignificantWhitespace)) {
                s = "SWS:" + tokenization.GetTokenText(t).Replace(' ', '.');
            } else if (t.Is(TokenKind.Whitespace)) {
                s = "WS:" + tokenization.GetTokenText(t);
            } else if (t.IsAny(TokenUsage.BeginGroup, TokenUsage.EndGroup) && !t.Is(TokenCategory.StringLiteral)) {
                s = tokenization.GetTokenText(t);
            } else if (t.IsAny(TokenKind.Name, TokenKind.Comment, TokenKind.Colon, TokenKind.SemiColon)) {
                s = tokenization.GetTokenText(t);
            } else {
                s = string.Format("{0}:{1}", t.Kind, tokenization.GetTokenText(t));
                if (s.StartsWith("Keyword")) {
                    s = s.Substring(7);
                }
            }

            s = s.Replace("\\", "\\\\");
            s = s.Replace("\r", "\\r");
            s = s.Replace("\n", "\\n");
            s = s.Replace("\t", "\\t");

            return s;
        }

        private static void AssertTokens(Tokenization tokenization, params string[] expected) {
            var actualList = Environment.NewLine + string.Join(", ", tokenization.AllTokens.Select(t =>
                string.Format("\"{0}\"", MakeString(tokenization, t))
            ));

            using (var e = tokenization.AllTokens.GetEnumerator()) {
                for (int i = 0; i < expected.Length; ++i) {
                    Assert.IsTrue(e.MoveNext(), "Not enough tokens" + actualList);

                    var actual = MakeString(tokenization, e.Current);
                    Assert.AreEqual(expected[i], actual, "Mismatch" + actualList);
                }

                Assert.IsFalse(e.MoveNext(), "Unexpected tokens" + actualList);
            }
        }

        [TestMethod, Priority(0)]
        public void SingleLineTokenization() {
            AssertTokens(
                Tokenize("a =  b+   c ", PythonLanguageVersion.V35),
                "SWS:", "a", "WS: ", "Assign:=", "WS:  ", "b", "Add:+",
                "WS:   ", "c", "WS: ", "EndOfFile:"
            );
        }

        [TestMethod, Priority(0)]
        public void MultiLineTokenization() {
            AssertTokens(
                Tokenize("a=b+c \nd = a + x   \n  ", PythonLanguageVersion.V35),
                "SWS:", "a", "Assign:=", "b", "Add:+", "c", "WS: ",
                "NewLine:\\n",
                "SWS:", "d", "WS: ", "Assign:=", "WS: ", "a",
                "WS: ", "Add:+", "WS: ", "x", "WS:   ", "NewLine:\\n",
                "WS:  ", "EndOfFile:"
            );
        }

        [TestMethod, Priority(0)]
        public void GroupingWhitespace() {
            AssertTokens(
                Tokenize("a={\n    a,\n#comment\n    b\n}", PythonLanguageVersion.V35),
                "SWS:", "a", "Assign:=", "{", "WS:\\n",
                "WS:    ", "a", "Comma:,", "WS:\\n",
                "#comment", "WS:\\n",
                "WS:    ", "b", "WS:\\n",
                "}", "EndOfFile:"
            );
        }

        [TestMethod, Priority(0)]
        public void BackslashInString() {
            AssertTokens(
                Tokenize("'\\\\'", PythonLanguageVersion.V35),
                "SWS:", "LeftSingleQuote:'", "LiteralString:\\\\\\\\", "RightSingleQuote:'", "EndOfFile:"
            );
        }

        [TestMethod, Priority(0)]
        public void EscapedQuotes() {
            AssertTokens(
                Tokenize(@"'\\\\' '\\' '\\""' '""'", PythonLanguageVersion.V35),
                "SWS:",
                "LeftSingleQuote:'", @"LiteralString:\\\\\\\\", "RightSingleQuote:'", "WS: ",
                "LeftSingleQuote:'", @"LiteralString:\\\\", "RightSingleQuote:'", "WS: ",
                "LeftSingleQuote:'", @"LiteralString:\\\\""", "RightSingleQuote:'", "WS: ",
                "LeftSingleQuote:'", @"LiteralString:""", "RightSingleQuote:'",
                "EndOfFile:"
            );
        }

        [TestMethod, Priority(0)]
        public void ExplicitLineJoin() {
            AssertTokens(
                Tokenize("a=b+\\\nc", PythonLanguageVersion.V35),
                "SWS:", "a", "Assign:=", "b", "Add:+", "ExplicitLineJoin:\\\\", "WS:\\n",
                "c", "EndOfFile:"
            );
        }

        [TestMethod, Priority(0)]
        public void AdjacentOperators() {
            AssertTokens(
                Tokenize("a--b++c", PythonLanguageVersion.V35),
                "SWS:", "a", "Subtract:-", "Subtract:-", "b", "Add:+", "Add:+", "c", "EndOfFile:"
            );
        }

        [TestMethod, Priority(0)]
        public void GroupingRecovery() {
            AssertTokens(
                Tokenize("if True:\n    x=[\n    def f(): pass\n", PythonLanguageVersion.V35),
                "SWS:", "If:if", "WS: ", "True:True", ":", "NewLine:\\n",
                "SWS:....", "x", "Assign:=", "[", "WS:\\n",
                "NewLine:", "SWS:....", "Def:def", "WS: ", "f", "(", ")", ":",
                "WS: ", "Pass:pass", "NewLine:\\n", "EndOfFile:"
            );
        }

        [TestMethod, Priority(0)]
        public void CommentTokenization() {
            AssertTokens(
                Tokenize("# a=b+c \n\nd=a   # c\n  #eof", PythonLanguageVersion.V35),
                "# a=b+c ", "NewLine:\\n",
                "NewLine:\\n",
                "SWS:", "d", "Assign:=", "a", "WS:   ", "# c", "NewLine:\\n",
                "WS:  ", "#eof", "EndOfFile:"
            );

            AssertTokens(
                Tokenize(new FileSourceDocument(PythonTestData.GetTestDataSourcePath("Grammar\\Comments.py")), PythonLanguageVersion.V35),
                    "# Above", "NewLine:\\r\\n",
                    "SWS:", "a", "NewLine:\\r\\n",
                    "NewLine:\\r\\n",
                    "SWS:", "a", "WS: ", "# After", "NewLine:\\r\\n",
                    "NewLine:\\r\\n",
                    "SWS:", "Def:def", "WS: ", "f", "(", "WS:\\r\\n",
                    "WS:    ", "a", "Comma:,", "WS: ", "#param", "WS:\\r\\n",
                    ")", ":", "WS: ", "#suite", "NewLine:\\r\\n",
                    "SWS:....", "Pass:pass", "WS: ", "#stmt", "NewLine:\\r\\n",
                    "#func", "NewLine:\\r\\n",
                    "SWS:....", "Pass:pass", "NewLine:\\r\\n",
                    "WS:    ", "#func", "NewLine:\\r\\n",
                    "#notfunc", "NewLine:\\r\\n",
                    "NewLine:\\r\\n",
                    "SWS:", "a", "NewLine:\\r\\n",
                    "# Below", "NewLine:\\r\\n",
                    "NewLine:\\r\\n",
                    "SWS:", "If:if", "WS: ", "True:True", ":", "NewLine:\\r\\n",
                    "WS:    ", "#block", "NewLine:\\r\\n",
                    "SWS:....", "Pass:pass", "NewLine:\\r\\n",
                    "NewLine:\\r\\n",
                    "#eof", "EndOfFile:"
                );
        }
    }
}
