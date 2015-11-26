extern alias analysis;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using analysis::Microsoft.PythonTools.Analysis.Analyzer;
using analysis::Microsoft.PythonTools.Parsing;
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

        private static Tokenization Tokenize(string text, PythonLanguageVersion version, bool verbatim = true) {
            return Tokenization.TokenizeAsync(
                new StringLiteralDocument(text),
                version,
                verbatim ? TokenizerOptions.Verbatim : TokenizerOptions.None,
                Severity.Ignore
            ).GetAwaiter().GetResult();
        }

        private static string MakeString(TokenWithSpan t) {
            var s = string.Format("{0}:{1}//{2}", t.Token.Kind, t.LeadingWhitespace, t.Token.Image);

            s = s.Replace("\r", "\\r");
            s = s.Replace("\n", "\\n");
            s = s.Replace("\t", "\\t");

            return s;
        }

        private static void AssertTokens(Tokenization tokenization, params string[] expected) {
            var actualList = Environment.NewLine + string.Join(", ", tokenization.RawTokens.Select(t =>
                string.Format("\"{0}\"", MakeString(t).Replace("\\", "\\\\"))
            ));

            using (var e = tokenization.RawTokens.GetEnumerator()) {
                for (int i = 0; i < expected.Length; ++i) {
                    Assert.IsTrue(e.MoveNext(), "Not enough tokens" + actualList);

                    var actual = MakeString(e.Current);
                    Assert.AreEqual(expected[i], actual, "Mismatch" + actualList);
                }

                Assert.IsFalse(e.MoveNext(), "Unexpected tokens" + actualList);
            }
        }

        [TestMethod]
        public void SingleLineTokenization() {
            AssertTokens(
                Tokenize("a =  b+   c ", PythonLanguageVersion.V35),
                "Name://a", "Assign: //=", "Name:  //b", "Add://+", "Name:   //c", "EndOfFile: //<eof>"
            );
        }

        [TestMethod]
        public void MultiLineTokenization() {
            AssertTokens(
                Tokenize(@"a=b+c 
d = a + x   
  ".Replace("\r\n", "\n"), PythonLanguageVersion.V35),
                "Name://a", "Assign://=", "Name://b", "Add://+", "Name://c", "NewLine: //<newline>",
                "Name://d", "Assign: //=", "Name: //a", "Add: //+", "Name: //x", "NewLine:   //<newline>",
                "EndOfFile:  //<eof>"
            );
        }

        [TestMethod]
        public void CommentTokenization() {
            AssertTokens(
                Tokenize(@"# a=b+c 

d=a   # c
  #eof".Replace("\r\n", "\n"), PythonLanguageVersion.V35, verbatim: false),
                "Comment://# a=b+c ", "NewLine://<newline>",
                "NLToken://<NL>",
                "Name://d", "Assign://=", "Name://a", "Comment://# c", "NewLine://<newline>",
                "Comment://#eof", "EndOfFile://<eof>"
            );

            AssertTokens(
                Tokenize(File.ReadAllText(PythonTestData.GetTestDataSourcePath("Grammar\\Comments.py")), PythonLanguageVersion.V35, verbatim: false),
                "Comment://# Above", "NewLine://<newline>",
                "Name://a", "NewLine://<newline>",
                "NLToken://<NL>",
                "Name://a", "Comment://# After", "NewLine://<newline>",
                "NLToken://<NL>",
                "KeywordDef://def", "Name://f", "LeftParenthesis://(",
                "Name://a", "Comma://,", "Comment://#param",
                "RightParenthesis://)", "Colon://:", "Comment://#suite", "NewLine://<newline>",
                "Indent://<indent>", "KeywordPass://pass", "Comment://#stmt", "NewLine://<newline>",
                "Comment://#func", "NLToken://<NL>",
                "KeywordPass://pass", "NewLine://<newline>",
                "Comment://#func", "NLToken://<NL>",
                "Dedent://<dedent>", "Comment://#notfunc", "NLToken://<NL>",
                "NLToken://<NL>",
                "Name://a", "NewLine://<newline>",
                "Comment://# Below", "NLToken://<NL>",
                "NLToken://<NL>",
                "KeywordIf://if", "KeywordTrue://True", "Colon://:", "NewLine://<newline>",
                "Indent://<indent>", "Comment://#block", "NLToken://<NL>",
                "KeywordPass://pass", "NewLine://<newline>",
                "NLToken://<NL>",
                "Dedent://<dedent>", "Comment://#eof", "EndOfFile://<eof>"
            );
        }

        [TestMethod]
        public void VerbatimCommentTokenization() {
            AssertTokens(
                Tokenize(@"# a=b+c 

d=a   # c
  #eof".Replace("\r\n", "\n"), PythonLanguageVersion.V35),
                "Comment://# a=b+c ", "NewLine://<newline>",
                "NLToken://<NL>",
                "Name://d", "Assign://=", "Name://a", "Comment:   //# c", "NewLine://<newline>",
                "Comment:  //#eof", "EndOfFile://<eof>"
            );

            AssertTokens(
                Tokenize(File.ReadAllText(PythonTestData.GetTestDataSourcePath("Grammar\\Comments.py")), PythonLanguageVersion.V35),
                "Comment://# Above", "NewLine://<newline>",
                "Name://a", "NewLine://<newline>",
                "NLToken://<NL>",
                "Name://a", "Comment: //# After", "NewLine://<newline>",
                "NLToken://<NL>",
                "KeywordDef://def", "Name: //f", "LeftParenthesis://(",
                "Name:\\r\\n    //a", "Comma://,", "Comment: //#param",
                "RightParenthesis://)", "Colon://:", "Comment: //#suite", "NewLine://<newline>",
                "Indent:    //<indent>", "KeywordPass://pass", "Comment: //#stmt", "NewLine://<newline>",
                "Comment://#func", "NLToken://<NL>",
                "KeywordPass:    //pass", "NewLine://<newline>",
                "Comment:    //#func", "NLToken://<NL>",
                "Dedent://<dedent>", "Comment://#notfunc", "NLToken://<NL>",
                "NLToken://<NL>",
                "Name://a", "NewLine://<newline>",
                "Comment://# Below", "NLToken://<NL>",
                "NLToken://<NL>",
                "KeywordIf://if", "KeywordTrue: //True", "Colon://:", "NewLine://<newline>",
                "Indent://<indent>", "Comment:    //#block", "NLToken://<NL>",
                "KeywordPass:    //pass", "NewLine://<newline>",
                "NLToken://<NL>",
                "Dedent://<dedent>", "Comment://#eof", "EndOfFile://<eof>"
            );
        }
    }
}
