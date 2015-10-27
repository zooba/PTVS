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
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.PythonTools;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Parsing;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.MockVsTests;
using TestUtilities;
using TestUtilities.Mocks;
using TestUtilities.Python;

namespace PythonToolsMockTests {
    [TestClass]
    public class ClassifierTests {
        [ClassInitialize]
        public static void DoDeployment(TestContext context) {
            AssertListener.Initialize();
            PythonTestData.Deploy(includeTestData: false);
        }

        [TestMethod, Priority(0), TestCategory("Mock")]
        public void KeywordClassification27() {
            var code = string.Join(Environment.NewLine, PythonKeywords.All(PythonLanguageVersion.V27));
            code += "\r\nTrue\r\nFalse";

            using (var helper = new ClassifierHelper(code, PythonLanguageVersion.V27)) {
                foreach (var span in helper.AstClassifierSpans) {
                    var text = span.Span.GetText();
                    if (string.IsNullOrWhiteSpace(text)) {
                        continue;
                    }

                    // None, True and False are special
                    if (text == "None" || text == "True" || text == "False") {
                        Assert.AreEqual("Python builtin", span.ClassificationType.Classification, text);
                        continue;
                    }

                    Assert.AreEqual("keyword", span.ClassificationType.Classification, text);
                }
            }
        }

        [TestMethod, Priority(0), TestCategory("Mock")]
        public void KeywordClassification33() {
            var code = string.Join(Environment.NewLine, PythonKeywords.All(PythonLanguageVersion.V33));

            using (var helper = new ClassifierHelper(code, PythonLanguageVersion.V33)) {
                foreach (var span in helper.AstClassifierSpans) {
                    var text = span.Span.GetText();
                    if (string.IsNullOrWhiteSpace(text)) {
                        continue;
                    }

                    // None is special
                    if (text == "None") {
                        Assert.AreEqual("Python builtin", span.ClassificationType.Classification, text);
                        continue;
                    }

                    Assert.AreEqual("keyword", span.ClassificationType.Classification, text);
                }
            }
        }

        [TestMethod, Priority(0), TestCategory("Mock")]
        public void ModuleClassification() {
            var code = @"import abc
import os
import ntpath

os.path = ntpath
abc = 123
abc = True
";
            using (var helper = new ClassifierHelper(code, PythonLanguageVersion.V27)) {
                helper.CheckAstClassifierSpans("ki ki ki i.i=i i=n i=b");
                helper.CheckAnalysisClassifierSpans("m<abc>m<os>m<ntpath>m<os>m<ntpath>m<abc>m<abc>");
            }
        }

        private static MockTextBuffer MockTextBuffer(string code) {
            return new MockTextBuffer(code, PythonCoreConstants.ContentType, "C:\\fob.py");
        }

        [TestMethod, Priority(0), TestCategory("Mock")]
        public void ImportClassifications() {
            var code = @"import abc as x
from os import fdopen

abc
x
os
fdopen
";
            using (var helper = new ClassifierHelper(code, PythonLanguageVersion.V27)) {
                helper.CheckAstClassifierSpans("kiki kiki i i i i");
                helper.CheckAnalysisClassifierSpans("m<abc>m<x>m<os>m<x>");
            }
        }

        [TestMethod, Priority(0), TestCategory("Mock")]
        public void TypeClassification() {
            var code = @"class MyClass(object):
    pass

mc = MyClass()
MyClassAlias = MyClass
mca = MyClassAlias()
MyClassType = type(mc)
";
            using (var helper = new ClassifierHelper(code, PythonLanguageVersion.V27)) {
                helper.CheckAstClassifierSpans("ki(i): k i=i() i=i i=i() i=i(i)");
                helper.CheckAnalysisClassifierSpans("c<MyClass>c<object>cc<MyClassAlias>ccc<type>");
            }
        }

        [TestMethod, Priority(0), TestCategory("Mock")]
        public void ParameterClassification() {
            var code = @"def f(a, b, c):
    a = b
    b = c
    return a

f(a, b, c)
a = b
b = c
";
            using (var helper = new ClassifierHelper(code, PythonLanguageVersion.V27)) {
                helper.CheckAstClassifierSpans("ki(i,i,i): i=i i=i ki i(i,i,i) i=i i=i");
                helper.CheckAnalysisClassifierSpans("f<f>ppppppppf<f>");
            }
        }

