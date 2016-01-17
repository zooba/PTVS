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
using System.Linq;
using Microsoft.PythonTools;
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
    public class CodeFormatterTests {
        [ClassInitialize]
        public static void DoDeployment(TestContext context) {
            AssertListener.Initialize();
            PythonTestData.Deploy();
        }

        [TestMethod, Priority(1)]
        public void TestCodeFormattingSelection() {
            var input = @"print('Hello World')

class SimpleTest(object):
    def test_simple_addition(self):
        pass

    def test_complex(self):
        pass

class Oar(object):
    def say_hello(self):
        method_end";

            string selection = "def say_hello .. method_end";

            string expected = @"print('Hello World')

class SimpleTest(object):
    def test_simple_addition(self):
        pass

    def test_complex(self):
        pass

class Oar(object):
    def say_hello( self ):
        method_end";

            var options = new CodeFormattingOptions() { 
                SpaceBeforeClassDeclarationParen = true, 
                SpaceWithinFunctionDeclarationParens = true 
            };

            CodeFormattingTest(input, selection, expected, "    def say_hello .. method_end", options);
        }

        [TestMethod, Priority(1)]
        public void TestCodeFormattingEndOfFile() {
            var input = @"print('Hello World')

class SimpleTest(object):
    def test_simple_addition(self):
        pass

    def test_complex(self):
        pass

class Oar(object):
    def say_hello(self):
        method_end
";

            var options = new CodeFormattingOptions() {
                SpaceBeforeClassDeclarationParen = true,
                SpaceWithinFunctionDeclarationParens = true
            };

            CodeFormattingTest(input, new Span(input.Length, 0), input, null, options);
        }

        [TestMethod, Priority(1)]
        public void TestCodeFormattingInMethodExpression() {
            var input = @"print('Hello World')

class SimpleTest(object):
    def test_simple_addition(self):
        pass

    def test_complex(self):
        pass

class Oar(object):
    def say_hello(self):
        method_end
";

            var options = new CodeFormattingOptions() {
                SpaceBeforeClassDeclarationParen = true,
                SpaceWithinFunctionDeclarationParens = true
            };

            CodeFormattingTest(input, "method_end", input, null, options);
        }

        [TestMethod, Priority(1)]
        public void TestCodeFormattingStartOfMethodSelection() {
            var input = @"print('Hello World')

class SimpleTest(object):
    def test_simple_addition(self):
        pass

    def test_complex(self):
        pass

class Oar(object):
    def say_hello(self):
        method_end";

            string selection = "def say_hello";

            string expected = @"print('Hello World')

class SimpleTest(object):
    def test_simple_addition(self):
        pass

    def test_complex(self):
        pass

class Oar(object):
    def say_hello( self ):
        method_end";

            var options = new CodeFormattingOptions() {
                SpaceBeforeClassDeclarationParen = true,
                SpaceWithinFunctionDeclarationParens = true
            };

            CodeFormattingTest(input, selection, expected, "    def say_hello .. method_end", options);
        }

        [TestMethod, Priority(1)]
        public void FormatDocument() {
            var input = @"fob('Hello World')";
            var expected = @"fob( 'Hello World' )";
            var options = new CodeFormattingOptions() { SpaceWithinCallParens = true };

            CodeFormattingTest(input, new Span(0, input.Length), expected, null, options, false);
        }

        private static void CodeFormattingTest(string input, object selection, string expected, object expectedSelection, CodeFormattingOptions options, bool selectResult = true) {
            var fact = InterpreterFactoryCreator.CreateAnalysisInterpreterFactory(new Version(2, 7));
            var serviceProvider = PythonToolsTestUtilities.CreateMockServiceProvider();
            using (var analyzer = new VsProjectAnalyzer(serviceProvider, fact, new[] { fact })) {
                var buffer = new MockTextBuffer(input, PythonCoreConstants.ContentType, "C:\\fob.py");
                buffer.AddProperty(typeof(VsProjectAnalyzer), analyzer);
                var view = new MockTextView(buffer);
                var selectionSpan = new SnapshotSpan(
                    buffer.CurrentSnapshot,
                    ExtractMethodTests.GetSelectionSpan(input, selection)
                );
                view.Selection.Select(selectionSpan, false);

                new CodeFormatter(serviceProvider, view, options).FormatCode(
                    selectionSpan,
                    selectResult
                );

                Assert.AreEqual(expected, view.TextBuffer.CurrentSnapshot.GetText());
                if (expectedSelection != null) {
                    Assert.AreEqual(
                        ExtractMethodTests.GetSelectionSpan(expected, expectedSelection),
                        view.Selection.StreamSelectionSpan.SnapshotSpan.Span
                    );
                }
            }
        }
    }
}
