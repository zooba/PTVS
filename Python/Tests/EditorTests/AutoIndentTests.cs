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

using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Editor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Text.Editor;
using TestUtilities.Mocks;

namespace EditorTests {
    [TestClass]
    public class AutoIndentTests {
        private CancellationToken Cancel5s {
            get {
                var cts = new CancellationTokenSource(5000);
                return cts.Token;
            }
        }

        private async Task Test(string code, int line, int expected, int tabSize = 4) {
            var tokenization = await Tokenization.TokenizeAsync(
                new StringLiteralDocument(code),
                PythonLanguageVersion.V35,
                Cancel5s
            );

            var options = new MockTextOptions();
            options.SetOptionValue(DefaultOptions.TabSizeOptionId, tabSize);

            var indent = await AutoIndent.CalculateIndentationAsync(
                tokenization,
                line,
                new MockTextOptions(),
                Cancel5s
            );

            Assert.AreEqual(expected, indent ?? -1, code);
        }

        [TestMethod, Priority(0)]
        public async Task AfterColon() {
            await Test("def f():\n\n", 1, 4);
            await Test("def f():\n\n", 2, 4);
            await Test("def f():\n    def f():\n\n", 1, 4);
            await Test("def f():\n    def f():\n\n", 2, 8);
            await Test("def f():\n    def f():\n\n", 3, 8);
            await Test("def f(): \n\n", 1, 4);
            await Test("def f(): \n\n", 2, 4);
            await Test("def f(): # Comment\n\n", 1, 4);
            await Test("def f(): # Comment\n\n", 2, 4);
            await Test("def f(\n):\n\n", 2, 4);
            await Test("def f(\n):\n\n", 3, 4);
        }

        [TestMethod, Priority(0)]
        public async Task NotAfterColon() {
            await Test("def f(): x\n\n", 1, 0);
            await Test("def f(): x\n\n", 2, 0);
        }

        [TestMethod, Priority(0)]
        public async Task DedentAfterKeywords() {
            foreach (var kw in new[] { "pass", "break", "continue", "return", "raise Error" }) {
                await Test(string.Format("def f():\n\n    {0}\n\n", kw), 1, 4);
                await Test(string.Format("def f():\n\n    {0}\n\n", kw), 3, 0);
            }
            foreach (var kw in new[] { "return", "raise Error" }) {
                await Test(string.Format("if x:\n    def f():\n\n        {0}(\n\n)\n\n", kw), 4, 12);
                await Test(string.Format("if x:\n    def f():\n\n        {0}(\n\n)\n\n", kw), 6, 4);
            }
        }

        [TestMethod, Priority(0)]
        public async Task AfterOpenGroup() {
            await Test("def f(\n\n", 1, 4);
            await Test("def f(\n\n", 2, 4);
            await Test("def f(a,\n\n", 1, 6);
            await Test("def f(a,\n\n", 2, 6);
        }

        [TestMethod, Priority(0)]
        public async Task NestedGroups() {
            await Test("print('%d' % (\n\n", 2, 4);
            await Test("print('%d' % ( a,\n\n", 2, 14);
        }

        [TestMethod, Priority(0)]
        public async Task ExplicitLineJoin() {
            await Test("x=\\\n\n", 2, 4);
            await Test("x=\\\n    \\\n\n", 3, 4);
        }

        [TestMethod, Priority(0)]
        public async Task WithinStringLiteral() {
            await Test("'''\n     ABC\n\n'''", 2, 5);
            await Test("'''\nABC\n\n'''", 2, 0);
            await Test("'''\n ABC:\n\n'''", 2, 1);
        }

        [TestMethod, Priority(0)]
        public async Task WithinDict() {
            await Test("x = { 'a': [1, 2, 3],\n\n'b':42}", 1, 5);
            await Test("x = { \n'a': [1, 2, 3],\n\n'b':42}", 2, 4);
        }

        [TestMethod, Priority(0)]
        public async Task NestedListWithComment() {
            await Test("x = {  #comment\n\n    'a': [\n\n],\n\n}", 1, 4);
            await Test("x = {  #comment\n\n    'a': [\n\n],\n\n}", 3, 8);
            await Test("x = {  #comment\n\n    'a': [\n\n],\n\n}", 5, 4);
        }
    }
}
