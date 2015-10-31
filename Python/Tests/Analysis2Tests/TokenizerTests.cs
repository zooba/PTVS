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
                "Name:a", "Whitespace: ", "Assign:=", "Whitespace:  ", "Name:b", "Add:+",
                "Whitespace:   ", "Name:c", "Whitespace: ", "EndOfFile:"
            );
        }

        [TestMethod, Priority(0)]
        public void MultiLineTokenization() {
            AssertTokens(
                Tokenize("a=b+c \nd = a + x   \n  ", PythonLanguageVersion.V35),
                "Name:a", "Assign:=", "Name:b", "Add:+", "Name:c", "Whitespace: ",
                "NewLine:\\n",
                "Name:d", "Whitespace: ", "Assign:=", "Whitespace: ", "Name:a", "Whitespace: ",
                "Add:+", "Whitespace: ", "Name:x", "Whitespace:   ", "NewLine:\\n",
                "SignificantWhitespace:  ", "EndOfFile:"
            );
        }

        [TestMethod, Priority(1)]
        public void CommentTokenization() {
            AssertTokens(
                Tokenize("# a=b+c \n\nd=a   # c\n  #eof", PythonLanguageVersion.V35),
                "Comment:# a=b+c ", "NewLine:\\n", "NewLine:\\n",
                "Name:d", "Assign:=", "Name:a", "Whitespace:   ", "Comment:# c", "NewLine:\\n",
                "SignificantWhitespace:  ", "Comment:#eof", "EndOfFile:"
            );

            AssertTokens(
                Tokenize(new FileSourceDocument(PythonTestData.GetTestDataSourcePath("Grammar\\Comments.py")), PythonLanguageVersion.V35),
                    "Comment:# Above", "NewLine:\\r\\n",
                    "Name:a", "NewLine:\\r\\n",
                    "NewLine:\\r\\n",
                    "Name:a", "Whitespace: ", "Comment:# After", "NewLine:\\r\\n",
                    "NewLine:\\r\\n",
                    "KeywordDef:def", "Whitespace: ", "Name:f", "LeftParenthesis:(", "NewLine:\\r\\n",
                    "Whitespace:    ", "Name:a", "Comma:,", "Whitespace: ", "Comment:#param", "NewLine:\\r\\n",
                    "RightParenthesis:)", "Colon::", "Whitespace: ", "Comment:#suite", "NewLine:\\r\\n",
                    "SignificantWhitespace:    ", "KeywordPass:pass", "Whitespace: ", "Comment:#stmt", "NewLine:\\r\\n",
                    "Comment:#func", "NewLine:\\r\\n",
                    "SignificantWhitespace:    ", "KeywordPass:pass", "NewLine:\\r\\n",
                    "SignificantWhitespace:    ", "Comment:#func", "NewLine:\\r\\n",
                    "Comment:#notfunc", "NewLine:\\r\\n",
                    "NewLine:\\r\\n",
                    "Name:a", "NewLine:\\r\\n",
                    "Comment:# Below", "NewLine:\\r\\n",
                    "NewLine:\\r\\n",
                    "KeywordIf:if", "Whitespace: ", "KeywordTrue:True", "Colon::", "NewLine:\\r\\n",
                    "SignificantWhitespace:    ", "Comment:#block", "NewLine:\\r\\n",
                    "SignificantWhitespace:    ", "KeywordPass:pass", "NewLine:\\r\\n",
                    "NewLine:\\r\\n", "Comment:#eof", "EndOfFile:"
                );
        }
    }
}
