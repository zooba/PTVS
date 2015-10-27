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
using System.IO;
using System.Linq;
using Microsoft.PythonTools;
using Microsoft.PythonTools.Debugger;
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Interpreter.Default;
using Microsoft.PythonTools.Parsing;
using Microsoft.PythonTools.Parsing.Ast;
using Microsoft.PythonTools.Refactoring;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.Text;
using TestUtilities;
using TestUtilities.Mocks;
using TestUtilities.Python;

namespace PythonToolsTests {
    [TestClass]
    public class ProximityExpressionWalkerTests {
        [ClassInitialize]
        public static void DoDeployment(TestContext context) {
            AssertListener.Initialize();
            PythonTestData.Deploy();
        }

        [TestMethod, Priority(0)]
        public void AutosRangeCheck() {
            string code = @"
a; b.\
c.d[e]
abs(f.\
g); h
len(i)";
            ProximityTest(code, 3, 4, "b.c.d[e]", "e", "abs(f.g)", "f.g");
        }

        [TestMethod, Priority(0)]
        public void AutosNames() {
            ProximityTest("a", "a");
        }

        [TestMethod, Priority(0)]
        public void AutosMembers() {
            string code = @"
a.b.c
d
d.e[0].f
(g + 1).e
abs(f.g).e
h(i.j).k
";
            ProximityTest(code, "a.b.c", "d", "d.e[0].f", "g", " (g + 1).e", "abs(f.g).e", "f.g", "i.j");
        }

        [TestMethod, Priority(0)]
        public void AutosIndexing() {
            string code = @"
a[b.c[d.e], f:g].h[abs(i[j])].k[l(m[n])].o[p]
abs(q[r])[s]
";
            ProximityTest(code,
                "a[(b.c[d.e],f :g)].h[abs(i[j])].k",
                "b.c[d.e]", "d.e", "f", "g", "abs(i[j])", "i[j]", "j",
                "m[n]", "n", "p", "abs(q[r])[s]", "q[r]", "r", "s");
        }

        [TestMethod, Priority(0)]
        public void AutosCalls() {
            string code = @"
abs(a, len(b))
c(d)
e.f.g(h)
";
            ProximityTest(code, "abs(a,len(b))", "a", "len(b)", "b", "d", "e.f", "h");
        }

        [TestMethod, Priority(0)]
        public void AutosNoYield() {
            string code = @"
a.b
(yield c).d
(yield from e).f
";
            ProximityTest(code, "a.b", "c", "e");
        }

        [TestMethod, Priority(0)]
        public void AutosNoBackQuotes() {
            string code = @"
a.b
`c`.d
";
            ProximityTest(code, "a.b");
        }

        private void ProximityTest(string code, params string[] exprs) {
            ProximityTest(PythonLanguageVersion.V34, code, 0, int.MaxValue, exprs);
        }

        private void ProximityTest(string code, int startLine, int endLine, params string[] exprs) {
            ProximityTest(PythonLanguageVersion.V34, code, startLine, endLine, exprs);
        }

        private void ProximityTest(PythonLanguageVersion ver, string code, int startLine, int endLine, params string[] exprs) {
            var parser = Parser.CreateParser(new StringReader(code), ver);
            var ast = parser.ParseFile();
            var walker = new ProximityExpressionWalker(ast, startLine, endLine);
            ast.Walk(walker);
            AssertUtil.ContainsExactly(walker.GetExpressions(), exprs.OrderBy(s => s));
        }
    }
}