        [TestMethod, Priority(0), TestCategory("Mock")]
        public void ParameterAnnotationClassification() {
            var code = @"class A: pass
class B: pass

def f(a = A, b : B):
    pass
";
            using (var helper = new ClassifierHelper(code, PythonLanguageVersion.V27)) {
                helper.CheckAstClassifierSpans("ki:k ki:k ki(i=i,i:i): k");
                helper.CheckAnalysisClassifierSpans("c<A>c<B>f<f>pc<A>pc<B>");
            }
        }

        [TestMethod, Priority(0), TestCategory("Mock")]
        public void TrueFalseClassification() {
            var code = "True False";

            using (var helper = new ClassifierHelper(code, PythonLanguageVersion.V27)) {
                helper.CheckAstClassifierSpans("b<True> b<False>");
            }

            using (var helper = new ClassifierHelper(code, PythonLanguageVersion.V33)) {
                helper.CheckAstClassifierSpans("k<True> k<False>");
            }
        }

        [TestMethod, Priority(0), TestCategory("Mock")]
        public void AsyncAwaitClassification() {
            var code = @"
await f
await + f
async with f: pass
async for x in f: pass

async def f():
    await f
    async with f: pass
    async for x in f: pass

class F:
    async def f(self): pass

";

            using (var helper = new ClassifierHelper(code, PythonLanguageVersion.V35)) {
                helper.CheckAstClassifierSpans("ii i+i iki:k ikiki:k iki(): ii iki:k ikiki:k ki: iki(i): k");
                // "await f" does not highlight "f", but "await + f" does
                helper.CheckAnalysisClassifierSpans("fff k<async>f k<await>f k<async>f k<async>f c<F> k<async>fp");
            }
        }

        #region ClassifierHelper class

        private class ClassifierHelper : IDisposable {
            private readonly PythonClassifierProvider _provider1;
            private readonly PythonAnalysisClassifierProvider _provider2;
            private readonly ManualResetEventSlim _classificationsReady1, _classificationsReady2;
            private readonly PythonEditor _view;

            public ClassifierHelper(string code, PythonLanguageVersion version) {
                _view = new PythonEditor("", version);

                var providers = _view.VS.ComponentModel.GetExtensions<IClassifierProvider>().ToArray();
                _provider1 = providers.OfType<PythonClassifierProvider>().Single();
                _provider2 = providers.OfType<PythonAnalysisClassifierProvider>().Single();

                _classificationsReady1 = new ManualResetEventSlim();
                _classificationsReady2 = new ManualResetEventSlim();

                AstClassifier.ClassificationChanged += (s, e) => _classificationsReady1.Set();
                AnalysisClassifier.ClassificationChanged += (s, e) => _classificationsReady2.Set();

                _view.Text = code;
            }

            public void Dispose() {
                _classificationsReady1.Dispose();
                _classificationsReady2.Dispose();
                _view.Dispose();
            }

            public ITextView TextView {
                get {
                    return _view.View.TextView;
                }
            }

            public ITextBuffer TextBuffer {
                get {
                    return _view.View.TextView.TextBuffer;
                }
            }

            public IClassifier AstClassifier {
                get {
                    return _provider1.GetClassifier(TextBuffer);
                }
            }

            public IEnumerable<ClassificationSpan> AstClassifierSpans {
                get {
                    _classificationsReady1.Wait();
                    return AstClassifier.GetClassificationSpans(
                        new SnapshotSpan(TextBuffer.CurrentSnapshot, 0, TextBuffer.CurrentSnapshot.Length)
                    ).OrderBy(s => s.Span.Start.Position);
                }
            }

            public IClassifier AnalysisClassifier {
                get {
                    return _provider2.GetClassifier(TextBuffer);
                }
            }

            public IEnumerable<ClassificationSpan> AnalysisClassifierSpans {
                get {
                    _classificationsReady2.Wait();
                    return AnalysisClassifier.GetClassificationSpans(
                        new SnapshotSpan(TextBuffer.CurrentSnapshot, 0, TextBuffer.CurrentSnapshot.Length)
                    ).OrderBy(s => s.Span.Start.Position);
                }
            }

