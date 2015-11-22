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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Values;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudioTools;
using TestUtilities;

namespace AnalysisTests {
    [TestClass]
    public class FileContextTest {
        [ClassInitialize]
        public static void Initialize(TestContext context) {
            AssertListener.Initialize();
        }

        [TestCleanup]
        public void TestCleanup() {
            AssertListener.ThrowUnhandled();
        }

        private CancellationToken Cancel5s {
            get {
                if (Debugger.IsAttached) {
                    return CancellationToken.None;
                }
                var cts = new CancellationTokenSource(5000);
                return cts.Token;
            }
        }

        [TestMethod]
        public void PathSet() {
            var pt = new PathSet<object>(@"C:\a");

            pt.Add(@"C:\a\b\c\e", null);
            pt.Add(@"C:\a\B\d\d", null);
            pt.Add(@"C:\a\B\c\d", null);
            pt.Add(@"C:\a\b\d\a", null);

            Assert.AreEqual(
                @"C:\a\B\c\d;C:\a\b\c\e;C:\a\b\d\a;C:\a\B\d\d",
                string.Join(";", pt.GetPaths())
            );

            try {
                pt.Add(@"D:\a\b\c\e", null);
                Assert.Fail("Expected ArgumentException");
            } catch (ArgumentException) {
            }
        }

