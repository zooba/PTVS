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

        [ThreadStatic]
        public static PythonLanguageServiceProvider LanguageServiceProvider;
        [ThreadStatic]
        public static PythonFileContextProvider FileContextProvider;

        public abstract InterpreterConfiguration Configuration { get; }

        public string Bytes => Configuration.Version.Is3x() ? "bytes" : "str";
        public string Unicode => Configuration.Version.Is3x() ? "str" : "unicode";

        [TestInitialize]
        public void TestInitialize() {
            LanguageServiceProvider = new PythonLanguageServiceProvider();
            FileContextProvider = new PythonFileContextProvider();
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
            await state.AssertAnnotationsAsync("f@11#y", "float");
            await state.AssertAnnotationsAsync("g@34#y", "int");
        }

        [TestMethod, Priority(0)]
        public async Task AssignCallResult() {
            var state = await @"def f(): return 1
def g(a): return a

x = f()
y = g(1)".AnalyzeAsync(Configuration);
            await state.AssertAnnotationsAsync("f@0#$r", "int");
            await state.AssertAnnotationsAsync("x", "int");
            await state.AssertAnnotationsAsync("g@19#$r", "Parameter[0]");
            await state.AssertAnnotationsAsync("y", "int");
            // TODO: Make annotation include parameter and return types
            await state.AssertAnnotationsAsync("f", "Callable");
            await state.AssertAnnotationsAsync("g", "Callable");
        }
    }

    static class AnalysisTestsHelpers {
        public static async Task AssertAnnotationsAsync(this AnalysisState state, string key, params string[] annotations) {
            var values = await state.GetTypesAsync(key, CancellationTokens.After5s);
            if (annotations.Any()) {
                Assert.IsNotNull(values, "No types returned for " + key);
                var types = new HashSet<string>(values.Select(v => v.ToAnnotation(state)));
                var expected = new HashSet<string>(annotations);
                var expectedNotFound = new HashSet<string>(expected);
                expectedNotFound.ExceptWith(types);
                var foundNotExpected = new HashSet<string>(types);
                foundNotExpected.ExceptWith(expected);

                string message = null;
                if (expectedNotFound.Any()) {
                    message = string.Format(
                        "Did not find {{{0}}}{2}{2}",
                        string.Join(", ", expectedNotFound.OrderBy(s => s)),
                        string.Join(", ", types.OrderBy(s => s)),
                        Environment.NewLine
                    );
                }

                if (foundNotExpected.Any()) {
                    message = string.Format(
                        "{1}Did not expect {{{0}}}{2}{2}",
                        string.Join(", ", foundNotExpected.OrderBy(s => s)),
                        message ?? "",
                        Environment.NewLine
                    );
                }

                if (message != null) {
                    message = string.Format(
                        "{3}{0} included {{{1}}}.{3}{3}{2}",
                        key,
                        string.Join(", ", types.OrderBy(s => s)),
                        message,
                        Environment.NewLine
                    );
                    Assert.Fail(message.TrimEnd());
                }
            } else {
                Assert.IsNull(values, "Expected no types");
            }
        }

        public static Task<AnalysisState> AnalyzeAsync(
            this string code,
            InterpreterConfiguration config,
            CancellationToken cancellationToken = default(CancellationToken),
            [CallerMemberName] string filename = null
        ) {
            return new StringLiteralDocument(code, "Tests\\" + filename + ".py").AnalyzeAsync(
                config,
                cancellationToken
            );
        }

        public static async Task<AnalysisState> AnalyzeAsync(
            this ISourceDocument document,
            InterpreterConfiguration config,
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

            var state = await analyzer.GetAnalysisStateAsync(
                context,
                document.Moniker,
                false,
                cancellationToken
            );
            await state.WaitForUpToDateAsync(cancellationToken);
            state.Dump(Console.Out);
            return state;
        }
    }

    [TestClass]
    public class Python35AnalysisTests : AnalysisTests {
        public override InterpreterConfiguration Configuration => PythonPaths.Python35.Configuration;
    }
}
