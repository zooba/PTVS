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

using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.IronPythonTools.Interpreter;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Parsing;
using Microsoft.PythonTools.Repl;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Text.Classification;
using TestUtilities;
using TestUtilities.Mocks;
using TestUtilities.Python;

namespace IronPythonTests {
    [TestClass]
    public class IronPythonReplEvaluatorTests {
        static IronPythonReplEvaluatorTests() {
            AssertListener.Initialize();
            PythonTestData.Deploy(includeTestData: false);
        }

        protected virtual PythonVersion PythonVersion {
            get {
                return PythonPaths.IronPython27;
            }
        }

        private IPythonInterpreterFactory IronPythonInterpreter {
            get {
                if (PythonVersion == null) {
                    Assert.Inconclusive("Interpreter missing for " + GetType().Name);
                }
                var provider = new IronPythonInterpreterFactoryProvider();
                return provider.GetInterpreterFactories()
                    .First(f => f.Id == PythonVersion.Id &&
                                f.Configuration.Version == PythonVersion.Version.ToVersion());
            }
        }

        [TestMethod, Priority(1)]
        public void IronPythonModuleName() {
            var replEval = new PythonReplEvaluator(IronPythonInterpreter, PythonToolsTestUtilities.CreateMockServiceProvider(), new ReplTestReplOptions());
            var replWindow = new MockReplWindow(replEval);
            replEval._Initialize(replWindow).Wait();
            replWindow.ClearScreen();
            var execute = replEval.ExecuteText("__name__");
            execute.Wait();
            Assert.IsTrue(execute.Result.IsSuccessful);
            Assert.AreEqual(replWindow.Output, "'__main__'\r\n");
            replWindow.ClearScreen();
        }

        [TestMethod, Priority(1)]
        public void IronPythonSignatures() {
            var replEval = new PythonReplEvaluator(IronPythonInterpreter, PythonToolsTestUtilities.CreateMockServiceProvider(), new ReplTestReplOptions());
            var replWindow = new MockReplWindow(replEval);
            replEval._Initialize(replWindow).Wait();
            var execute = replEval.ExecuteText("from System import Array");
            execute.Wait();
            Assert.IsTrue(execute.Result.IsSuccessful);

            OverloadDoc[] sigs = null;
            for (int retries = 0; retries < 5 && sigs == null; retries += 1) {
                sigs = replEval.GetSignatureDocumentation("Array[int]");
            }
            Assert.IsNotNull(sigs, "GetSignatureDocumentation timed out");
            Assert.AreEqual(sigs.Length, 1);
            Assert.AreEqual("Array[int](: int)\r\n", sigs[0].Documentation);
        }

        [TestMethod, Priority(1)]
        public void IronPythonCommentInput() {
            // http://pytools.codeplex.com/workitem/649
            var replEval = new PythonReplEvaluator(IronPythonInterpreter, PythonToolsTestUtilities.CreateMockServiceProvider(), new ReplTestReplOptions());
            var replWindow = new MockReplWindow(replEval);
            replEval._Initialize(replWindow).Wait();
            var execute = replEval.ExecuteText("#fob\n1+2");
            execute.Wait();
            Assert.IsTrue(execute.Result.IsSuccessful);
        }

        [TestMethod, Priority(1)]
        public void ConsoleWriteLineTest() {
            // http://pytools.codeplex.com/workitem/649
            var replEval = new PythonReplEvaluator(IronPythonInterpreter, PythonToolsTestUtilities.CreateMockServiceProvider(), new ReplTestReplOptions());
            var replWindow = new MockReplWindow(replEval);
            replEval._Initialize(replWindow).Wait();
            var execute = replEval.ExecuteText("import System");
            execute.Wait();
            Assert.IsTrue(execute.Result.IsSuccessful);
            replWindow.ClearScreen();

            execute = replEval.ExecuteText("System.Console.WriteLine(42)");
            execute.Wait();
            Assert.AreEqual(replWindow.Output, "42\r\n");
            replWindow.ClearScreen();

            Assert.IsTrue(execute.Result.IsSuccessful);

            execute = replEval.ExecuteText("System.Console.Write(42)");
            execute.Wait();

            Assert.IsTrue(execute.Result.IsSuccessful);

            Assert.AreEqual(replWindow.Output, "42");
        }

