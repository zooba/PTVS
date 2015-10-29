using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
                version
            ).GetAwaiter().GetResult();
        }

        private static Tokenization Tokenize(string text, PythonLanguageVersion version) {
            return Tokenization.TokenizeAsync(
                new StringLiteralDocument(text),
                version
            ).GetAwaiter().GetResult();
        }

        private static string MakeString(Tokenization tokenization, Token t) {
            var s = string.Format("{0}:{1}", t.Kind, tokenization.GetTokenText(t));

            s = s.Replace("\r", "\\r");
            s = s.Replace("\n", "\\n");
            s = s.Replace("\t", "\\t");

            return s;
        }

        private static void AssertTokens(Tokenization tokenization, params string[] expected) {
            var actualList = Environment.NewLine + string.Join(", ", tokenization.AllTokens.Select(t =>
                string.Format("\"{0}\"", MakeString(tokenization, t).Replace("\\", "\\\\"))
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

        [TestMethod]
        public void SingleLineTokenization() {
            AssertTokens(
                Tokenize("a =  b+   c ", PythonLanguageVersion.V35),
                "Identifier:a", "WhiteSpace: ", "Operator:=", "WhiteSpace:  ", "Identifier:b", "Operator:+",
                "WhiteSpace:   ", "Identifier:c", "WhiteSpace: ", "EndOfStream:"
            );
        }

        [TestMethod]
        public void MultiLineTokenization() {
            AssertTokens(
                Tokenize("a=b+c \nd = a + x   \n  ", PythonLanguageVersion.V35),
                "Identifier:a", "Operator:=", "Identifier:b", "Operator:+", "Identifier:c", "WhiteSpace: ",
                "EndOfLine:\\n",
                "Identifier:d", "WhiteSpace: ", "Operator:=", "WhiteSpace: ", "Identifier:a", "WhiteSpace: ",
                "Operator:+", "WhiteSpace: ", "Identifier:x", "WhiteSpace:   ", "EndOfLine:\\n",
                "WhiteSpace:  ", "EndOfStream:"
            );
        }

        [TestMethod]
        public void CommentTokenization() {
            AssertTokens(
                Tokenize("# a=b+c \n\nd=a   # c\n  #eof", PythonLanguageVersion.V35),
                "Comment:# a=b+c ", "EndOfLine:\\n", "EndOfLine:\\n",
                "Identifier:d", "Operator:=", "Identifier:a", "WhiteSpace:   ", "Comment:# c", "EndOfLine:\\n",
                "WhiteSpace:  ", "Comment:#eof", "EndOfStream:"
            );

            AssertTokens(
                Tokenize(new FileSourceDocument(PythonTestData.GetTestDataSourcePath("Grammar\\Comments.py")), PythonLanguageVersion.V35),
                    "Comment:# Above", "EndOfLine:\\r\\n",
                    "Identifier:a", "EndOfLine:\\r\\n",
                    "EndOfLine:\\r\\n",
                    "Identifier:a", "WhiteSpace: ", "Comment:# After", "EndOfLine:\\r\\n",
                    "EndOfLine:\\r\\n",
                    "Identifier:def", "WhiteSpace: ", "Identifier:f", "OpenGrouping:(", "EndOfLine:\\r\\n",
                    "WhiteSpace:    ", "Identifier:a", "Comma:,", "WhiteSpace: ", "Comment:#param", "EndOfLine:\\r\\n",
                    "CloseGrouping:)", "Colon::", "WhiteSpace: ", "Comment:#suite", "EndOfLine:\\r\\n",
                    "WhiteSpace:    ", "Identifier:pass", "WhiteSpace: ", "Comment:#stmt", "EndOfLine:\\r\\n",
                    "Comment:#func", "EndOfLine:\\r\\n",
                    "WhiteSpace:    ", "Identifier:pass", "EndOfLine:\\r\\n",
                    "WhiteSpace:    ", "Comment:#func", "EndOfLine:\\r\\n",
                    "Comment:#notfunc", "EndOfLine:\\r\\n",
                    "EndOfLine:\\r\\n",
                    "Identifier:a", "EndOfLine:\\r\\n",
                    "Comment:# Below", "EndOfLine:\\r\\n",
                    "EndOfLine:\\r\\n",
                    "Identifier:if", "WhiteSpace: ", "Identifier:True", "Colon::", "EndOfLine:\\r\\n",
                    "WhiteSpace:    ", "Comment:#block", "EndOfLine:\\r\\n",
                    "WhiteSpace:    ", "Identifier:pass", "EndOfLine:\\r\\n",
                    "EndOfLine:\\r\\n", "Comment:#eof", "EndOfStream:"
                );
        }
    }
}