            private static readonly Dictionary<char, string> ClassificationAbbreviations = new Dictionary<char, string> {
                { 'm', PythonPredefinedClassificationTypeNames.Module },
                { 'c', PythonPredefinedClassificationTypeNames.Class },
                { 'f', PythonPredefinedClassificationTypeNames.Function },
                { 'p', PythonPredefinedClassificationTypeNames.Parameter },
                { 'b', PythonPredefinedClassificationTypeNames.Builtin },
                { 'i', PredefinedClassificationTypeNames.Identifier },
                { 'l', PredefinedClassificationTypeNames.Literal },
                { 'n', PredefinedClassificationTypeNames.Number },
                { 'k', PredefinedClassificationTypeNames.Keyword },
                { '(', PythonPredefinedClassificationTypeNames.Grouping },
                { ')', PythonPredefinedClassificationTypeNames.Grouping },
                { '+', PythonPredefinedClassificationTypeNames.Operator },
                { ':', PythonPredefinedClassificationTypeNames.Operator },
                { '=', PythonPredefinedClassificationTypeNames.Operator },
                { ',', PythonPredefinedClassificationTypeNames.Comma },
                { '.', PythonPredefinedClassificationTypeNames.Dot },
            };

            private static IEnumerable<string> ExpandNames(string classifications) {
                return classifications.Select(c => {
                    string name;
                    return ClassificationAbbreviations.TryGetValue(c, out name) ? name : null;
                }).Where(n => !string.IsNullOrEmpty(n));
            }

            public void CheckAstClassifierSpans(string expectedSpans) {
                CheckClassifierSpans(AstClassifierSpans, expectedSpans);
            }

            public void CheckAnalysisClassifierSpans(string expectedSpans) {
                CheckClassifierSpans(AnalysisClassifierSpans, expectedSpans);
            }

            private void CheckClassifierSpans(IEnumerable<ClassificationSpan> spans, string expectedSpans) {
                spans = spans
                    .Where(s => !s.ClassificationType.IsOfType(PredefinedClassificationTypeNames.WhiteSpace))
                    .ToArray();

                var sb = new StringBuilder("Actual: ");
                foreach (var span in spans) {
                    var code = ClassificationAbbreviations.FirstOrDefault(kv => kv.Value == span.ClassificationType.Classification).Key;
                    if (code == '\0') {
                        Console.WriteLine("No code for {0}", span.ClassificationType.Classification);
                        sb.AppendFormat("{{{0}}}", span.ClassificationType.Classification);
                    } else {
                        sb.AppendFormat("{0}", code);
                    }
                    var text = span.Span.GetText();
                    if (!string.IsNullOrEmpty(text)) {
                        sb.AppendFormat("<{0}>", text);
                    }
                }
                Console.WriteLine(sb.ToString());

                foreach (var span in spans) {
                    if (string.IsNullOrEmpty(expectedSpans)) {
                        Assert.Fail("Not enough spans expected");
                    }
                    string expected;
                    var match = Regex.Match(expectedSpans, @"\s*(?<code>.)(\<(?<token>.+?)\>)?(?<rest>.*)$");
                    if (!match.Success) {
                        break;
                    }

                    Assert.IsTrue(ClassificationAbbreviations.TryGetValue(match.Groups["code"].Value[0], out expected), "Did not understand character '" + match.Groups["code"].Value + "'");

                    string expectedToken = "(none)";
                    if (match.Groups["token"].Success) {
                        expectedToken = match.Groups["token"].Value;
                    }

                    Assert.IsTrue(
                        span.ClassificationType.IsOfType(expected),
                        string.Format("Expected <{0}>. Actual <{1}>. Expected token <{2}> Actual token <{3}>.", expected, span.ClassificationType.Classification, expectedToken, span.Span.GetText())
                    );

                    if (match.Groups["token"].Success) {
                        Assert.AreEqual(expectedToken, span.Span.GetText());
                    }

                    expectedSpans = match.Groups["rest"].Value;
                }

                Assert.IsTrue(string.IsNullOrEmpty(expectedSpans), "Remaining: " + expectedSpans);
            }
        }

        #endregion
    }
}
