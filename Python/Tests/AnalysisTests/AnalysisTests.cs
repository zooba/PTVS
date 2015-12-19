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
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Values;
using Microsoft.PythonTools.Common.Infrastructure;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestUtilities;

namespace AnalysisTests {
    [TestClass, Ignore]
    public abstract class AnalysisTests {
        static AnalysisTests() {
            AssertListener.Initialize();
        }

        public static PythonLanguageServiceProvider LanguageServiceProvider;
        public static PythonFileContextProvider FileContextProvider;

        public static int Counter { get; set; }

        public abstract InterpreterConfiguration Configuration { get; }

        public string Bytes => Configuration.Version.Is3x() ? "bytes" : "str";
        public string Unicode => Configuration.Version.Is3x() ? "str" : "unicode";

        [TestInitialize]
        public void TestInitialize() {
            LanguageServiceProvider = new PythonLanguageServiceProvider(new[] {
                new OperatorModuleProvider()
            });
            FileContextProvider = new PythonFileContextProvider();
            Counter = 0;
        }

        [TestCleanup]
        public void TestCleanup() {
            FileContextProvider.Dispose();
            LanguageServiceProvider.Dispose();
            AssertListener.ThrowUnhandled();
        }

        [TestMethod, Priority(0)]
        public async Task AssignNumber() {
            var state = await @"x = 0
if True:
    y = 1.0

def h():
    z = 2".AnalyzeAsync(Configuration);
            await state.AssertAnnotationsAsync("x", "int");
            await state.AssertAnnotationsAsync("y", "float");
            await state.AssertAnnotationsAsync("z");
        }

        [TestMethod, Priority(0)]
        public async Task AssignString() {
            var state = await @"x = 'abc'
if True:
    y = u'def'

z = b'ghi'
".AnalyzeAsync(Configuration);
            await state.AssertAnnotationsAsync("x", "str");
            await state.AssertAnnotationsAsync("y", Unicode);
            await state.AssertAnnotationsAsync("z", Bytes);
        }

        [TestMethod, Priority(0)]
        public async Task Functions() {
            var state = await @"def f(): pass

if True:
    def g():
        def h(): pass".AnalyzeAsync(Configuration);
            await state.AssertAnnotationsAsync("f", "Callable");
            await state.AssertAnnotationsAsync("g", "Callable");
            await state.AssertAnnotationsAsync("h");
        }

        [TestMethod, Priority(0)]
        public async Task Classes() {
            var state = await @"class A: pass

if True:
    class B(object):
        def h(): pass".AnalyzeAsync(Configuration);
            await state.AssertAnnotationsAsync("A", "A");
            await state.AssertAnnotationsAsync("B", "B");
            await state.AssertAnnotationsAsync("h");
        }

        [TestMethod, Priority(0)]
        public async Task AssignVariable() {
            var state = await @"x = 1.0
y = x
x = 1".AnalyzeAsync(Configuration);
            await state.AssertAnnotationsAsync("x", "int", "float");
            await state.AssertAnnotationsAsync("y", "int", "float");
        }

        [TestMethod, Priority(0)]
        public async Task AssignVariableScope() {
            var state = await @"x = 1.0

def f():
    y = x

def g():
    y = x
    x = 1".AnalyzeAsync(Configuration);
            await state.AssertAnnotationsAsync("x", "float");
            await state.AssertAnnotationsAsync("f@1#y", "float");
            await state.AssertAnnotationsAsync("g@2#y", "int");
        }

        [TestMethod, Priority(0)]
        public async Task AssignCallResult() {
            var state = await @"def f(): return 1
def g(a): return a
def h(): return x

x = f()
y = g(1)
z = g('abc')
w = h()
x = z".AnalyzeAsync(Configuration);
            await state.AssertAnnotationsAsync("f@1#$r", "int");
            await state.AssertAnnotationsAsync("x", "int", "str");
            await state.AssertAnnotationsAsync("g@2#$r", "Parameter[0]");
            await state.AssertAnnotationsAsync("y", "int");
            await state.AssertAnnotationsAsync("z", "str");
            await state.AssertAnnotationsAsync("w", "int", "str");
            // TODO: Make annotation include parameter and return types
            await state.AssertAnnotationsAsync("f", "Callable");
            await state.AssertAnnotationsAsync("g", "Callable");
            await state.AssertAnnotationsAsync("h", "Callable");
        }

        [TestMethod, Priority(0)]
        public async Task NumericOperators() {
            var state = await @"x = 1 + 2
y = 1.0 + 2
z_p = x + y
z_m = x - y
".AnalyzeAsync(Configuration);
            await state.AssertAnnotationsAsync("x", "int");
            await state.AssertAnnotationsAsync("y", "float");
            await state.AssertAnnotationsAsync("z_p", "float");
            await state.AssertAnnotationsAsync("z_m", "float");

            state = await @"x = 3 / 2
y = 3. / 2
z = 3 / 2.".AnalyzeAsync(Configuration);
            await state.AssertAnnotationsAsync("x", state.Features.HasTrueDivision ? "float" : "int");
            await state.AssertAnnotationsAsync("y", "float");
            await state.AssertAnnotationsAsync("z", "float");
        }