        [TestMethod, Priority(1)]
        public void GenericMethodCompletions() {
            // http://pytools.codeplex.com/workitem/661
            var fact = IronPythonInterpreter;
            var replEval = new PythonReplEvaluator(fact, PythonToolsTestUtilities.CreateMockServiceProvider(), new ReplTestReplOptions());
            var replWindow = new MockReplWindow(replEval);
            replEval._Initialize(replWindow).Wait();
            var execute = replEval.ExecuteText("from System.Threading.Tasks import Task");
            execute.Wait();
            Assert.IsTrue(execute.Result.IsSuccessful);
            replWindow.ClearScreen();

            execute = replEval.ExecuteText("def func1(): print 'hello world'\r\n\r\n");
            execute.Wait();
            replWindow.ClearScreen();

            Assert.IsTrue(execute.Result.IsSuccessful);

            execute = replEval.ExecuteText("t = Task.Factory.StartNew(func1)");
            execute.Wait();
            Assert.IsTrue(execute.Result.IsSuccessful);

            using (var analyzer = new VsProjectAnalyzer(PythonToolsTestUtilities.CreateMockServiceProvider(), fact, new[] { fact })) {
                replWindow.TextView.TextBuffer.Properties.AddProperty(typeof(VsProjectAnalyzer), analyzer);

                MemberResult[] names = null;
                for (int retries = 0; retries < 5 && names == null; retries += 1) {
                    names = replEval.GetMemberNames("t");
                }
                Assert.IsNotNull(names, "GetMemberNames call timed out");
                foreach (var name in names) {
                    Debug.WriteLine(name.Name);
                }
            }
        }

        [TestMethod, Priority(1)]
        public void NoTraceFunction() {
            // http://pytools.codeplex.com/workitem/662
            var replEval = new PythonReplEvaluator(IronPythonInterpreter, PythonToolsTestUtilities.CreateMockServiceProvider(), new ReplTestReplOptions());
            var replWindow = new MockReplWindow(replEval);
            replEval._Initialize(replWindow).Wait();
            var execute = replEval.ExecuteText("import sys");
            execute.Wait();
            Assert.IsTrue(execute.Result.IsSuccessful);
            replWindow.ClearScreen();

            execute = replEval.ExecuteText("sys.gettrace()");
            execute.Wait();
            AssertUtil.AreEqual(
                new Regex(@"\<bound method Thread.trace_func of \<Thread.+\>\>"),
                replWindow.Output
            );
            replWindow.ClearScreen();
        }

        [TestMethod, Priority(1)]
        public void CommentFollowedByBlankLine() {
            // http://pytools.codeplex.com/workitem/659
            var replEval = new PythonReplEvaluator(IronPythonInterpreter, PythonToolsTestUtilities.CreateMockServiceProvider(), new ReplTestReplOptions());
            var replWindow = new MockReplWindow(replEval);
            replEval._Initialize(replWindow).Wait();
            var execute = replEval.ExecuteText("# fob\r\n\r\n    \r\n\t\t\r\na = 42");
            execute.Wait();
            Assert.IsTrue(execute.Result.IsSuccessful);
            replWindow.ClearScreen();
        }



        [TestMethod, Priority(1)]
        public void AttachSupportMultiThreaded() {
            // http://pytools.codeplex.com/workitem/663
            var replEval = new PythonReplEvaluator(IronPythonInterpreter, PythonToolsTestUtilities.CreateMockServiceProvider(), new ReplTestReplOptions());
            var replWindow = new MockReplWindow(replEval);
            replEval._Initialize(replWindow).Wait();
            var code = new[] {
                "import threading",
                "def sayHello():\r\n    pass",
                "t1 = threading.Thread(target=sayHello)",
                "t1.start()",
                "t2 = threading.Thread(target=sayHello)",
                "t2.start()"
            };
            foreach (var line in code) {
                var execute = replEval.ExecuteText(line);
                execute.Wait();
                Assert.IsTrue(execute.Result.IsSuccessful);
            }

            replWindow.ClearScreen();
            var finalExecute = replEval.ExecuteText("42");
            finalExecute.Wait();
            Assert.IsTrue(finalExecute.Result.IsSuccessful);
            Assert.AreEqual(replWindow.Output, "42\r\n");
        }
    }


    [TestClass]
    public class IronPythonx64ReplEvaluatorTests : IronPythonReplEvaluatorTests {
        protected override PythonVersion PythonVersion {
            get {
                return PythonPaths.IronPython27_x64;
            }
        }
    }
}

