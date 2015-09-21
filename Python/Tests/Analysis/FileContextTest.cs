﻿extern alias analysis;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using analysis::Microsoft.PythonTools.Analysis.Analyzer;
using analysis::Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudioTools;
using TestUtilities;

namespace AnalysisTests {
    [TestClass]
    public class FileContextTest {
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
            var ct = CancellationToken.None;
            var pfcp = new PythonFileContextProvider();
            var lsp = new PythonLanguageServiceProvider();

            var config = GetPythonConfig();

            using (var service = await lsp.GetServiceAsync(config, pfcp, ct)) {
                await service.AddSearchPathAsync(Path.Combine(config.PrefixPath, "Lib"), null, ct);

                Assert.AreEqual(
                    Path.Combine(config.PrefixPath, "Lib", "os.py"),
                    await service.ResolveImportAsync("os", "", ct)
                );
                Assert.AreEqual(
                    Path.Combine(config.PrefixPath, "Lib", "ctypes", "util.py"),
                    await service.ResolveImportAsync("ctypes.util", "", ct)
                );
                Assert.AreEqual(
                    Path.Combine(config.PrefixPath, "Lib", "ctypes", "util.py"),
                    await service.ResolveImportAsync(".util", "ctypes.__init__", ct)
                );

                var sitePath = Path.Combine(config.PrefixPath, "Lib", "site-packages");
                var pipPath = Path.Combine(sitePath, "pip", "__init__.py");

                if (!File.Exists(pipPath)) {
                    Assert.Inconclusive("Could not use " + pipPath);
                }

                Assert.IsNull(await service.ResolveImportAsync("pip", "", ct));

                await service.AddSearchPathAsync(sitePath, null, ct);

                Assert.AreEqual(pipPath, await service.ResolveImportAsync("pip", "", ct));
            }
        }

        [TestMethod]
        public async Task GetImportableModules() {
            var ct = CancellationToken.None;
            var pfcp = new PythonFileContextProvider();
            var lsp = new PythonLanguageServiceProvider();

            var config = GetPythonConfig();

            using (var service = await lsp.GetServiceAsync(config, pfcp, ct)) {
                await service.AddSearchPathAsync(Path.Combine(config.PrefixPath, "Lib"), null, ct);

                var imports = await service.GetImportableModulesAsync("", "", ct);
                AssertUtil.ContainsAtLeast(imports.Keys, "os", "ctypes");

                var ast = await service.GetAstAsync(null, imports["os"], ct);
                Assert.IsNotNull(ast, "No AST for os module at " + imports["os"]);

                imports = await service.GetImportableModulesAsync("ctypes", "", ct);
                AssertUtil.ContainsAtLeast(imports.Keys, "wintypes", "util");
            }
        }

        [TestMethod]
        public async Task GetModuleMembers() {
            var ct = CancellationToken.None;
            var pfcp = new PythonFileContextProvider();
            var lsp = new PythonLanguageServiceProvider();

            var config = GetPythonConfig();

            using (var service = await lsp.GetServiceAsync(config, pfcp, ct)) {
                await service.AddSearchPathAsync(Path.Combine(config.PrefixPath, "Lib"), null, ct);

                var moniker = await service.ResolveImportAsync("os", "", ct);
                Trace.TraceInformation("Looking at {0}", moniker);
                var imports = await service.GetModuleMembersAsync(null, moniker, null, ct);
                AssertUtil.CheckCollection(
                    imports.Keys,
                    new[] { "walk", "environ", "_wrap_close", "_exit" },
                    new[] { "__init__", "__enter__" }
                );
                Assert.AreEqual(PythonMemberType.Function, imports["walk"]);

                moniker = await service.ResolveImportAsync("collections", "", ct);
                imports = await service.GetModuleMembersAsync(null, moniker, null, ct);
                Assert.AreEqual(PythonMemberType.Class, imports["Counter"]);

                imports = await service.GetModuleMembersAsync(null, moniker, "Counter", ct);
                AssertUtil.ContainsAtLeast(imports.Keys, "elements", "fromkeys");
                Assert.AreEqual(PythonMemberType.Function, imports["elements"]);
            }
        }

        [TestMethod]
        public async Task ParseFiles() {
            var ct = CancellationToken.None;
            var doc1 = new StringLiteralDocument(@"C:\Root\__init__.py", "x = 1");
            var doc2 = new StringLiteralDocument(@"C:\Root\module.py", "y = 2");
            var lsp = new PythonLanguageServiceProvider();

            using (var service = await lsp.GetServiceAsync(PythonPaths.Python35.Configuration, null, ct))
            using (var context = new PythonFileContext(@"C:\", "")) {
                await context.AddDocumentsAsync(new[] { doc1, doc2 }, ct);

                await service.AddFileContextAsync(context, ct);

                var tree1 = await service.GetAstAsync(context, doc1.Moniker, ct);
                var tree2 = await service.GetAstAsync(context, doc2.Moniker, ct);

                Assert.IsNotNull(tree1, "No AST for doc1");
                Assert.IsNotNull(tree2, "No AST for doc2");
                Assert.AreEqual(doc1.Document, tree1.ToCodeString(tree1));
                Assert.AreEqual(doc2.Document, tree2.ToCodeString(tree2));
            }
        }


    }
}