        private static void TestGetModuleFullName(string importName, string fromName, string expected) {
            var actual = string.Join(".", PythonLanguageService.GetModuleFullNameParts(importName, fromName));
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void GetModuleFullNameParts() {
            TestGetModuleFullName("mod1", "mod9.__init__", "mod1");
            TestGetModuleFullName("mod1.mod2", "mod9.__init__", "mod1.mod2");
            TestGetModuleFullName(".mod2", "mod9.__init__", "mod9.mod2");
            TestGetModuleFullName("..mod2", "mod9.__init__", "mod2");
            TestGetModuleFullName("...mod2", "mod9.__init__", "mod2");
        }

        private static InterpreterConfiguration GetPythonConfig() {
            var python = PythonPaths.Python35 ?? PythonPaths.Python35_x64 ??
                PythonPaths.Python34 ?? PythonPaths.Python34_x64 ??
                PythonPaths.Python27 ?? PythonPaths.Python27_x64;
            python.AssertInstalled();
            Trace.TraceInformation("Using " + python.PrefixPath);
            return python.Configuration;
        }

        [TestMethod]
        public async Task ResolveImports() {
            var pfcp = new PythonFileContextProvider();
            var lsp = new PythonLanguageServiceProvider();

            var config = GetPythonConfig();

            using (var service = await lsp.GetServiceAsync(config, pfcp, Cancel5s)) {
                await service.AddSearchPathAsync(Path.Combine(config.PrefixPath, "Lib"), null, Cancel5s);

                Assert.AreEqual(
                    Path.Combine(config.PrefixPath, "Lib", "os.py"),
                    await service.ResolveImportAsync("os", "", Cancel5s)
                );
                Assert.AreEqual(
                    Path.Combine(config.PrefixPath, "Lib", "ctypes", "util.py"),
                    await service.ResolveImportAsync("ctypes.util", "", Cancel5s)
                );
                Assert.AreEqual(
                    Path.Combine(config.PrefixPath, "Lib", "ctypes", "util.py"),
                    await service.ResolveImportAsync(".util", "ctypes.__init__", Cancel5s)
                );

                var sitePath = Path.Combine(config.PrefixPath, "Lib", "site-packages");
                var pipPath = Path.Combine(sitePath, "pip", "__init__.py");

                if (!File.Exists(pipPath)) {
                    Assert.Inconclusive("Could not use " + pipPath);
                }

                Assert.IsNull(await service.ResolveImportAsync("pip", "", Cancel5s));

                await service.AddSearchPathAsync(sitePath, null, Cancel5s);

                Assert.AreEqual(pipPath, await service.ResolveImportAsync("pip", "", Cancel5s));
            }
        }

        [TestMethod]
        public async Task GetImportableModules() {
            var pfcp = new PythonFileContextProvider();
            var lsp = new PythonLanguageServiceProvider();

            var config = GetPythonConfig();

            using (var service = await lsp.GetServiceAsync(config, pfcp, Cancel5s)) {
                await service.AddSearchPathAsync(Path.Combine(config.PrefixPath, "Lib"), null, Cancel5s);

                var imports = await service.GetImportableModulesAsync("", "", Cancel5s);
                AssertUtil.ContainsAtLeast(imports.Keys, "os", "ctypes");

                var ast = await service.GetAstAsync(null, imports["os"], Cancel5s);
                Assert.IsNotNull(ast, "No AST for os module at " + imports["os"]);

                imports = await service.GetImportableModulesAsync("ctypes", "", Cancel5s);
                AssertUtil.ContainsAtLeast(imports.Keys, "wintypes", "util");
            }
        }

        [TestMethod]
        public async Task GetModuleMembers() {
            var pfcp = new PythonFileContextProvider();
            var lsp = new PythonLanguageServiceProvider();

            var config = GetPythonConfig();

            using (var service = await lsp.GetServiceAsync(config, pfcp, Cancel5s)) {
                await service.AddSearchPathAsync(Path.Combine(config.PrefixPath, "Lib"), null, Cancel5s);

                var moniker = await service.ResolveImportAsync("os", "", Cancel5s);
                Trace.TraceInformation("Looking at {0}", moniker);
                var imports = await service.GetModuleMembersAsync(null, moniker, null, Cancel5s);
                AssertUtil.CheckCollection(
                    imports,
                    new[] { "walk", "environ", "_wrap_close", "_exit" },
                    new[] { "__init__", "__enter__" }
                );
                var walk = await service.GetModuleMemberTypesAsync(null, moniker, "walk", Cancel5s);
                Assert.IsInstanceOfType(walk.Single(), typeof(FunctionInfo));

                moniker = await service.ResolveImportAsync("collections", "", Cancel5s);
                imports = await service.GetModuleMembersAsync(null, moniker, null, Cancel5s);
                var Counter = await service.GetModuleMemberTypesAsync(null, moniker, "Counter", Cancel5s);
                Assert.IsInstanceOfType(Counter.Single(), typeof(ClassInfo));

                // TODO: Reimplement class members
                //imports = await service.GetModuleMembersAsync(null, moniker, "Counter", Cancel5s);
                //AssertUtil.ContainsAtLeast(imports, "elements", "fromkeys");
                //Assert.IsInstanceOfType(imports["elements"], typeof(FunctionInfo));
            }
        }

        [TestMethod]
        public async Task ParseFiles() {
            var doc1 = new StringLiteralDocument("x = 1", @"C:\Root\__init__.py");
            var doc2 = new StringLiteralDocument("y = 2", @"C:\Root\module.py");
            var lsp = new PythonLanguageServiceProvider();

            using (var service = await lsp.GetServiceAsync(PythonPaths.Python35.Configuration, null, Cancel5s))
            using (var context = new PythonFileContext(@"C:\", "")) {
                await context.AddDocumentsAsync(new[] { doc1, doc2 }, Cancel5s);

                await service.AddFileContextAsync(context, Cancel5s);

                var tree1 = await service.GetAstAsync(context, doc1.Moniker, Cancel5s);
                var tree2 = await service.GetAstAsync(context, doc2.Moniker, Cancel5s);

                Assert.IsNotNull(tree1, "No AST for doc1");
                Assert.IsNotNull(tree2, "No AST for doc2");
                Assert.AreEqual(doc1.Document, tree1.ToCodeString(tree1));
                Assert.AreEqual(doc2.Document, tree2.ToCodeString(tree2));
            }
        }


    }
}
