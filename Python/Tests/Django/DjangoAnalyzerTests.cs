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
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Django.Analysis;
using Microsoft.PythonTools.Django.Project;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudioTools;
using TestUtilities;
using TestUtilities.Python;

namespace DjangoTests {
    [TestClass]
    public class DjangoAnalyzerTests {
        [ClassInitialize]
        public static void DoDeployment(TestContext context) {
            AssertListener.Initialize();
            PythonTestData.Deploy();
        }

        private void TestSingleRenderVariable(string template, string value="data") {
            var proj = AnalyzerTest(TestData.GetPath("TestData\\DjangoAnalysisTestApp"));

            var vars = proj.GetVariablesForTemplateFile(TestData.GetPath("TestData\\DjangoAnalysisTestApp\\test_render\\templates\\" + template));
            Assert.IsNotNull(vars, "No variables found for " + template);

            HashSet<AnalysisValue> values;
            Assert.IsTrue(vars.TryGetValue("content", out values), "content was missing");
            Assert.AreEqual(1, values.Count, "expected single value");
            Assert.AreEqual(value, values.Single().GetConstantValueAsString());
        }

        [TestMethod, Priority(1)]
        public void TestRender() {
            TestSingleRenderVariable("test_render.html");
        }

        [TestMethod, Priority(1)]
        public void TestRenderToResponse() {
            TestSingleRenderVariable("test_render_to_response.html");
        }

        [TestMethod, Priority(1)]
        public void TestRequestContext() {
            TestSingleRenderVariable("test_RequestContext.html");
            TestSingleRenderVariable("test_RequestContext2.html");
        }

        [TestMethod, Priority(1)]
        public void TestCustomFilter() {
            var proj = AnalyzerTest(TestData.GetPath("TestData\\DjangoAnalysisTestApp"));

            AssertUtil.ContainsExactly(
                proj._filters.Keys.Except(DjangoAnalyzer._knownFilters.Keys),
                "test_filter",
                "test_filter_2"
            );

            var entry = proj._filters["test_filter_2"].Entry;
            var parser = Parser.CreateParser(
                new StringReader(File.ReadAllText(entry.FilePath).Replace("test_filter_2", "test_filter_3")),
                PythonLanguageVersion.V27
            );
            entry.UpdateTree(parser.ParseFile(), null);
            entry.Analyze(CancellationToken.None, false);

            AssertUtil.ContainsExactly(
                proj._filters.Keys.Except(DjangoAnalyzer._knownFilters.Keys),
                "test_filter",
                "test_filter_3"
            );
        }

        [TestMethod, Priority(1)]
        public void TestCustomTag() {
            var proj = AnalyzerTest(TestData.GetPath("TestData\\DjangoAnalysisTestApp"));

            AssertUtil.ContainsExactly(
                proj._tags.Keys.Except(DjangoAnalyzer._knownTags.Keys),
                "test_tag",
                "test_tag_2",
                "test_assignment_tag",
                "test_simple_tag"
            );

            var entry = proj._tags["test_tag_2"].Entry;
            var parser = Parser.CreateParser(
                new StringReader(File.ReadAllText(entry.FilePath).Replace("test_tag_2", "test_tag_3")),
                PythonLanguageVersion.V27
            );
            entry.UpdateTree(parser.ParseFile(), null);
            entry.Analyze(CancellationToken.None, false);

            AssertUtil.ContainsExactly(
                proj._tags.Keys.Except(DjangoAnalyzer._knownTags.Keys),
                "test_tag",
                "test_tag_3",
                "test_assignment_tag",
                "test_simple_tag"
            );
        }

        [TestMethod, Priority(1)]
        public void TestListView() {
            var proj = AnalyzerTest(TestData.GetPath("TestData\\DjangoAnalysisTestApp"));
            var templates = TestData.GetPath("TestData\\DjangoAnalysisTestApp\\myapp\\templates\\myapp\\");

            var detailsVars = proj.GetVariablesForTemplateFile(templates + "index.html");
            Assert.IsNotNull(detailsVars, "No vars found for index.html");
            AssertUtil.ContainsExactly(detailsVars.Keys, "latest_poll_list");
        }

        [TestMethod, Priority(1)]
        public void TestDetailsView() {
            var proj = AnalyzerTest(TestData.GetPath("TestData\\DjangoAnalysisTestApp"));
            var templates = TestData.GetPath("TestData\\DjangoAnalysisTestApp\\myapp\\templates\\myapp\\");

            var detailsVars = proj.GetVariablesForTemplateFile(templates + "details.html");
            Assert.IsNotNull(detailsVars, "No vars found for details.html");
            AssertUtil.ContainsExactly(detailsVars.Keys, "mymodel");

            var mymodel2_detailsVars = proj.GetVariablesForTemplateFile(templates + "mymodel2_details.html");
            Assert.IsNotNull(detailsVars, "No vars found for mymodel2_details.html");
            AssertUtil.ContainsExactly(mymodel2_detailsVars.Keys, "mymodel2");
        }

        private DjangoAnalyzer AnalyzerTest(string path) {
            string djangoDbPath = TestData.GetPath("TestData\\DjangoDB");
            Assert.IsTrue(
                PythonTypeDatabase.IsDatabaseVersionCurrent(djangoDbPath),
                "TestData\\DjangoDB needs updating."
            );

            var testFact = InterpreterFactoryCreator.CreateAnalysisInterpreterFactory(
                new Version(2, 7),
                "Django Test Interpreter",
                TestData.GetPath("CompletionDB"),
                djangoDbPath
            );

            var serviceProvider = PythonToolsTestUtilities.CreateMockServiceProvider();
            PythonAnalyzer analyzer = PythonAnalyzer.CreateAsync(testFact).WaitAndUnwrapExceptions();
            DjangoAnalyzer djangoAnalyzer = new DjangoAnalyzer();
            djangoAnalyzer.Register(analyzer);

            analyzer.SetSearchPaths(new[] { path });

            List<IPythonProjectEntry> entries = new List<IPythonProjectEntry>();
            foreach (string file in Directory.EnumerateFiles(path, "*.py", SearchOption.AllDirectories)) {
                var entry = analyzer.AddModule(ModulePath.FromFullPath(file).ModuleName, file);
                var parser = Parser.CreateParser(
                    new FileStream(file, FileMode.Open, FileAccess.Read),
                    PythonLanguageVersion.V27
                );
                entry.UpdateTree(parser.ParseFile(), null);
                entries.Add(entry);
            }

            foreach (var entry in entries) {
                entry.Analyze(CancellationToken.None, false);
            }

            return djangoAnalyzer;
        }
    }
}
