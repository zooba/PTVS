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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestUtilities;

namespace AnalysisTests {
    [TestClass, Ignore]
    public abstract class AnalysisTests {
        [ClassInitialize]
        public static void Initialize(TestContext context) {
            AssertListener.Initialize();
        }

        PythonLanguageServiceProvider LanguageServiceProvider { get; set; }
        PythonFileContextProvider FileContextProvider { get; set; }
        public abstract InterpreterConfiguration Configuration { get; }

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
            var state = await AnalyzeAsync(@"x = 0
if True:
    y = 1.0

def h():
    z = 2", CancellationTokens.After5s);
            var x = await state.GetTypesAsync("x", CancellationTokens.After5s);
            AssertAnnotations(x, "int");
            var y = await state.GetTypesAsync("y", CancellationTokens.After5s);
            AssertAnnotations(y, "float");
            var z = await state.GetTypesAsync("z", CancellationTokens.After5s);
            AssertAnnotations(z);
        }

        [TestMethod, Priority(0)]
        public async Task Functions() {
            var state = await AnalyzeAsync(@"def f(): pass

if True:
    def g():
        def h(): pass", CancellationTokens.After5s);
            var f = await state.GetTypesAsync("f", CancellationTokens.After5s);
            AssertAnnotations(f, "Callable");
            var g = await state.GetTypesAsync("g", CancellationTokens.After5s);
            AssertAnnotations(g, "Callable");
            var h = await state.GetTypesAsync("h", CancellationTokens.After5s);
            AssertAnnotations(h);
        }

        [TestMethod, Priority(0)]
        public async Task Classes() {
            var state = await AnalyzeAsync(@"class A: pass

if True:
    class B(object):
        def h(): pass", CancellationTokens.After5s);
            var f = await state.GetTypesAsync("A", CancellationTokens.After5s);
            AssertAnnotations(f, "A");
            var g = await state.GetTypesAsync("B", CancellationTokens.After5s);
            AssertAnnotations(g, "B");
            var h = await state.GetTypesAsync("h", CancellationTokens.After5s);
            AssertAnnotations(h);
        }

        private void AssertAnnotations(IEnumerable<AnalysisValue> values, params string[] annotations) {
            if (annotations.Any()) {
                Assert.IsNotNull(values, "No types returned");
                AssertUtil.ContainsExactly(values.Select(v => v.ToAnnotation()), annotations);
            } else {
                Assert.IsNull(values, "Expected no types");
            }
        }

        private async Task<AnalysisState> AnalyzeAsync(
            ISourceDocument document,
            CancellationToken cancellationToken,
            InterpreterConfiguration config = null
        ) {
            config = config ?? Configuration;
            var analyzer = await LanguageServiceProvider.GetServiceAsync(
                config,
                FileContextProvider,
                cancellationToken
            );
            var context = await FileContextProvider.GetOrCreateContextAsync(
                "Tests",
                cancellationToken
            );

            await context.AddDocumentsAsync(new[] { document }, cancellationToken);
            await analyzer.AddFileContextAsync(context, cancellationToken);

            return await analyzer.GetAnalysisStateAsync(
                context,
                document.Moniker,
                false,
                cancellationToken
            );
        }

        private Task<AnalysisState> AnalyzeAsync(
            string code,
            CancellationToken cancellationToken,
            InterpreterConfiguration config = null,
            [CallerMemberName] string filename = null
        ) {
            return AnalyzeAsync(
                new StringLiteralDocument(code, "Tests\\" + filename + ".py"),
                cancellationToken,
                config
            );
        }
    }

    [TestClass]
    public class Python35AnalysisTests : AnalysisTests {
        public override InterpreterConfiguration Configuration => PythonPaths.Python35.Configuration;
    }
}