        public virtual IEnumerable<string> BuiltinFunctionNames {
            get {
                yield return "abs x";
                yield return "all bool";
                yield return "any bool";
                yield return "ascii str";
                yield return "bin str";
                yield return "callable bool";
                yield return "compile code";
                yield return "delattr None";
            }
        }

        [TestMethod, Priority(0)]
        public async Task BuiltinFunctions() {
            var code = new StringBuilder();
            var expected = new Dictionary<string, string>();
            foreach (var fn in BuiltinFunctionNames) {
                var name = fn.Split(' ')[0];
                var type = fn.Split(' ')[1];
                code.AppendLine(name + "_ = " + name + "(1)");
                if (type == "x") {
                    expected[name + "_"] = "int";
                    code.AppendLine(name + "_f = " + name + "(1.0)");
                    expected[name + "_f"] = "float";
                } else {
                    expected[name + "_"] = type;
                }
            }
            var state = await code.ToString().AnalyzeAsync(Configuration);
            foreach (var kv in expected) {
                await state.AssertAnnotationsAsync(kv.Key, kv.Value);
            }
        }
    }

    static class AnalysisTestsHelpers {
        public static async Task AssertAnnotationsAsync(this IAnalysisState state, string key, params string[] annotations) {
            var cancel = CancellationTokens.After5s;
            var values = await state.GetTypesAsync(key, cancel);
            var types = new HashSet<string>();
            foreach (var v in values) { types.Add(await v.ToAnnotationAsync(cancel)); }
            if (annotations.Any()) {
                Assert.IsTrue(types.Any(), "No types returned for " + key);
                var expected = new HashSet<string>(annotations);
                var expectedNotFound = new HashSet<string>(expected);
                expectedNotFound.ExceptWith(types);
                var foundNotExpected = new HashSet<string>(types);
                foundNotExpected.ExceptWith(expected);

                string message = null;
                if (expectedNotFound.Any()) {
                    message = string.Format(
                        "Did not find {{{0}}}{2}{2}",
                        string.Join(", ", expectedNotFound.Ordered()),
                        string.Join(", ", types.Ordered()),
                        Environment.NewLine
                    );
                }

                if (foundNotExpected.Any()) {
                    message = string.Format(
                        "{1}Did not expect {{{0}}}{2}{2}",
                        string.Join(", ", foundNotExpected.Ordered()),
                        message ?? "",
                        Environment.NewLine
                    );
                }

                if (message != null) {
                    message = string.Format(
                        "{3}{0} included {{{1}}}.{3}{3}{2}",
                        key,
                        string.Join(", ", types.Ordered()),
                        message,
                        Environment.NewLine
                    );
                    Assert.Fail(message.TrimEnd());
                }
            } else {
                Assert.IsFalse(types.Any(), string.Format(
                    "Expected no types{2}{0} included {{{1}}}",
                    key,
                    string.Join(", ", types.Ordered()),
                    Environment.NewLine
                ));
            }
        }

        public static Task<IAnalysisState> AnalyzeAsync(
            this string code,
            InterpreterConfiguration config,
            bool wait = true,
            CancellationToken cancellationToken = default(CancellationToken),
            [CallerMemberName] string filename = null
        ) {
            return new StringLiteralDocument(code, $"Tests\\{filename}_{AnalysisTests.Counter++}.py").AnalyzeAsync(
                config,
                wait,
                cancellationToken
            );
        }

        public static async Task<IAnalysisState> AnalyzeAsync(
            this ISourceDocument document,
            InterpreterConfiguration config,
            bool wait = true,
            CancellationToken cancellationToken = default(CancellationToken)
        ) {
            if (Debugger.IsAttached) {
                if (cancellationToken.CanBeCanceled) {
                    cancellationToken = default(CancellationToken);
                }
            } else {
                if (!cancellationToken.CanBeCanceled) {
                    cancellationToken = CancellationTokens.After5s;
                }
            }

            var lsp = AnalysisTests.LanguageServiceProvider;
            var fcp = AnalysisTests.FileContextProvider;
            var analyzer = await lsp.GetServiceAsync(
                config,
                fcp,
                cancellationToken
            );
            var context = await fcp.GetOrCreateContextAsync(
                "Tests",
                cancellationToken
            );

            await context.AddDocumentsAsync(new[] { document }, cancellationToken);
            await analyzer.AddFileContextAsync(context, cancellationToken);

            var state = analyzer.GetAnalysisState(context, document.Moniker, false);
            if (wait) {
                await state.WaitForUpToDateAsync(cancellationToken);
                await state.DumpAsync(Console.Out, cancellationToken);
                var aState = state as AnalysisState;
                if (aState != null) {
                    await aState.DumpTraceAsync(Console.Error, cancellationToken);
                }
            }
            return state;
        }
    }

    [TestClass]
    public class Python27AnalysisTests : AnalysisTests {
        public override InterpreterConfiguration Configuration => PythonPaths.Python27.Configuration;
    }

    [TestClass]
    public class Python35AnalysisTests : AnalysisTests {
        public override InterpreterConfiguration Configuration => PythonPaths.Python35.Configuration;
    }
}
