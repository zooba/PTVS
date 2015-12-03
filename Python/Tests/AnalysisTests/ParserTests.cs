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
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.PythonTools.Common.Infrastructure;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestUtilities;
using TestUtilities.Python;

namespace AnalysisTests {
    /// <summary>
    /// Test cases for parser written in a continuation passing style.
    /// </summary>
    [TestClass]
    public class ParserTests {
        [ClassInitialize]
        public static void DoDeployment(TestContext context) {
            AssertListener.Initialize();
            // Don't deploy test data because we read directly from the source.
            PythonTestData.Deploy(includeTestData: false);
        }

        internal static readonly PythonLanguageVersion[] AllVersions = new[] { PythonLanguageVersion.V25, PythonLanguageVersion.V26, PythonLanguageVersion.V27, PythonLanguageVersion.V30, PythonLanguageVersion.V31, PythonLanguageVersion.V32, PythonLanguageVersion.V33, PythonLanguageVersion.V34, PythonLanguageVersion.V35 };
        internal static readonly PythonLanguageVersion[] V25AndUp = AllVersions.Where(v => v >= PythonLanguageVersion.V25).ToArray();
        internal static readonly PythonLanguageVersion[] V26AndUp = AllVersions.Where(v => v >= PythonLanguageVersion.V26).ToArray();
        internal static readonly PythonLanguageVersion[] V27AndUp = AllVersions.Where(v => v >= PythonLanguageVersion.V27).ToArray();
        internal static readonly PythonLanguageVersion[] V2Versions = AllVersions.Where(v => v <= PythonLanguageVersion.V27).ToArray();
        internal static readonly PythonLanguageVersion[] V25Versions = AllVersions.Where(v => v <= PythonLanguageVersion.V25).ToArray();
        internal static readonly PythonLanguageVersion[] V25_V26Versions = AllVersions.Where(v => v <= PythonLanguageVersion.V26).ToArray();
        internal static readonly PythonLanguageVersion[] V25_V27Versions = AllVersions.Where(v => v >= PythonLanguageVersion.V25 && v <= PythonLanguageVersion.V27).ToArray();
        internal static readonly PythonLanguageVersion[] V26_V27Versions = AllVersions.Where(v => v >= PythonLanguageVersion.V26 && v <= PythonLanguageVersion.V27).ToArray();
        internal static readonly PythonLanguageVersion[] V30_V32Versions = AllVersions.Where(v => v >= PythonLanguageVersion.V30 && v <= PythonLanguageVersion.V32).ToArray();
        internal static readonly PythonLanguageVersion[] V3Versions = AllVersions.Where(v => v >= PythonLanguageVersion.V30).ToArray();
        internal static readonly PythonLanguageVersion[] V33AndUp = AllVersions.Where(v => v >= PythonLanguageVersion.V33).ToArray();
        internal static readonly PythonLanguageVersion[] V35AndUp = AllVersions.Where(v => v >= PythonLanguageVersion.V35).ToArray();

        #region Test Cases

        [TestMethod, Priority(0)]
        public void MixedWhiteSpace() {
            // mixed, but in different blocks, which is ok
            ParseErrors("MixedWhitespace1.py", PythonLanguageVersion.V27, Severity.Error);

            // mixed in the same block, tabs first
            ParseErrors("MixedWhitespace2.py", PythonLanguageVersion.V27, Severity.Error,
                new ErrorInfo("inconsistent whitespace", 294, 14, 1, 302, 14, 9)
            );

            // mixed in same block, spaces first
            ParseErrors("MixedWhitespace3.py", PythonLanguageVersion.V27, Severity.Error,
                new ErrorInfo("inconsistent whitespace", 286, 14, 1, 287, 14, 2)
            );

            // mixed on same line, spaces first
            ParseErrors("MixedWhitespace4.py", PythonLanguageVersion.V27, Severity.Error);

            // mixed on same line, tabs first
            ParseErrors("MixedWhitespace5.py", PythonLanguageVersion.V27, Severity.Error);

            // mixed on a comment line - should not crash
            ParseErrors("MixedWhitespace6.py", PythonLanguageVersion.V27, Severity.Error,
                new ErrorInfo("inconsistent whitespace", 127, 9, 1, 128, 9, 2)
            );
        }

        [TestMethod, Priority(1)]
        public void Errors() {
            foreach (var version in V30_V32Versions) {
                ParseErrors("Errors3x.py",
                    version,
                    new ErrorInfo("no binding for nonlocal '__class__' found", 23, 2, 14, 32, 2, 23)
                );
            }

            ParseErrors("AllErrors.py",
                PythonLanguageVersion.V25,
                new ErrorInfo("future statement does not support import *", 0, 1, 1, 24, 1, 25),
                new ErrorInfo("future feature is not defined: *", 0, 1, 1, 24, 1, 25),
                new ErrorInfo("not a chance", 26, 2, 1, 55, 2, 30),
                new ErrorInfo("future feature is not defined: unknown", 57, 3, 1, 87, 3, 31),
                new ErrorInfo("default value must be specified here", 106, 5, 16, 107, 5, 17),
                new ErrorInfo("non-keyword arg after keyword arg", 134, 8, 12, 135, 8, 13),
                new ErrorInfo("only one * allowed", 147, 9, 10, 149, 9, 12),
                new ErrorInfo("only one ** allowed", 162, 10, 11, 165, 10, 14),
                new ErrorInfo("keywords must come before ** args", 180, 11, 13, 186, 11, 19),
                new ErrorInfo("unexpected token 'pass'", 197, 14, 1, 201, 14, 5),
                new ErrorInfo("unexpected token '42'", 217, 17, 11, 219, 17, 13),
                new ErrorInfo("unexpected token '42'", 251, 20, 10, 253, 20, 12),
                new ErrorInfo("'break' outside loop", 278, 25, 1, 283, 25, 6),
                new ErrorInfo("'continue' not properly in loop", 285, 26, 1, 293, 26, 9),
                new ErrorInfo("print statement expected expression to be printed", 297, 28, 1, 311, 28, 15),
                new ErrorInfo("'continue' not supported inside 'finally' clause", 374, 34, 9, 382, 34, 17),
                new ErrorInfo("expected expression after del", 386, 36, 1, 389, 36, 4),
                new ErrorInfo("can't delete binary operator", 396, 37, 5, 399, 37, 8),
                new ErrorInfo("can't delete unary operator", 405, 38, 5, 407, 38, 7),
                new ErrorInfo("can't delete or expression", 413, 39, 5, 421, 39, 13),
                new ErrorInfo("can't delete and expression", 427, 40, 5, 436, 40, 14),
                new ErrorInfo("can't delete dictionary display", 442, 41, 5, 444, 41, 7),
                new ErrorInfo("can't delete literal", 450, 42, 5, 454, 42, 9),
                new ErrorInfo("can't delete literal", 460, 43, 5, 464, 43, 9),
                new ErrorInfo("can't assign to literal", 468, 45, 1, 472, 45, 5),
                new ErrorInfo("can't assign to literal", 482, 46, 1, 486, 46, 5),
                new ErrorInfo("'return' outside function", 498, 48, 1, 504, 48, 7),
                new ErrorInfo("'return' with argument inside generator", 539, 53, 5, 548, 53, 14),
                new ErrorInfo("misplaced yield", 552, 55, 1, 557, 55, 6),
                new ErrorInfo("'return' with argument inside generator", 581, 59, 5, 590, 59, 14),
                new ErrorInfo("'return' with argument inside generator", 596, 60, 5, 606, 60, 15),
                new ErrorInfo("invalid syntax", 657, 68, 5, 658, 68, 6),
                new ErrorInfo("invalid syntax", 661, 68, 9, 662, 68, 10),
                new ErrorInfo("illegal expression for augmented assignment", 674, 70, 1, 676, 70, 3),
                new ErrorInfo("missing module name", 692, 72, 6, 698, 72, 12),
                new ErrorInfo("from __future__ imports must occur at the beginning of the file", 749, 78, 1, 780, 78, 32),
                new ErrorInfo("unexpected token 'blazzz'", 797, 82, 10, 803, 82, 16),
                new ErrorInfo("invalid syntax, from cause not allowed in 2.x.", 837, 87, 11, 845, 87, 19),
                new ErrorInfo("invalid syntax, class decorators require 2.6 or later.", 861, 93, 1, 866, 93, 6),
                new ErrorInfo("invalid syntax, parameter annotations require 3.x", 890, 96, 8, 894, 96, 12),
                new ErrorInfo("default value must be specified here", 924, 99, 15, 925, 99, 16),
                new ErrorInfo("positional parameter after * args not allowed", 953, 102, 13, 959, 102, 19),
                new ErrorInfo("duplicate * args arguments", 987, 105, 13, 988, 105, 14),
                new ErrorInfo("duplicate * args arguments", 1017, 108, 13, 1018, 108, 14),
                new ErrorInfo("unexpected token ','", 1045, 111, 11, 1046, 111, 12),
                new ErrorInfo("unexpected token '42'", 1107, 117, 11, 1109, 117, 13),
                new ErrorInfo("duplicate argument 'abc' in function definition", 1143, 120, 12, 1146, 120, 15),
                new ErrorInfo("duplicate argument 'abc' in function definition", 1177, 123, 16, 1180, 123, 19),
                new ErrorInfo("unexpected token '42'", 1208, 127, 7, 1210, 127, 9),
                new ErrorInfo("default 'except' must be last", 1242, 132, 1, 1248, 132, 7),
                new ErrorInfo("'as' requires Python 2.6 or later", 1328, 139, 18, 1330, 139, 20),
                new ErrorInfo("invalid syntax", 1398, 147, 2, 1403, 147, 7),
                new ErrorInfo("invalid syntax", 1404, 147, 8, 1409, 147, 13),
                new ErrorInfo("unexpected token 'b'", 1417, 148, 7, 1418, 148, 8),
                new ErrorInfo("invalid syntax", 1431, 149, 7, 1433, 149, 9),
                new ErrorInfo("invalid syntax", 1431, 149, 7, 1433, 149, 9),
                new ErrorInfo("invalid syntax", 1436, 150, 2, 1441, 150, 7),
                new ErrorInfo("invalid syntax", 1442, 150, 8, 1444, 150, 10),
                new ErrorInfo("invalid syntax", 1451, 152, 4, 1453, 152, 6),
                new ErrorInfo("expected name", 1459, 154, 3, 1461, 154, 5),
                new ErrorInfo("unexpected token '42'", 1476, 156, 7, 1478, 156, 9),
                new ErrorInfo("invalid syntax, set literals require Python 2.7 or later.", 1511, 160, 12, 1512, 160, 13),
                new ErrorInfo("invalid syntax, set literals require Python 2.7 or later.", 1521, 161, 7, 1522, 161, 8)
            );

            ParseErrors("AllErrors.py",
                PythonLanguageVersion.V26,
                new ErrorInfo("future statement does not support import *", 0, 1, 1, 24, 1, 25),
                new ErrorInfo("future feature is not defined: *", 0, 1, 1, 24, 1, 25),
                new ErrorInfo("not a chance", 26, 2, 1, 55, 2, 30),
                new ErrorInfo("future feature is not defined: unknown", 57, 3, 1, 87, 3, 31),
                new ErrorInfo("default value must be specified here", 106, 5, 16, 107, 5, 17),
                new ErrorInfo("non-keyword arg after keyword arg", 134, 8, 12, 135, 8, 13),
                new ErrorInfo("only one * allowed", 147, 9, 10, 149, 9, 12),
                new ErrorInfo("only one ** allowed", 162, 10, 11, 165, 10, 14),
                new ErrorInfo("keywords must come before ** args", 180, 11, 13, 186, 11, 19),
                new ErrorInfo("unexpected token 'pass'", 197, 14, 1, 201, 14, 5),
                new ErrorInfo("unexpected token '42'", 217, 17, 11, 219, 17, 13),
                new ErrorInfo("unexpected token '42'", 251, 20, 10, 253, 20, 12),
                new ErrorInfo("'break' outside loop", 278, 25, 1, 283, 25, 6),
                new ErrorInfo("'continue' not properly in loop", 285, 26, 1, 293, 26, 9),
                new ErrorInfo("print statement expected expression to be printed", 297, 28, 1, 311, 28, 15),
                new ErrorInfo("'continue' not supported inside 'finally' clause", 374, 34, 9, 382, 34, 17),
                new ErrorInfo("expected expression after del", 386, 36, 1, 389, 36, 4),
                new ErrorInfo("can't delete binary operator", 396, 37, 5, 399, 37, 8),
                new ErrorInfo("can't delete unary operator", 405, 38, 5, 407, 38, 7),
                new ErrorInfo("can't delete or expression", 413, 39, 5, 421, 39, 13),
                new ErrorInfo("can't delete and expression", 427, 40, 5, 436, 40, 14),
                new ErrorInfo("can't delete dictionary display", 442, 41, 5, 444, 41, 7),
                new ErrorInfo("can't delete literal", 450, 42, 5, 454, 42, 9),
                new ErrorInfo("can't delete literal", 460, 43, 5, 464, 43, 9),
                new ErrorInfo("can't assign to literal", 468, 45, 1, 472, 45, 5),
                new ErrorInfo("can't assign to literal", 482, 46, 1, 486, 46, 5),
                new ErrorInfo("'return' outside function", 498, 48, 1, 504, 48, 7),
                new ErrorInfo("'return' with argument inside generator", 539, 53, 5, 548, 53, 14),
                new ErrorInfo("misplaced yield", 552, 55, 1, 557, 55, 6),
                new ErrorInfo("'return' with argument inside generator", 581, 59, 5, 590, 59, 14),
                new ErrorInfo("'return' with argument inside generator", 596, 60, 5, 606, 60, 15),
                new ErrorInfo("invalid syntax", 657, 68, 5, 658, 68, 6),
                new ErrorInfo("invalid syntax", 661, 68, 9, 662, 68, 10),
                new ErrorInfo("illegal expression for augmented assignment", 674, 70, 1, 676, 70, 3),
                new ErrorInfo("missing module name", 692, 72, 6, 698, 72, 12),
                new ErrorInfo("from __future__ imports must occur at the beginning of the file", 749, 78, 1, 780, 78, 32),
                new ErrorInfo("unexpected token 'blazzz'", 797, 82, 10, 803, 82, 16),
                new ErrorInfo("invalid syntax, from cause not allowed in 2.x.", 837, 87, 11, 845, 87, 19),
                new ErrorInfo("invalid syntax, parameter annotations require 3.x", 890, 96, 8, 894, 96, 12),
                new ErrorInfo("default value must be specified here", 924, 99, 15, 925, 99, 16),
                new ErrorInfo("positional parameter after * args not allowed", 953, 102, 13, 959, 102, 19),
                new ErrorInfo("duplicate * args arguments", 987, 105, 13, 988, 105, 14),
                new ErrorInfo("duplicate * args arguments", 1017, 108, 13, 1018, 108, 14),
                new ErrorInfo("unexpected token ','", 1045, 111, 11, 1046, 111, 12),
                new ErrorInfo("unexpected token '42'", 1107, 117, 11, 1109, 117, 13),
                new ErrorInfo("duplicate argument 'abc' in function definition", 1143, 120, 12, 1146, 120, 15),
                new ErrorInfo("duplicate argument 'abc' in function definition", 1177, 123, 16, 1180, 123, 19),
                new ErrorInfo("unexpected token '42'", 1208, 127, 7, 1210, 127, 9),
                new ErrorInfo("default 'except' must be last", 1242, 132, 1, 1248, 132, 7),
                new ErrorInfo("invalid syntax", 1431, 149, 7, 1433, 149, 9),
                new ErrorInfo("invalid syntax", 1431, 149, 7, 1433, 149, 9),
                new ErrorInfo("invalid syntax", 1442, 150, 8, 1444, 150, 10),
                new ErrorInfo("invalid syntax", 1442, 150, 8, 1444, 150, 10),
                new ErrorInfo("invalid syntax", 1451, 152, 4, 1453, 152, 6),
                new ErrorInfo("expected name", 1459, 154, 3, 1461, 154, 5),
                new ErrorInfo("unexpected token '42'", 1476, 156, 7, 1478, 156, 9),
                new ErrorInfo("invalid syntax, set literals require Python 2.7 or later.", 1511, 160, 12, 1512, 160, 13),
                new ErrorInfo("invalid syntax, set literals require Python 2.7 or later.", 1521, 161, 7, 1522, 161, 8)
            );

            ParseErrors("AllErrors.py",
                PythonLanguageVersion.V27,
                new ErrorInfo("future statement does not support import *", 0, 1, 1, 24, 1, 25),
                new ErrorInfo("future feature is not defined: *", 0, 1, 1, 24, 1, 25),
                new ErrorInfo("not a chance", 26, 2, 1, 55, 2, 30),
                new ErrorInfo("future feature is not defined: unknown", 57, 3, 1, 87, 3, 31),
                new ErrorInfo("default value must be specified here", 106, 5, 16, 107, 5, 17),
                new ErrorInfo("non-keyword arg after keyword arg", 134, 8, 12, 135, 8, 13),
                new ErrorInfo("only one * allowed", 147, 9, 10, 149, 9, 12),
                new ErrorInfo("only one ** allowed", 162, 10, 11, 165, 10, 14),
                new ErrorInfo("keywords must come before ** args", 180, 11, 13, 186, 11, 19),
                new ErrorInfo("unexpected token 'pass'", 197, 14, 1, 201, 14, 5),
                new ErrorInfo("unexpected token '42'", 217, 17, 11, 219, 17, 13),
                new ErrorInfo("unexpected token '42'", 251, 20, 10, 253, 20, 12),
                new ErrorInfo("'break' outside loop", 278, 25, 1, 283, 25, 6),
                new ErrorInfo("'continue' not properly in loop", 285, 26, 1, 293, 26, 9),
                new ErrorInfo("print statement expected expression to be printed", 297, 28, 1, 311, 28, 15),
                new ErrorInfo("'continue' not supported inside 'finally' clause", 374, 34, 9, 382, 34, 17),
                new ErrorInfo("expected expression after del", 386, 36, 1, 389, 36, 4),
                new ErrorInfo("can't delete binary operator", 396, 37, 5, 399, 37, 8),
                new ErrorInfo("can't delete unary operator", 405, 38, 5, 407, 38, 7),
                new ErrorInfo("can't delete or expression", 413, 39, 5, 421, 39, 13),
                new ErrorInfo("can't delete and expression", 427, 40, 5, 436, 40, 14),
                new ErrorInfo("can't delete dictionary display", 442, 41, 5, 444, 41, 7),
                new ErrorInfo("can't delete literal", 450, 42, 5, 454, 42, 9),
                new ErrorInfo("can't delete literal", 460, 43, 5, 464, 43, 9),
                new ErrorInfo("can't assign to literal", 468, 45, 1, 472, 45, 5),
                new ErrorInfo("can't assign to literal", 482, 46, 1, 486, 46, 5),
                new ErrorInfo("'return' outside function", 498, 48, 1, 504, 48, 7),
                new ErrorInfo("'return' with argument inside generator", 539, 53, 5, 548, 53, 14),
                new ErrorInfo("misplaced yield", 552, 55, 1, 557, 55, 6),
                new ErrorInfo("'return' with argument inside generator", 581, 59, 5, 590, 59, 14),
                new ErrorInfo("'return' with argument inside generator", 596, 60, 5, 606, 60, 15),
                new ErrorInfo("invalid syntax", 657, 68, 5, 658, 68, 6),
                new ErrorInfo("invalid syntax", 661, 68, 9, 662, 68, 10),
                new ErrorInfo("illegal expression for augmented assignment", 674, 70, 1, 676, 70, 3),
                new ErrorInfo("missing module name", 692, 72, 6, 698, 72, 12),
                new ErrorInfo("from __future__ imports must occur at the beginning of the file", 749, 78, 1, 780, 78, 32),
                new ErrorInfo("unexpected token 'blazzz'", 797, 82, 10, 803, 82, 16),
                new ErrorInfo("invalid syntax, from cause not allowed in 2.x.", 837, 87, 11, 845, 87, 19),
                new ErrorInfo("invalid syntax, parameter annotations require 3.x", 890, 96, 8, 894, 96, 12),
                new ErrorInfo("default value must be specified here", 924, 99, 15, 925, 99, 16),
                new ErrorInfo("positional parameter after * args not allowed", 953, 102, 13, 959, 102, 19),
                new ErrorInfo("duplicate * args arguments", 987, 105, 13, 988, 105, 14),
                new ErrorInfo("duplicate * args arguments", 1017, 108, 13, 1018, 108, 14),
                new ErrorInfo("unexpected token ','", 1045, 111, 11, 1046, 111, 12),
                new ErrorInfo("unexpected token '42'", 1107, 117, 11, 1109, 117, 13),
                new ErrorInfo("duplicate argument 'abc' in function definition", 1143, 120, 12, 1146, 120, 15),
                new ErrorInfo("duplicate argument 'abc' in function definition", 1177, 123, 16, 1180, 123, 19),
                new ErrorInfo("unexpected token '42'", 1208, 127, 7, 1210, 127, 9),
                new ErrorInfo("default 'except' must be last", 1242, 132, 1, 1248, 132, 7),
                new ErrorInfo("invalid syntax", 1431, 149, 7, 1433, 149, 9),
                new ErrorInfo("invalid syntax", 1431, 149, 7, 1433, 149, 9),
                new ErrorInfo("invalid syntax", 1442, 150, 8, 1444, 150, 10),
                new ErrorInfo("invalid syntax", 1442, 150, 8, 1444, 150, 10),
                new ErrorInfo("invalid syntax", 1451, 152, 4, 1453, 152, 6),
                new ErrorInfo("expected name", 1459, 154, 3, 1461, 154, 5),
                new ErrorInfo("unexpected token '42'", 1476, 156, 7, 1478, 156, 9),
                new ErrorInfo("invalid syntax", 1511, 160, 12, 1512, 160, 13),
                new ErrorInfo("invalid syntax", 1524, 161, 10, 1527, 161, 13)
            );

            foreach (var version in V30_V32Versions) {
                ParseErrors("AllErrors.py",
                    version,
                    new ErrorInfo("future statement does not support import *", 0, 1, 1, 24, 1, 25),
                    new ErrorInfo("future feature is not defined: *", 0, 1, 1, 24, 1, 25),
                    new ErrorInfo("not a chance", 26, 2, 1, 55, 2, 30),
                    new ErrorInfo("future feature is not defined: unknown", 57, 3, 1, 87, 3, 31),
                    new ErrorInfo("default value must be specified here", 106, 5, 16, 107, 5, 17),
                    new ErrorInfo("non-keyword arg after keyword arg", 134, 8, 12, 135, 8, 13),
                    new ErrorInfo("only one * allowed", 147, 9, 10, 149, 9, 12),
                    new ErrorInfo("only one ** allowed", 162, 10, 11, 165, 10, 14),
                    new ErrorInfo("keywords must come before ** args", 180, 11, 13, 186, 11, 19),
                    new ErrorInfo("unexpected token 'pass'", 197, 14, 1, 201, 14, 5),
                    new ErrorInfo("unexpected token '42'", 217, 17, 11, 219, 17, 13),
                    new ErrorInfo("sublist parameters are not supported in 3.x", 216, 17, 10, 222, 17, 16),
                    new ErrorInfo("unexpected token '42'", 251, 20, 10, 253, 20, 12),
                    new ErrorInfo("'break' outside loop", 278, 25, 1, 283, 25, 6),
                    new ErrorInfo("'continue' not properly in loop", 285, 26, 1, 293, 26, 9),
                    new ErrorInfo("'continue' not supported inside 'finally' clause", 374, 34, 9, 382, 34, 17),
                    new ErrorInfo("expected expression after del", 386, 36, 1, 389, 36, 4),
                    new ErrorInfo("can't delete binary operator", 396, 37, 5, 399, 37, 8),
                    new ErrorInfo("can't delete unary operator", 405, 38, 5, 407, 38, 7),
                    new ErrorInfo("can't delete or expression", 413, 39, 5, 421, 39, 13),
                    new ErrorInfo("can't delete and expression", 427, 40, 5, 436, 40, 14),
                    new ErrorInfo("can't delete dictionary display", 442, 41, 5, 444, 41, 7),
                    new ErrorInfo("can't delete literal", 450, 42, 5, 454, 42, 9),
                    new ErrorInfo("can't delete literal", 460, 43, 5, 464, 43, 9),
                    new ErrorInfo("can't assign to literal", 468, 45, 1, 472, 45, 5),
                    new ErrorInfo("can't assign to literal", 482, 46, 1, 486, 46, 5),
                    new ErrorInfo("'return' outside function", 498, 48, 1, 504, 48, 7),
                    new ErrorInfo("'return' with argument inside generator", 539, 53, 5, 548, 53, 14),
                    new ErrorInfo("misplaced yield", 552, 55, 1, 557, 55, 6),
                    new ErrorInfo("'return' with argument inside generator", 581, 59, 5, 590, 59, 14),
                    new ErrorInfo("'return' with argument inside generator", 596, 60, 5, 606, 60, 15),
                    new ErrorInfo("two starred expressions in assignment", 660, 68, 8, 662, 68, 10),
                    new ErrorInfo("illegal expression for augmented assignment", 674, 70, 1, 676, 70, 3),
                    new ErrorInfo("missing module name", 692, 72, 6, 698, 72, 12),
                    new ErrorInfo("import * only allowed at module level", 720, 75, 5, 735, 75, 20),
                    new ErrorInfo("from __future__ imports must occur at the beginning of the file", 749, 78, 1, 780, 78, 32),
                    new ErrorInfo("nonlocal declaration not allowed at module level", 788, 82, 1, 796, 82, 9),
                    new ErrorInfo("invalid syntax, only exception value is allowed in 3.x.", 814, 83, 10, 819, 83, 15),
                    new ErrorInfo("default value must be specified here", 924, 99, 15, 925, 99, 16),
                    new ErrorInfo("duplicate * args arguments", 987, 105, 13, 988, 105, 14),
                    new ErrorInfo("duplicate * args arguments", 1017, 108, 13, 1018, 108, 14),
                    new ErrorInfo("named arguments must follow bare *", 1044, 111, 10, 1048, 111, 14),
                    new ErrorInfo("sublist parameters are not supported in 3.x", 1072, 114, 10, 1077, 114, 15),
                    new ErrorInfo("unexpected token '42'", 1107, 117, 11, 1109, 117, 13),
                    new ErrorInfo("sublist parameters are not supported in 3.x", 1106, 117, 10, 1112, 117, 16),
                    new ErrorInfo("duplicate argument 'abc' in function definition", 1143, 120, 12, 1146, 120, 15),
                    new ErrorInfo("duplicate argument 'abc' in function definition", 1177, 123, 16, 1180, 123, 19),
                    new ErrorInfo("sublist parameters are not supported in 3.x", 1171, 123, 10, 1180, 123, 19),
                    new ErrorInfo("unexpected token '42'", 1208, 127, 7, 1210, 127, 9),
                    new ErrorInfo("\", variable\" not allowed in 3.x - use \"as variable\" instead.", 1277, 134, 17, 1280, 134, 20),
                    new ErrorInfo("default 'except' must be last", 1242, 132, 1, 1248, 132, 7),
                    new ErrorInfo("\", variable\" not allowed in 3.x - use \"as variable\" instead.", 1379, 144, 17, 1382, 144, 20),
                    new ErrorInfo("cannot mix bytes and nonbytes literals", 1404, 147, 8, 1409, 147, 13),
                    new ErrorInfo("cannot mix bytes and nonbytes literals", 1417, 148, 7, 1423, 148, 13),
                    new ErrorInfo("invalid syntax", 1431, 149, 7, 1433, 149, 9),
                    new ErrorInfo("invalid syntax", 1431, 149, 7, 1433, 149, 9),
                    new ErrorInfo("invalid syntax", 1442, 150, 8, 1444, 150, 10),
                    new ErrorInfo("invalid syntax", 1442, 150, 8, 1444, 150, 10),
                    new ErrorInfo("invalid syntax", 1451, 152, 4, 1453, 152, 6),
                    new ErrorInfo("expected name", 1459, 154, 3, 1461, 154, 5),
                    new ErrorInfo("unexpected token '42'", 1476, 156, 7, 1478, 156, 9),
                    new ErrorInfo("invalid syntax", 1511, 160, 12, 1512, 160, 13),
                    new ErrorInfo("invalid syntax", 1524, 161, 10, 1527, 161, 13)
                );
            }

            foreach (var version in V33AndUp) {
                ParseErrors("AllErrors.py",
                    version,
                    new ErrorInfo("future statement does not support import *", 0, 1, 1, 24, 1, 25),
                    new ErrorInfo("future feature is not defined: *", 0, 1, 1, 24, 1, 25),
                    new ErrorInfo("not a chance", 26, 2, 1, 55, 2, 30),
                    new ErrorInfo("future feature is not defined: unknown", 57, 3, 1, 87, 3, 31),
                    new ErrorInfo("default value must be specified here", 106, 5, 16, 107, 5, 17),
                    new ErrorInfo("non-keyword arg after keyword arg", 134, 8, 12, 135, 8, 13),
                    new ErrorInfo("only one * allowed", 147, 9, 10, 149, 9, 12),
                    new ErrorInfo("only one ** allowed", 162, 10, 11, 165, 10, 14),
                    new ErrorInfo("keywords must come before ** args", 180, 11, 13, 186, 11, 19),
                    new ErrorInfo("unexpected token 'pass'", 197, 14, 1, 201, 14, 5),
                    new ErrorInfo("unexpected token '42'", 217, 17, 11, 219, 17, 13),
                    new ErrorInfo("sublist parameters are not supported in 3.x", 216, 17, 10, 222, 17, 16),
                    new ErrorInfo("unexpected token '42'", 251, 20, 10, 253, 20, 12),
                    new ErrorInfo("'break' outside loop", 278, 25, 1, 283, 25, 6),
                    new ErrorInfo("'continue' not properly in loop", 285, 26, 1, 293, 26, 9),
                    new ErrorInfo("'continue' not supported inside 'finally' clause", 374, 34, 9, 382, 34, 17),
                    new ErrorInfo("expected expression after del", 386, 36, 1, 389, 36, 4),
                    new ErrorInfo("can't delete binary operator", 396, 37, 5, 399, 37, 8),
                    new ErrorInfo("can't delete unary operator", 405, 38, 5, 407, 38, 7),
                    new ErrorInfo("can't delete or expression", 413, 39, 5, 421, 39, 13),
                    new ErrorInfo("can't delete and expression", 427, 40, 5, 436, 40, 14),
                    new ErrorInfo("can't delete dictionary display", 442, 41, 5, 444, 41, 7),
                    new ErrorInfo("can't delete literal", 450, 42, 5, 454, 42, 9),
                    new ErrorInfo("can't delete literal", 460, 43, 5, 464, 43, 9),
                    new ErrorInfo("can't assign to literal", 468, 45, 1, 472, 45, 5),
                    new ErrorInfo("can't assign to literal", 482, 46, 1, 486, 46, 5),
                    new ErrorInfo("'return' outside function", 498, 48, 1, 504, 48, 7),
                    new ErrorInfo("misplaced yield", 552, 55, 1, 557, 55, 6),
                    new ErrorInfo("two starred expressions in assignment", 660, 68, 8, 662, 68, 10),
                    new ErrorInfo("illegal expression for augmented assignment", 674, 70, 1, 676, 70, 3),
                    new ErrorInfo("missing module name", 692, 72, 6, 698, 72, 12),
                    new ErrorInfo("import * only allowed at module level", 720, 75, 5, 735, 75, 20),
                    new ErrorInfo("from __future__ imports must occur at the beginning of the file", 749, 78, 1, 780, 78, 32),
                    new ErrorInfo("nonlocal declaration not allowed at module level", 788, 82, 1, 796, 82, 9),
                    new ErrorInfo("invalid syntax, only exception value is allowed in 3.x.", 814, 83, 10, 819, 83, 15),
                    new ErrorInfo("default value must be specified here", 924, 99, 15, 925, 99, 16),
                    new ErrorInfo("duplicate * args arguments", 987, 105, 13, 988, 105, 14),
                    new ErrorInfo("duplicate * args arguments", 1017, 108, 13, 1018, 108, 14),
                    new ErrorInfo("named arguments must follow bare *", 1044, 111, 10, 1048, 111, 14),
                    new ErrorInfo("sublist parameters are not supported in 3.x", 1072, 114, 10, 1077, 114, 15),
                    new ErrorInfo("unexpected token '42'", 1107, 117, 11, 1109, 117, 13),
                    new ErrorInfo("sublist parameters are not supported in 3.x", 1106, 117, 10, 1112, 117, 16),
                    new ErrorInfo("duplicate argument 'abc' in function definition", 1143, 120, 12, 1146, 120, 15),
                    new ErrorInfo("duplicate argument 'abc' in function definition", 1177, 123, 16, 1180, 123, 19),
                    new ErrorInfo("sublist parameters are not supported in 3.x", 1171, 123, 10, 1180, 123, 19),
                    new ErrorInfo("unexpected token '42'", 1208, 127, 7, 1210, 127, 9),
                    new ErrorInfo("\", variable\" not allowed in 3.x - use \"as variable\" instead.", 1277, 134, 17, 1280, 134, 20),
                    new ErrorInfo("default 'except' must be last", 1242, 132, 1, 1248, 132, 7),
                    new ErrorInfo("\", variable\" not allowed in 3.x - use \"as variable\" instead.", 1379, 144, 17, 1382, 144, 20),
                    new ErrorInfo("cannot mix bytes and nonbytes literals", 1404, 147, 8, 1409, 147, 13),
                    new ErrorInfo("cannot mix bytes and nonbytes literals", 1417, 148, 7, 1423, 148, 13),
                    new ErrorInfo("invalid syntax", 1431, 149, 7, 1433, 149, 9),
                    new ErrorInfo("invalid syntax", 1431, 149, 7, 1433, 149, 9),
                    new ErrorInfo("invalid syntax", 1442, 150, 8, 1444, 150, 10),
                    new ErrorInfo("invalid syntax", 1442, 150, 8, 1444, 150, 10),
                    new ErrorInfo("invalid syntax", 1451, 152, 4, 1453, 152, 6),
                    new ErrorInfo("expected name", 1459, 154, 3, 1461, 154, 5),
                    new ErrorInfo("unexpected token '42'", 1476, 156, 7, 1478, 156, 9),
                    new ErrorInfo("invalid syntax", 1511, 160, 12, 1512, 160, 13),
                    new ErrorInfo("invalid syntax", 1524, 161, 10, 1527, 161, 13)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void InvalidUnicodeLiteral() {
            foreach (var version in V26AndUp) {
                ParseErrors("InvalidUnicodeLiteral26Up.py",
                    version,
                    new ErrorInfo("invalid \\uxxxx escape", 42, 2, 2, 48, 2, 8)
                );
            }

            foreach (var version in V2Versions) {
                ParseErrors("InvalidUnicodeLiteral2x.py",
                    version,
                    new ErrorInfo("invalid \\uxxxx escape", 2, 1, 3, 8, 1, 9)
                );
            }

            foreach (var version in V3Versions) {
                ParseErrors("InvalidUnicodeLiteral.py",
                    version,
                    new ErrorInfo("invalid \\uxxxx escape", 1, 1, 2, 7, 1, 8)
                );
            }
        }


        [TestMethod, Priority(0)]
        public void DedentError() {
            foreach (var version in AllVersions) {
                ParseErrors("DedentError.py",
                    version,
                    new ErrorInfo("unindent does not match any outer indentation level", 63, 4, 1, 68, 4, 6)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void DedentErrorLargeFile() {
            foreach (var version in AllVersions) {
                ParseErrors("DedentErrorLargeFile.py",
                    version,
                    new ErrorInfo("unindent does not match any outer indentation level", 3037, 10, 1, 3043, 10, 7)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void Literals() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("Literals.py", version),
                    CheckSuite(
                        CheckStrOrBytesStmt(version, "abc"),
                        CheckStrOrBytesStmt(version, "raw string"),
                        CheckStrOrBytesStmt(version, "raw string"),
                        CheckStrOrBytesStmt(version, "abc"),
                        CheckStrOrBytesStmt(version, "raw string"),
                        CheckStrOrBytesStmt(version, "raw string"),
                        CheckStrOrBytesStmt(version, "abc"),
                        CheckStrOrBytesStmt(version, "raw string"),
                        CheckStrOrBytesStmt(version, "raw string"),
                        CheckStrOrBytesStmt(version, "abc"),
                        CheckStrOrBytesStmt(version, "raw string"),
                        CheckStrOrBytesStmt(version, "raw string"),
                        CheckConstantStmtAndRepr(1000, "1000", version),
                        CheckConstantStmtAndRepr(2147483647, "2147483647", version),
                        CheckConstantStmtAndRepr(3.14, "3.14", version),
                        CheckConstantStmtAndRepr(10.0, "10.0", version),
                        CheckConstantStmtAndRepr(.001, "0.001", version),
                        CheckConstantStmtAndRepr(1e100, "1e+100", version),
                        CheckConstantStmtAndRepr(3.14e-10, "3.14e-10", version),
                        CheckConstantStmtAndRepr(0e0, "0.0", version),
                        CheckConstantStmtAndRepr(new Complex(0, 3.14), "3.14j", version),
                        CheckConstantStmtAndRepr(new Complex(0, 10), "10j", version),
                        CheckConstantStmt(new Complex(0, 10)),
                        CheckConstantStmtAndRepr(new Complex(0, .001), "0.001j", version),
                        CheckConstantStmtAndRepr(new Complex(0, 1e100), "1e+100j", version),
                        CheckConstantStmtAndRepr(new Complex(0, 3.14e-10), "3.14e-10j", version),
                        CheckUnaryStmt(PythonOperator.Negate, CheckConstant(
                            new BigInteger(2147483648L),
                            "2147483648" + (version.Is3x() ? "" : "L"),
                            version
                        )),
                        CheckUnaryStmt(PythonOperator.Negate, CheckConstant(100))
                    )
                );
            }

            foreach (var version in V2Versions) {
                CheckAst(
                    ParseFileNoErrors("LiteralsV2.py", version),
                    CheckSuite(
                        CheckConstantStmtAndRepr((BigInteger)1000, "1000L", version),
                        CheckConstantStmtAndRepr("unicode string", "u'unicode string'", version),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmt("raw unicode"),
                        CheckConstantStmtAndRepr(
                            "\\'\"\a\b\f\n\r\t\u2026\v\x2A\x2A",
                            "u'\\\\\\\'\"\\x07\\x08\\x0c\\n\\r\\t\\u2026\\x0b**'",
                            PythonLanguageVersion.V27
                        ),
                        IgnoreStmt(), // u'\N{COLON}',
                        CheckUnaryStmt(PythonOperator.Negate, CheckConstant(new BigInteger(2147483648))),
                        CheckUnaryStmt(PythonOperator.Negate, CheckConstant(new BigInteger(2147483648))),
                        CheckConstantStmt(464),
                        CheckUnaryStmt(PythonOperator.Negate, CheckConstant(new BigInteger(100)))
                    )
                );
            }

            foreach (var version in V30_V32Versions) {
                ParseErrors("LiteralsV2.py",
                    version,
                    new ErrorInfo("invalid syntax", 4, 1, 5, 5, 1, 6),
                    new ErrorInfo("invalid syntax", 7, 2, 1, 9, 2, 3),
                    new ErrorInfo("invalid syntax", 26, 3, 1, 28, 3, 3),
                    new ErrorInfo("invalid syntax", 45, 4, 1, 48, 4, 4),
                    new ErrorInfo("invalid syntax", 62, 5, 1, 65, 5, 4),
                    new ErrorInfo("invalid syntax", 79, 6, 1, 82, 6, 4),
                    new ErrorInfo("invalid syntax", 96, 7, 1, 99, 7, 4),
                    new ErrorInfo("invalid syntax", 113, 8, 1, 117, 8, 5),
                    new ErrorInfo("invalid syntax", 136, 9, 1, 140, 9, 5),
                    new ErrorInfo("invalid syntax", 159, 10, 1, 164, 10, 6),
                    new ErrorInfo("invalid syntax", 180, 11, 1, 185, 11, 6),
                    new ErrorInfo("invalid syntax", 201, 12, 1, 206, 12, 6),
                    new ErrorInfo("invalid syntax", 222, 13, 1, 227, 13, 6),
                    new ErrorInfo("invalid syntax", 243, 14, 1, 245, 14, 3),
                    new ErrorInfo("invalid syntax", 262, 15, 1, 264, 15, 3),
                    new ErrorInfo("invalid syntax", 281, 16, 1, 284, 16, 4),
                    new ErrorInfo("invalid syntax", 298, 17, 1, 301, 17, 4),
                    new ErrorInfo("invalid syntax", 315, 18, 1, 318, 18, 4),
                    new ErrorInfo("invalid syntax", 332, 19, 1, 335, 19, 4),
                    new ErrorInfo("invalid syntax", 349, 20, 1, 353, 20, 5),
                    new ErrorInfo("invalid syntax", 372, 21, 1, 376, 21, 5),
                    new ErrorInfo("invalid syntax", 395, 22, 1, 400, 22, 6),
                    new ErrorInfo("invalid syntax", 416, 23, 1, 421, 23, 6),
                    new ErrorInfo("invalid syntax", 437, 24, 1, 442, 24, 6),
                    new ErrorInfo("invalid syntax", 458, 25, 1, 463, 25, 6),
                    new ErrorInfo("invalid syntax", 479, 26, 1, 481, 26, 3),
                    new ErrorInfo("invalid syntax", 521, 28, 1, 523, 28, 3),
                    new ErrorInfo("invalid syntax", 546, 29, 12, 547, 29, 13),
                    new ErrorInfo("invalid syntax", 560, 30, 12, 561, 30, 13),
                    new ErrorInfo("invalid syntax", 564, 31, 2, 567, 31, 5),
                    new ErrorInfo("invalid syntax", 573, 32, 5, 574, 32, 6)
                );
            }

            foreach (var version in V33AndUp) {
                ParseErrors("LiteralsV2.py",
                    version,
                    new ErrorInfo("invalid syntax", 4, 1, 5, 5, 1, 6),
                    new ErrorInfo("r and u prefixes are not compatible", 45, 4, 1, 48, 4, 4),
                    new ErrorInfo("r and u prefixes are not compatible", 62, 5, 1, 65, 5, 4),
                    new ErrorInfo("r and u prefixes are not compatible", 79, 6, 1, 82, 6, 4),
                    new ErrorInfo("r and u prefixes are not compatible", 96, 7, 1, 99, 7, 4),
                    new ErrorInfo("r and u prefixes are not compatible", 159, 10, 1, 164, 10, 6),
                    new ErrorInfo("r and u prefixes are not compatible", 180, 11, 1, 185, 11, 6),
                    new ErrorInfo("r and u prefixes are not compatible", 201, 12, 1, 206, 12, 6),
                    new ErrorInfo("r and u prefixes are not compatible", 222, 13, 1, 227, 13, 6),
                    new ErrorInfo("r and u prefixes are not compatible", 281, 16, 1, 284, 16, 4),
                    new ErrorInfo("r and u prefixes are not compatible", 298, 17, 1, 301, 17, 4),
                    new ErrorInfo("r and u prefixes are not compatible", 315, 18, 1, 318, 18, 4),
                    new ErrorInfo("r and u prefixes are not compatible", 332, 19, 1, 335, 19, 4),
                    new ErrorInfo("r and u prefixes are not compatible", 395, 22, 1, 400, 22, 6),
                    new ErrorInfo("r and u prefixes are not compatible", 416, 23, 1, 421, 23, 6),
                    new ErrorInfo("r and u prefixes are not compatible", 437, 24, 1, 442, 24, 6),
                    new ErrorInfo("r and u prefixes are not compatible", 458, 25, 1, 463, 25, 6),
                    new ErrorInfo("invalid syntax", 546, 29, 12, 547, 29, 13),
                    new ErrorInfo("invalid syntax", 560, 30, 12, 561, 30, 13),
                    new ErrorInfo("invalid syntax", 564, 31, 2, 567, 31, 5),
                    new ErrorInfo("invalid syntax", 573, 32, 5, 574, 32, 6));
                CheckAst(
                    ParseFileNoErrors("LiteralsV3.py", version),
                    CheckSuite(
                        CheckConstantStmtAndRepr(true, "True", version),
                        CheckConstantStmtAndRepr(false, "False", version),
                        CheckConstantStmtAndRepr(new BigInteger(111222333444), "111222333444", version),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("raw string"),
                        CheckConstantStmt("raw string"),
                        CheckConstantStmt(Encoding.ASCII.GetBytes("byte string")),
                        CheckConstantStmt(Encoding.ASCII.GetBytes("byte string")),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("raw string"),
                        CheckConstantStmt("raw string"),
                        CheckConstantStmt(Encoding.ASCII.GetBytes("byte string")),
                        CheckConstantStmt(Encoding.ASCII.GetBytes("byte string")),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("raw string"),
                        CheckConstantStmt("raw string"),
                        CheckConstantStmt(Encoding.ASCII.GetBytes("byte string")),
                        CheckConstantStmt(Encoding.ASCII.GetBytes("byte string")),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("unicode string"),
                        CheckConstantStmt("raw string"),
                        CheckConstantStmt("raw string"),
                        CheckConstantStmt(Encoding.ASCII.GetBytes("byte string")),
                        CheckConstantStmt(Encoding.ASCII.GetBytes("byte string")),
                        CheckConstantStmtAndRepr("\\\'\"\a\b\f\n\r\t\u2026\v\x2A\x2A", "'\\\\\\'\"\\x07\\x08\\x0c\\n\\r\\t\\u2026\\x0b**'", PythonLanguageVersion.V33),
                        IgnoreStmt()  // u'\N{COLON}'
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void Literals26() {
            foreach (var version in V26AndUp) {
                CheckAst(
                    ParseFileNoErrors("Literals26.py", version),
                    CheckSuite(
                        CheckConstantStmt(464),
                        CheckConstantStmt(4)
                    )
                );
            }

            foreach (var version in V25Versions) {
                ParseErrors("Literals26.py",
                    version,
                    new ErrorInfo("invalid syntax", 0, 1, 1, 5, 1, 6),
                    new ErrorInfo("invalid syntax", 7, 2, 1, 12, 2, 6)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void Keywords25() {
            foreach (var version in V25Versions) {
                CheckAst(
                    ParseFileNoErrors("Keywords25.py", version),
                    CheckSuite(
                        CheckAssignment(CheckNameExpr("with"), One),
                        CheckAssignment(CheckNameExpr("as"), Two)
                    )
                );
            }

            foreach (var version in V26AndUp) {
                ParseErrors("Keywords25.py",
                    version,
                    new ErrorInfo("invalid syntax", 5, 1, 6, 8, 1, 9),
                    new ErrorInfo("invalid syntax", 10, 2, 1, 16, 2, 7)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void Keywords2x() {
            foreach (var version in V2Versions) {
                CheckAst(
                    ParseFileNoErrors("Keywords2x.py", version),
                    CheckSuite(
                        CheckAssignment(CheckNameExpr("True"), One),
                        CheckAssignment(CheckNameExpr("False"), Zero)
                    )
                );
            }

            foreach (var version in V3Versions) {
                ParseErrors("Keywords2x.py",
                    version,
                    new ErrorInfo("can't assign to literal", 0, 1, 1, 4, 1, 5),
                    new ErrorInfo("can't assign to literal", 10, 2, 1, 15, 2, 6)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void Keywords30() {
            foreach (var version in V3Versions) {
                CheckAst(
                    ParseFileNoErrors("Keywords30.py", version),
                    CheckSuite(
                        CheckAssignment(Fob, CheckConstant(true)),
                        CheckAssignment(Oar, CheckConstant(false))
                    )
                );
            }

            foreach (var version in V2Versions) {
                CheckAst(
                     ParseFileNoErrors("Keywords30.py", version),
                     CheckSuite(
                         CheckAssignment(Fob, CheckNameExpr("True")),
                         CheckAssignment(Oar, CheckNameExpr("False"))
                     )
                 );
            }
        }

        [TestMethod, Priority(0)]
        public void BinaryOperators() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("BinaryOperators.py", version),
                    CheckSuite(
                        CheckBinaryStmt(One, PythonOperator.Add, Two),
                        CheckBinaryStmt(One, PythonOperator.Subtract, Two),
                        CheckBinaryStmt(One, PythonOperator.Multiply, Two),
                        CheckBinaryStmt(One, PythonOperator.Power, Two),
                        CheckBinaryStmt(One, PythonOperator.Divide, Two),
                        CheckBinaryStmt(One, PythonOperator.FloorDivide, Two),
                        CheckBinaryStmt(One, PythonOperator.Mod, Two),
                        CheckBinaryStmt(One, PythonOperator.LeftShift, Two),
                        CheckBinaryStmt(One, PythonOperator.RightShift, Two),
                        CheckBinaryStmt(One, PythonOperator.BitwiseAnd, Two),
                        CheckBinaryStmt(One, PythonOperator.BitwiseOr, Two),
                        CheckBinaryStmt(One, PythonOperator.BitwiseXor, Two),
                        CheckBinaryStmt(One, PythonOperator.LessThan, Two),
                        CheckBinaryStmt(One, PythonOperator.GreaterThan, Two),
                        CheckBinaryStmt(One, PythonOperator.LessThanOrEqual, Two),
                        CheckBinaryStmt(One, PythonOperator.GreaterThanOrEqual, Two),
                        CheckBinaryStmt(One, PythonOperator.Equal, Two),
                        CheckBinaryStmt(One, PythonOperator.NotEqual, Two),
                        CheckBinaryStmt(One, PythonOperator.Is, Two),
                        CheckBinaryStmt(One, PythonOperator.IsNot, Two),
                        CheckExprStmt(CheckOrExpression(One, Two)),
                        CheckExprStmt(CheckAndExpression(One, Two)),
                        CheckBinaryStmt(One, PythonOperator.In, Two),
                        CheckBinaryStmt(One, PythonOperator.NotIn, Two)
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void BinaryOperatorsV2() {
            foreach (var version in V2Versions) {
                CheckAst(
                    ParseFileNoErrors("BinaryOperatorsV2.py", version),
                    CheckSuite(
                        CheckBinaryStmt(One, PythonOperator.NotEqual, Two)
                    )
                );
            }

            foreach (var version in V3Versions) {
                ParseErrors("BinaryOperatorsV2.py", version, new[] { 
                    new ErrorInfo("invalid syntax", 2, 1, 3, 4, 1, 5)
                });
            }
        }

        [TestMethod, Priority(0)]
        public void MatMulOperator() {
            foreach (var version in V35AndUp) {
                CheckAst(
                    ParseFileNoErrors("MatMulOperator.py", version),
                    CheckSuite(
                        CheckBinaryStmt(One, PythonOperator.MatMultiply, Two)
                    )
                );
            }

            foreach (var version in V3Versions.Except(V35AndUp)) {
                ParseErrors("MatMulOperator.py", version, new[] { 
                    new ErrorInfo("invalid syntax", 2, 1, 3, 3, 1, 4),
                    new ErrorInfo("invalid syntax", 4, 1, 5, 5, 1, 6)
                });
            }
        }

        [TestMethod, Priority(0)]
        public void GroupingRecovery() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileIgnoreErrors("GroupingRecovery.py", version),
                    CheckSuite(
                        CheckAssignment(Fob, CheckErrorExpr()),
                        CheckFuncDef("f", new Action<Parameter>[] {
                            p => {
                                Assert.AreEqual("a", p.Name);
                                Assert.AreEqual(15, p.Span.Start.Index);
                            }
                        }, CheckSuite(Pass))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void UnaryOperators() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("UnaryOperators.py", version),
                    CheckSuite(
                        CheckUnaryStmt(PythonOperator.Negate, One),
                        CheckUnaryStmt(PythonOperator.Invert, One),
                        CheckUnaryStmt(PythonOperator.Pos, One),
                        CheckUnaryStmt(PythonOperator.Not, One),
                        CheckUnaryStmt(PythonOperator.Negate, CheckUnaryExpression(PythonOperator.Negate, One)),
                        CheckUnaryStmt(PythonOperator.Invert, CheckUnaryExpression(PythonOperator.Invert, One)),
                        CheckUnaryStmt(PythonOperator.Pos, CheckUnaryExpression(PythonOperator.Pos, One)),
                        CheckUnaryStmt(PythonOperator.Not, CheckUnaryExpression(PythonOperator.Not, One))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void StringPlus() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("StringPlus.py", version),
                    CheckSuite(
                        CheckStrOrBytesStmt(version, "hello again")
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void BytesPlus() {
            foreach (var version in V26AndUp) {
                CheckAst(
                    ParseFileNoErrors("BytesPlus.py", version),
                    CheckSuite(
                        CheckExprStmt(CheckConstant(ToBytes("hello again")))
                    )
                );
            }

            foreach (var version in V25Versions) {
                ParseErrors("BytesPlus.py", version,
                    new ErrorInfo("invalid syntax", 0, 1, 1, 2, 1, 3),
                    new ErrorInfo("invalid syntax", 9, 1, 10, 11, 1, 12)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void UnicodePlus() {
            foreach (var version in V2Versions.Concat(V33AndUp)) {
                CheckAst(
                    ParseFileNoErrors("UnicodePlus.py", version),
                    CheckSuite(
                        CheckExprStmt(CheckConstant("hello again"))
                    )
                );
            }

            foreach (var version in V30_V32Versions) {
                ParseErrors("UnicodePlus.py", version,
                    new ErrorInfo("invalid syntax", 0, 1, 1, 2, 1, 3),
                    new ErrorInfo("invalid syntax", 9, 1, 10, 11, 1, 12)
                );

            }
        }

        [TestMethod, Priority(0)]
        public void RawBytes() {
            foreach (var version in V33AndUp) {
                CheckAst(
                    ParseFileNoErrors("RawBytes.py", version),
                    CheckSuite(
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob"))),
                        CheckExprStmt(CheckConstant(ToBytes("\\fob")))
                    )
                );
            }

            foreach (var version in AllVersions.Except(V33AndUp)) {
                ParseErrors("RawBytes.py", version,
                    new ErrorInfo("invalid syntax", 0, 1, 1, 3, 1, 4),
                    new ErrorInfo("invalid syntax", 10, 2, 1, 15, 2, 6),
                    new ErrorInfo("invalid syntax", 24, 3, 1, 27, 3, 4),
                    new ErrorInfo("invalid syntax", 34, 4, 1, 39, 4, 6),
                    new ErrorInfo("invalid syntax", 48, 5, 1, 51, 5, 4),
                    new ErrorInfo("invalid syntax", 58, 6, 1, 63, 6, 6),
                    new ErrorInfo("invalid syntax", 72, 7, 1, 75, 7, 4),
                    new ErrorInfo("invalid syntax", 82, 8, 1, 87, 8, 6),
                    new ErrorInfo("invalid syntax", 96, 9, 1, 99, 9, 4),
                    new ErrorInfo("invalid syntax", 106, 10, 1, 111, 10, 6),
                    new ErrorInfo("invalid syntax", 120, 11, 1, 123, 11, 4),
                    new ErrorInfo("invalid syntax", 130, 12, 1, 135, 12, 6),
                    new ErrorInfo("invalid syntax", 144, 13, 1, 147, 13, 4),
                    new ErrorInfo("invalid syntax", 154, 14, 1, 159, 14, 6),
                    new ErrorInfo("invalid syntax", 168, 15, 1, 171, 15, 4),
                    new ErrorInfo("invalid syntax", 178, 16, 1, 183, 16, 6)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void Delimiters() {
            foreach (var version in AllVersions) {
                var ast = version.Is3x() ?
                    ParseFileNoErrors("Delimiters.py", version) :
                    ParseFileIgnoreErrors("Delimiters.py", version);

                CheckAst(
                    ast,
                    CheckSuite(
                        CheckCallStmt(One, PositionalArg(Two)),
                        CheckIndexStmt(One, Two),
                        CheckDictionaryStmt(DictItem(One, Two)),
                        CheckTupleStmt(One, Two, Three),
                        CheckIndexStmt(One, CheckSlice(Two, Three)),
                        CheckIndexStmt(One, CheckSlice(Two, Three, Four)),
                        CheckIndexStmt(One, CheckSlice(Two, null, Four)),
                        CheckIndexStmt(One, CheckSlice(null, null, Four)),
                        CheckIndexStmt(One, Ellipsis),
                        CheckIndexStmt(One, CheckTupleExpr(CheckSlice(null, null))),
                        CheckMemberStmt(Fob, "oar"),
                        CheckAssignment(Fob, One),
                        CheckAssignment(Fob, PythonOperator.Add, One),
                        CheckAssignment(Fob, PythonOperator.Subtract, One),
                        CheckAssignment(Fob, PythonOperator.Multiply, One),
                        CheckAssignment(Fob, PythonOperator.Divide, One),
                        CheckAssignment(Fob, PythonOperator.FloorDivide, One),
                        CheckAssignment(Fob, PythonOperator.Mod, One),
                        CheckAssignment(Fob, PythonOperator.BitwiseAnd, One),
                        CheckAssignment(Fob, PythonOperator.BitwiseOr, One),
                        CheckAssignment(Fob, PythonOperator.BitwiseXor, One),
                        CheckAssignment(Fob, PythonOperator.RightShift, One),
                        CheckAssignment(Fob, PythonOperator.LeftShift, One),
                        CheckAssignment(Fob, PythonOperator.Power, One)
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void DelimitersV2() {
            foreach (var version in V2Versions) {
                CheckAst(
                    ParseFileNoErrors("DelimitersV2.py", version),
                    CheckSuite(
                        CheckBackquoteStmt(Fob)
                    )
                );
            }

            foreach (var version in V3Versions) {
                ParseErrors(
                    "DelimitersV2.py",
                    version,
                    new ErrorInfo("invalid syntax", 0, 1, 1, 5, 1, 6)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void ForStmt() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("ForStmt.py", version),
                    CheckSuite(
                        CheckForStmt(Fob, Oar, CheckSuite(Pass)),
                        CheckForStmt(CheckTupleExpr(Fob, Oar), Baz, CheckSuite(Pass)),
                        CheckForStmt(Fob, Oar, CheckSuite(Pass), CheckSuite(Pass)),
                        CheckForStmt(Fob, Oar, CheckSuite(Break)),
                        CheckForStmt(Fob, Oar, CheckSuite(Continue)),
                        CheckForStmt(CheckListExpr(CheckListExpr(Fob), CheckListExpr(Oar)), Baz, CheckSuite(Pass)),
                        CheckForStmt(CheckParenExpr(CheckTupleExpr(CheckParenExpr(Fob), CheckParenExpr(Oar))), Baz, CheckSuite(Pass)),
                        CheckForStmt(Fob, CheckTupleExpr(CheckStrOrBytes(version, "b"), CheckStrOrBytes(version, "a"), CheckStrOrBytes(version, "z")), CheckSuite(Pass))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void WithStmt() {
            foreach (var version in V26AndUp) {
                CheckAst(
                    ParseFileNoErrors("WithStmt.py", version),
                    CheckSuite(
                        CheckWithStmt(Fob, CheckSuite(Pass)),
                        CheckWithStmt(CheckAsExpression(Fob, Oar), CheckSuite(Pass)),
                        CheckWithStmt(CheckTupleExpr(Fob, Oar), CheckSuite(Pass)),
                        CheckWithStmt(CheckTupleExpr(
                            CheckAsExpression(Fob, Oar),
                            CheckAsExpression(Baz, Quox)
                        ), CheckSuite(Pass)),
                        CheckWithStmt(expr => CheckMemberExpr(Oar, "fob"), CheckSuite(Pass))
                    )
                );
            }

            foreach (var version in V25Versions) {
                ParseErrors("WithStmt.py", version,
                    new ErrorInfo("invalid syntax", 5, 1, 6, 14, 1, 15),
                    new ErrorInfo("invalid syntax", 23, 3, 6, 39, 3, 22),
                    new ErrorInfo("invalid syntax", 48, 5, 6, 62, 5, 20),
                    new ErrorInfo("invalid syntax", 71, 7, 6, 100, 7, 35),
                    new ErrorInfo("invalid syntax", 109, 9, 6, 130, 9, 27)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void Semicolon() {
            foreach (var version in V26AndUp) {
                CheckAst(
                    ParseFileNoErrors("Semicolon.py", version),
                    CheckSuite(
                        CheckConstantStmt(1),
                        CheckConstantStmt(2),
                        CheckConstantStmt(3),
                        CheckNameStmt("fob"),
                        CheckNameStmt("oar"),
                        CheckNameStmt("baz")
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void DelStmt() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("DelStmt.py", version),
                    CheckSuite(
                        CheckDelStmt(Fob),
                        CheckDelStmt(Fob, Oar),
                        CheckDelStmt(CheckMemberExpr(Fob, "oar")),
                        CheckDelStmt(CheckIndexExpression(Fob, Oar)),
                        CheckDelStmt(CheckParenExpr(CheckTupleExpr(Fob, Oar))),
                        CheckDelStmt(CheckListExpr(Fob, Oar)),
                        CheckDelStmt(CheckParenExpr(Fob)),
                        // Semicolon separated
                        CheckDelStmt(Fob), CheckDelStmt(Oar)
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void IndexExpr() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("IndexExpr.py", version),
                    CheckSuite(
                        CheckExprStmt(CheckIndexExpression(Fob, CheckConstant(.2)))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void DelStmtIllegal() {
            foreach (var version in AllVersions) {
                ParseErrors("DelStmtIllegal.py", version,
                    new ErrorInfo("can't delete literal", 4, 1, 5, 5, 1, 6),
                    new ErrorInfo("can't delete generator expression", 12, 2, 6, 30, 2, 24),
                    new ErrorInfo("can't delete function call", 37, 3, 5, 45, 3, 13)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void YieldStmt() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("YieldStmt.py", version),
                    CheckSuite(
                        CheckFuncDef("f", NoParameters,
                            CheckSuite(
                                CheckYieldStmt(One),
                                CheckYieldStmt(CheckTupleExpr(One, Two))
                            )
                        )
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void YieldExpr() {
            foreach (var version in V25AndUp) {
                CheckAst(
                    ParseFileNoErrors("YieldExpr.py", version),
                    CheckSuite(
                        CheckFuncDef("f", NoParameters, CheckSuite(
                            CheckYieldStmt(EmptyExpr)
                        )),
                        CheckFuncDef("f", NoParameters, CheckSuite(
                            CheckAssignment(Fob, CheckYieldExpr(EmptyExpr))
                        )),
                        CheckFuncDef("f", NoParameters, CheckSuite(
                            CheckAssignment(Baz, CheckListComp(CheckParenExpr(CheckYieldExpr(Oar)), CompFor(Oar, Fob)))
                        ))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void YieldStmtIllegal() {
            foreach (var version in V2Versions.Concat(V30_V32Versions)) {
                ParseErrors("YieldStmtIllegal.py", version,
                    new ErrorInfo("'yield' outside of generator", 0, 1, 1, 5, 1, 6),
                    new ErrorInfo("'return' with argument inside generator", 25, 4, 5, 34, 4, 14),
                    new ErrorInfo("'return' with argument inside generator", 78, 9, 5, 87, 9, 14)
                );
            }

            // return inside generator is legal as of 3.3
            foreach (var version in V33AndUp) {
                ParseErrors("YieldStmtIllegal.py", version,
                    new ErrorInfo("'yield' outside of generator", 0, 1, 1, 5, 1, 6)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void YieldFromStmt() {
            foreach (var version in V33AndUp) {
                CheckAst(
                    ParseFileNoErrors("YieldFromStmt.py", version),
                    CheckSuite(
                        CheckFuncDef("f", NoParameters,
                            CheckSuite(
                                CheckYieldFromStmt(Fob)
                            )
                        )
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void YieldFromExpr() {
            foreach (var version in V33AndUp) {
                CheckAst(
                    ParseFileNoErrors("YieldFromExpr.py", version),
                    CheckSuite(
                        CheckFuncDef("f", NoParameters,
                            CheckSuite(
                                CheckYieldFromStmt(Fob),
                                CheckAssignment(Oar, CheckYieldFromExpr(Fob)),
                                CheckAssignment(Baz, CheckListComp(CheckParenExpr(CheckYieldFromExpr(Oar)), CompFor(Oar, Fob)))
                            )
                        )
                    )
                );
            }

            foreach (var version in V25_V27Versions.Concat(V30_V32Versions)) {
                ParseErrors("YieldFromExpr.py", version,
                    new ErrorInfo("'yield from' requires 3.3 or later", 14, 2, 5, 24, 2, 15),
                    new ErrorInfo("'yield from' requires 3.3 or later", 40, 3, 11, 50, 3, 21),
                    new ErrorInfo("'yield from' requires 3.3 or later", 68, 4, 13, 78, 4, 23)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void YieldFromStmtIllegal() {
            foreach (var version in V25_V27Versions.Concat(V30_V32Versions)) {
                ParseErrors("YieldFromStmtIllegal.py", version,
                    new ErrorInfo("'yield from' requires 3.3 or later", 0, 1, 1, 10, 1, 11),
                    new ErrorInfo("'return' with argument inside generator", 30, 4, 5, 39, 4, 14),
                    new ErrorInfo("'yield from' requires 3.3 or later", 45, 5, 5, 55, 5, 15),
                    new ErrorInfo("'yield from' requires 3.3 or later", 75, 8, 5, 85, 8, 15),
                    new ErrorInfo("'return' with argument inside generator", 93, 9, 5, 102, 9, 14),
                    new ErrorInfo("'yield from' requires 3.3 or later", 120, 12, 5, 130, 12, 15),
                    new ErrorInfo("invalid syntax", 120, 12, 5, 130, 12, 15),
                    new ErrorInfo("'yield from' requires 3.3 or later", 148, 15, 5, 158, 15, 15),
                    new ErrorInfo("invalid syntax", 160, 15, 17, 166, 16, 1)
                );
            }

            foreach (var version in V33AndUp) {
                ParseErrors("YieldFromStmtIllegal.py", version,
                    new ErrorInfo("'yield from' outside of generator", 0, 1, 1, 10, 1, 11),
                    new ErrorInfo("invalid syntax", 120, 12, 5, 130, 12, 15),
                    new ErrorInfo("invalid syntax", 160, 15, 17, 166, 16, 1)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void ImportStmt() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("ImportStmt.py", version),
                    CheckSuite(
                        CheckImport(new[] { "sys" }),
                        CheckImport(new[] { "sys", "fob" }),
                        CheckImport(new[] { "sys" }, new[] { "oar" }),
                        CheckImport(new[] { "sys", "fob" }, new[] { "oar", "baz" }),
                        CheckImport(new[] { "sys.fob" }),
                        CheckImport(new[] { "sys.fob" }, new[] { "oar" })
                    )
                );
            }

            foreach (var version in AllVersions) {
                ParseErrors("ImportStmtIllegal.py", version,
                    new ErrorInfo("invalid syntax", 17, 1, 18, 26, 1, 27)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void GlobalStmt() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("GlobalStmt.py", version),
                    CheckSuite(
                        CheckFuncDef("f", NoParameters,
                            CheckSuite(
                                CheckGlobal("a"),
                                CheckGlobal("a", "b")
                            )
                        )
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void NonlocalStmt() {
            foreach (var version in V3Versions) {
                CheckAst(
                    ParseFileNoErrors("NonlocalStmt.py", version),
                    CheckSuite(
                        CheckFuncDef("g", NoParameters,
                            CheckSuite(
                                CheckAssignment(Fob, One),
                                CheckAssignment(Oar, One),
                                CheckFuncDef("f", NoParameters,
                                    CheckSuite(
                                        CheckNonlocal("fob"),
                                        CheckNonlocal("fob", "oar")
                                    )
                                )
                            )
                        ),
                        CheckFuncDef("g", NoParameters,
                            CheckSuite(
                                CheckFuncDef("f", NoParameters,
                                    CheckSuite(
                                        CheckNonlocal("fob")
                                    )
                                ),
                                CheckAssignment(Fob, One)
                            )
                        ),
                        CheckFuncDef("f", NoParameters,
                            CheckSuite(
                                CheckClassDef("C",
                                    CheckSuite(
                                        CheckNonlocal("fob"),
                                        CheckAssignment(Fob, One)
                                    )
                                ),
                                CheckAssignment(Fob, Two)
                            )
                        ),
                        CheckClassDef("X",
                            CheckSuite(
                                CheckFuncDef("f", new[] { CheckParameter("x") },
                                    CheckSuite(
                                        CheckNonlocal("__class__")
                                    )
                                )
                            )
                        )
                    )
                );
            }

            foreach (var version in V2Versions) {
                ParseErrors("NonlocalStmt.py", version,
                    new ErrorInfo("invalid syntax", 67, 5, 18, 70, 5, 21),
                    new ErrorInfo("invalid syntax", 89, 6, 18, 97, 6, 26),
                    new ErrorInfo("invalid syntax", 144, 11, 18, 147, 11, 21),
                    new ErrorInfo("invalid syntax", 209, 18, 18, 212, 18, 21),
                    new ErrorInfo("invalid syntax", 288, 24, 18, 297, 24, 27)
                );
            }
        }

        [TestMethod, Priority(1)]
        public void NonlocalStmtIllegal() {
            foreach (var version in V3Versions) {
                ParseErrors("NonlocalStmtIllegal.py", version,
                    new ErrorInfo("nonlocal declaration not allowed at module level", 195, 17, 1, 203, 17, 9),
                    new ErrorInfo("name 'x' is nonlocal and global", 118, 10, 13, 128, 10, 23),
                    new ErrorInfo("name 'x' is a parameter and nonlocal", 181, 15, 13, 191, 15, 23),
                    new ErrorInfo("no binding for nonlocal 'x' found", 406, 35, 22, 407, 35, 23),
                    new ErrorInfo("no binding for nonlocal 'x' found", 306, 27, 12, 307, 27, 13),
                    new ErrorInfo("no binding for nonlocal 'globalvar' found", 250, 21, 14, 259, 21, 23),
                    new ErrorInfo("no binding for nonlocal 'a' found", 41, 3, 18, 42, 3, 19)
                );
            }

        }

        [TestMethod, Priority(0)]
        public void WhileStmt() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("WhileStmt.py", version),
                    CheckSuite(
                        CheckWhileStmt(One, CheckSuite(Pass)),
                        CheckWhileStmt(One, CheckSuite(Pass), CheckSuite(Pass))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void TryStmt() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("TryStmt.py", version),
                    CheckSuite(
                        CheckTryStmt(
                            CheckSuite(Pass),
                            CheckHandler(null, CheckSuite(Pass))
                        ),
                        CheckTryStmt(
                            CheckSuite(Pass),
                            CheckHandler(Exception, CheckSuite(Pass))
                        )
                    )
                );
            }

            // execpt Exception as e: vs except Exception, e:
            // comma supported in 2.4/2.5, both supported in 2.6 - 2.7, as supported in 3.x
            foreach (var version in V25Versions) {
                TryStmtV2(version);

                ParseErrors(
                    "TryStmtV3.py", version,
                    new ErrorInfo("'as' requires Python 2.6 or later", 23, 3, 8, 37, 3, 22)
                );
            }

            foreach (var version in V26_V27Versions) {
                TryStmtV2(version);
                TryStmtV3(version);
            }

            foreach (var version in V3Versions) {
                TryStmtV3(version);

                ParseErrors(
                    "TryStmtV2.py", version,
                    new ErrorInfo("\", variable\" not allowed in 3.x - use \"as variable\" instead.", 32, 3, 17, 35, 3, 20)
                );
            }
        }

        private void TryStmtV3(PythonLanguageVersion version) {
            CheckAst(
                ParseFileNoErrors("TryStmtV3.py", version),
                CheckSuite(
                    CheckTryStmt(
                        CheckSuite(Pass),
                        CheckHandler(CheckAsExpression(Exception, CheckNameExpr("e")), CheckSuite(Pass))
                    )
                )
            );
        }

        private void TryStmtV2(PythonLanguageVersion version) {
            CheckAst(
                ParseFileNoErrors("TryStmtV2.py", version),
                CheckSuite(
                    CheckTryStmt(
                        CheckSuite(Pass),
                        CheckHandler(CheckTupleExpr(Exception, CheckNameExpr("e")), CheckSuite(Pass))
                    )
                )
            );
        }

        [TestMethod, Priority(0)]
        public void RaiseStmt() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("RaiseStmt.py", version),
                    CheckSuite(
                        CheckRaiseStmt(),
                        CheckRaiseStmt(Fob)
                    )
                );
            }

            foreach (var version in V2Versions) {
                CheckAst(
                    ParseFileNoErrors("RaiseStmtV2.py", version),
                    CheckSuite(
                        CheckRaiseStmt(Fob, Oar),
                        CheckRaiseStmt(Fob, Oar, Baz)
                    )
                );

                ParseErrors(
                    "RaiseStmtV3.py", version,
                    new ErrorInfo("invalid syntax, from cause not allowed in 2.x.", 10, 1, 11, 18, 1, 19)
                );
            }

            foreach (var version in V3Versions) {
                CheckAst(
                    ParseFileNoErrors("RaiseStmtV3.py", version),
                    CheckSuite(
                        CheckRaiseStmt(Fob, cause: Oar)
                    )
                );

                ParseErrors(
                    "RaiseStmtV2.py", version,
                    new ErrorInfo("invalid syntax, only exception value is allowed in 3.x.", 9, 1, 10, 14, 1, 15),
                    new ErrorInfo("invalid syntax, only exception value is allowed in 3.x.", 25, 2, 10, 35, 2, 20)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void PrintStmt() {
            foreach (var version in V2Versions) {
                CheckAst(
                    ParseFileNoErrors("PrintStmt.py", version),
                    CheckSuite(
                        CheckPrintStmt(null),
                        CheckPrintStmt(new[] { One }),
                        CheckPrintStmt(new[] { One }, trailingComma: true),
                        CheckPrintStmt(new[] { One, Two }),
                        CheckPrintStmt(new[] { One, Two }, trailingComma: true),
                        CheckPrintStmt(new[] { One, Two }, Fob),
                        CheckPrintStmt(new[] { One, Two }, Fob, trailingComma: true),
                        CheckPrintStmt(null, Fob),
                        CheckPrintStmt(new[] { CheckBinaryExpression(One, PythonOperator.Equal, Two) }),
                        CheckPrintStmt(new[] { CheckLambda(new Action<Parameter>[0], One) })
                    )
                );
            }

            foreach (var version in V3Versions) {
                ParseErrors(
                    "PrintStmt.py", version,
                    new ErrorInfo("Missing parentheses in call to 'print'", 13, 2, 7, 14, 2, 8),
                    new ErrorInfo("Missing parentheses in call to 'print'", 22, 3, 7, 24, 3, 9),
                    new ErrorInfo("Missing parentheses in call to 'print'", 32, 4, 7, 36, 4, 11),
                    new ErrorInfo("Missing parentheses in call to 'print'", 44, 5, 7, 50, 5, 13),
                    new ErrorInfo("Missing parentheses in call to 'print'", 110, 9, 7, 116, 9, 13),
                    new ErrorInfo("invalid syntax", 124, 10, 7, 133, 10, 16)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void ExplicitLineJoins() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("ExplicitLineJoins.py", version),
                    CheckSuite(
                        CheckAssignment(Fob, CheckMemberExpr(CheckCallExpression(Oar, CheckArg("eggs")), "spam"))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void ComplexCode() {
            foreach (var version in AllVersions) {
                // This file is a broad sample of code that has previously
                // caused incorrect parser errors. If the parser does not break,
                // it is good enough. More specific tests should be used for
                // ensuring the generated AST is valid.s
                var ast = ParseFileNoErrors("ComplexCode.py", version);
            }
        }


        [TestMethod, Priority(0)]
        public void AssertStmt() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("AssertStmt.py", version),
                    CheckSuite(
                        CheckAssertStmt(One),
                        CheckAssertStmt(One, Fob)
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void ListComp() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("ListComp.py", version),
                    CheckSuite(
                        CheckExprStmt(CheckListExpr()),
                        CheckExprStmt(CheckListExpr()),
                        CheckExprStmt(CheckListComp(Fob, CompFor(Fob, Oar))),
                        CheckExprStmt(CheckListComp(Fob, CompFor(Fob, Oar), CompIf(Baz))),
                        CheckExprStmt(CheckListComp(Fob, CompFor(Fob, Oar), CompFor(Baz, Quox)))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void ListComp2x() {
            foreach (var version in V2Versions) {
                CheckAst(
                    ParseFileNoErrors("ListComp2x.py", version),
                    CheckSuite(
                        CheckExprStmt(CheckListComp(Fob, CompFor(Fob, CheckTupleExpr(Oar, Baz))))
                    )
                );
            }

            foreach (var version in V3Versions) {
                ParseErrors("ListComp2x.py", version,
                    new ErrorInfo("invalid syntax", 19, 1, 20, 25, 1, 26)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void GenComp() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("GenComp.py", version),
                    CheckSuite(
                        CheckExprStmt(CheckParenExpr(CheckGeneratorComp(Fob, CompFor(Fob, Oar)))),
                        CheckExprStmt(CheckParenExpr(CheckGeneratorComp(Fob, CompFor(Fob, Oar), CompIf(Baz)))),
                        CheckExprStmt(CheckParenExpr(CheckGeneratorComp(Fob, CompFor(Fob, Oar), CompFor(Baz, Quox)))),
                        CheckCallStmt(Baz, PositionalArg(CheckGeneratorComp(Fob, CompFor(Fob, Oar))))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void DictComp() {
            foreach (var version in V27AndUp) {
                CheckAst(
                    ParseFileNoErrors("DictComp.py", version),
                    CheckSuite(
                        CheckDictionaryStmt(),
                        CheckDictionaryStmt(),
                        CheckExprStmt(CheckDictComp(Fob, Oar, CompFor(CheckTupleExpr(Fob, Oar), Baz))),
                        CheckExprStmt(CheckDictComp(Fob, Oar, CompFor(CheckTupleExpr(Fob, Oar), Baz), CompIf(Quox))),
                        CheckExprStmt(CheckDictComp(Fob, Oar, CompFor(CheckTupleExpr(Fob, Oar), Baz), CompFor(Quox, Exception)))
                    )
                );
            }

            foreach (var version in V25_V26Versions) {
                ParseErrors("DictComp.py", version,
                    new ErrorInfo("invalid syntax, dictionary comprehensions require Python 2.7 or later", 10, 3, 2, 36, 3, 28),
                    new ErrorInfo("invalid syntax, dictionary comprehensions require Python 2.7 or later", 40, 4, 2, 74, 4, 36),
                    new ErrorInfo("invalid syntax, dictionary comprehensions require Python 2.7 or later", 78, 5, 2, 126, 5, 50)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void SetComp() {
            foreach (var version in V27AndUp) {
                CheckAst(
                    ParseFileNoErrors("SetComp.py", version),
                    CheckSuite(
                        CheckExprStmt(CheckSetComp(Fob, CompFor(Fob, Baz))),
                        CheckExprStmt(CheckSetComp(Fob, CompFor(Fob, Baz), CompIf(Quox))),
                        CheckExprStmt(CheckSetComp(Fob, CompFor(Fob, Baz), CompFor(Quox, Exception)))
                    )
                );
            }

            foreach (var version in V25_V26Versions) {
                ParseErrors("SetComp.py", version,
                    new ErrorInfo("invalid syntax, set literals require Python 2.7 or later", 1, 1, 2, 19, 1, 20),
                    new ErrorInfo("invalid syntax, set literals require Python 2.7 or later", 23, 2, 2, 49, 2, 28),
                    new ErrorInfo("invalid syntax, set literals require Python 2.7 or later", 53, 3, 2, 93, 3, 42)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void SetLiteral() {
            foreach (var version in V27AndUp) {
                CheckAst(
                    ParseFileNoErrors("SetLiteral.py", version),
                    CheckSuite(
                        CheckExprStmt(CheckSetLiteral(One)),
                        CheckExprStmt(CheckSetLiteral(One, Two))
                    )
                );
            }

            foreach (var version in V25_V26Versions) {
                ParseErrors("SetLiteral.py", version,
                    new ErrorInfo("invalid syntax, set literals require Python 2.7 or later", 1, 1, 2, 2, 1, 3),
                    new ErrorInfo("invalid syntax, set literals require Python 2.7 or later", 6, 2, 2, 10, 2, 6)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void IfStmt() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("IfStmt.py", version),
                    CheckSuite(
                        CheckIfStmt(CheckIf(One, CheckSuite(Pass))),
                        CheckIfStmt(CheckIf(One, CheckSuite(Pass)), CheckIf(Two, CheckSuite(Pass))),
                        CheckIfStmt(CheckIf(One, CheckSuite(Pass)), CheckElse(CheckSuite(Pass)))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void FromImportStmt() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("FromImportStmt.py", version),
                    CheckSuite(
                        CheckFromImport("sys", new[] { "winver" }),
                        CheckFromImport("sys", new[] { "winver" }, new[] { "baz" }),
                        CheckFromImport("sys.fob", new[] { "winver" }),
                        CheckFromImport("sys.fob", new[] { "winver" }, new[] { "baz" }),
                        CheckFromImport(".", new[] { "oar" }),
                        CheckFromImport("...fob", new[] { "oar" }),
                        CheckFromImport("....fob", new[] { "oar" }),
                        CheckFromImport("......fob", new[] { "oar" }),
                        CheckFromImport(".......fob", new[] { "oar" }),
                        CheckFromImport("fob", new[] { "fob", "baz" }, new string[] { "oar", "quox" })
                    )
                );
            }

            foreach (var version in V2Versions) {
                CheckAst(
                    ParseFileNoErrors("FromImportStmtV2.py", version),
                    CheckSuite(
                        CheckFuncDef("f", NoParameters,
                            CheckSuite(CheckFromImport("sys", new[] { "*" }))
                        ),
                        CheckClassDef("C",
                            CheckSuite(CheckFromImport("sys", new[] { "*" }))
                        )
                    )
                );
            }

            foreach (var version in V3Versions) {
                ParseErrors(
                    "FromImportStmtV2.py",
                    version,
                    new ErrorInfo("import * only allowed at module level", 14, 2, 5, 31, 2, 22),
                    new ErrorInfo("import * only allowed at module level", 49, 5, 5, 66, 5, 22)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void FromImportStmtIllegal() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileIgnoreErrors("FromImportStmtIllegal.py", version),
                    CheckSuite(
                        CheckFromImport("", new[] { "fob" })
                    )
                );

                ParseErrors(
                    "FromImportStmtIllegal.py",
                    version,
                    new ErrorInfo("missing module name", 5, 1, 6, 11, 1, 12)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void FromImportStmtIncomplete() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileIgnoreErrors("FromImportStmtIncomplete.py", version),
                    CheckSuite(
                        CheckFuncDef(
                            "f",
                            NoParameters,
                            CheckSuite(
                                CheckFromImport("sys", new[] { "abc" })
                            )
                        )
                    )
                );

                ParseErrors(
                    "FromImportStmtIncomplete.py",
                    version,
                    new ErrorInfo("trailing comma not allowed without surrounding parentheses", 33, 2, 24, 34, 2, 25)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void DecoratorsFuncDef() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("DecoratorsFuncDef.py", version),
                    CheckSuite(
                        CheckFuncDef("f", NoParameters, CheckSuite(Pass), new[] { Fob }),
                        CheckFuncDef("f", NoParameters, CheckSuite(Pass), new[] { CheckMemberExpr(Fob, "oar") }),
                        CheckFuncDef("f", NoParameters, CheckSuite(Pass), new[] { CheckCallExpression(Fob, PositionalArg(Oar)) }),
                        CheckFuncDef("f", NoParameters, CheckSuite(Pass), new[] { Fob, Oar })
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void DecoratorsAsyncFuncDef() {
            foreach (var version in V35AndUp) {
                CheckAst(
                    ParseFileNoErrors("DecoratorsAsyncFuncDef.py", version),
                    CheckSuite(
                        CheckFuncDef("f", NoParameters, CheckSuite(Pass), new[] { Fob }, isAsync: true),
                        CheckFuncDef("f", NoParameters, CheckSuite(Pass), new[] { CheckMemberExpr(Fob, "oar") }, isAsync: true),
                        CheckFuncDef("f", NoParameters, CheckSuite(Pass), new[] { CheckCallExpression(Fob, PositionalArg(Oar)) }, isAsync: true),
                        CheckFuncDef("f", NoParameters, CheckSuite(Pass), new[] { Fob, Oar }, isAsync: true)
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void DecoratorsClassDef() {
            foreach (var version in V26AndUp) {
                CheckAst(
                    ParseFileNoErrors("DecoratorsClassDef.py", version),
                    CheckSuite(
                        CheckClassDef("C", CheckSuite(Pass), decorators: new[] { Fob }),
                        CheckClassDef("C", CheckSuite(Pass), decorators: new[] { CheckMemberExpr(Fob, "oar") }),
                        CheckClassDef("C", CheckSuite(Pass), decorators: new[] { CheckCallExpression(Fob, PositionalArg(Oar)) }),
                        CheckClassDef("C", CheckSuite(Pass), decorators: new[] { Fob, Oar })
                    )
                );
            }

            foreach (var version in V25Versions) {
                ParseErrors("DecoratorsClassDef.py",
                    version,
                    new ErrorInfo("invalid syntax, class decorators require 2.6 or later.", 0, 1, 1, 4, 1, 5),
                    new ErrorInfo("invalid syntax, class decorators require 2.6 or later.", 23, 4, 1, 31, 4, 9),
                    new ErrorInfo("invalid syntax, class decorators require 2.6 or later.", 52, 8, 1, 61, 8, 10),
                    new ErrorInfo("invalid syntax, class decorators require 2.6 or later.", 80, 11, 1, 84, 11, 5),
                    new ErrorInfo("invalid syntax, class decorators require 2.6 or later.", 86, 12, 1, 90, 12, 5)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void DecoratorsIllegal() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileIgnoreErrors("DecoratorsIllegal.py", version),
                    CheckSuite(
                        stmt => { Assert.IsInstanceOfType(stmt, typeof(DecoratorStatement)); },
                        CheckAssignment(Fob, One)
                    )
                );
            }

            foreach (var version in AllVersions) {
                var msg = "invalid decorator, must be applied to function";
                if (version >= PythonLanguageVersion.V26) {
                    msg += " or class";
                }
                ParseErrors("DecoratorsIllegal.py",
                    version,
                    new ErrorInfo(msg, 0, 1, 1, 4, 1, 5)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void Calls() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("Calls.py", version),
                    CheckSuite(
                        CheckCallStmt(Fob),
                        CheckCallStmt(Fob, PositionalArg(One)),
                        CheckCallStmt(Fob, NamedArg("oar", One)),
                        CheckCallStmt(Fob, ListArg(Oar)),
                        CheckCallStmt(Fob, DictArg(Oar)),
                        CheckCallStmt(Fob, ListArg(Oar), DictArg(Baz)),
                        CheckCallStmt(Fob, NamedArg("oar", One), NamedArg("baz", Two)),
                        CheckCallStmt(Fob, PositionalArg(Oar), PositionalArg(Baz))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void CallsIllegal() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileIgnoreErrors("CallsIllegal.py", version),
                    CheckSuite(
                        CheckCallStmt(Fob, NamedArg("oar", One), NamedArg("oar", Two)),
                        CheckCallStmt(Fob, NamedArg(null, Two))
                    )
                );
            }

            foreach (var version in AllVersions) {
                ParseErrors("CallsIllegal.py",
                    version,
                    new ErrorInfo("keyword argument repeated", 13, 1, 14, 16, 1, 17),
                    new ErrorInfo("expected name", 27, 2, 5, 28, 2, 6)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void LambdaExpr() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("LambdaExpr.py", version),
                    CheckSuite(
                        CheckLambdaStmt(new[] { CheckParameter("x") }, One),
                        CheckLambdaStmt(new[] { CheckParameter("x", ParameterKind.List) }, One),
                        CheckLambdaStmt(new[] { CheckParameter("x", ParameterKind.Dictionary) }, One)
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void FuncDef() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("FuncDef.py", version),
                    CheckSuite(
                        CheckFuncDef("f", NoParameters, CheckSuite(Pass)),
                        CheckFuncDef("f", new[] { CheckParameter("a") }, CheckSuite(Pass)),
                        CheckFuncDef("f", new[] { CheckParameter("a"), CheckParameter("b", ParameterKind.List) }, CheckSuite(Pass)),
                        CheckFuncDef("f", new[] { CheckParameter("a"), CheckParameter("b", ParameterKind.List), CheckParameter("c", ParameterKind.Dictionary) }, CheckSuite(Pass)),
                        CheckFuncDef("f", new[] { CheckParameter("a", ParameterKind.List) }, CheckSuite(Pass)),
                        CheckFuncDef("f", new[] { CheckParameter("a", ParameterKind.Dictionary) }, CheckSuite(Pass)),

                        CheckFuncDef("f", NoParameters, CheckSuite(CheckReturnStmt(One))),
                        CheckFuncDef("f", NoParameters, CheckSuite(CheckReturnStmt()))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void FuncDefV2() {
            foreach (var version in V2Versions) {
                CheckAst(
                    ParseFileNoErrors("FuncDefV2.py", version),
                    CheckSuite(
                        CheckFuncDef("f", new[] { CheckParameter("a"), CheckSublistParameter("b", "c"), CheckParameter("d") }, CheckSuite(Pass))
                    )
                );
            }

            foreach (var version in V3Versions) {
                ParseErrors("FuncDefV2.py", version,
                    new ErrorInfo("sublist parameters are not supported in 3.x", 10, 1, 11, 14, 1, 15)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void FuncDefV3() {
            foreach (var version in V3Versions) {
                CheckAst(
                    ParseFileNoErrors("FuncDefV3.py", version),
                    CheckSuite(
                        CheckFuncDef("f", new[] { CheckParameter("a", ParameterKind.List), CheckParameter("x", ParameterKind.KeywordOnly) }, CheckSuite(Pass)),
                        CheckFuncDef("f", new[] { CheckParameter("a", ParameterKind.List), CheckParameter("x", ParameterKind.KeywordOnly, defaultValue: One) }, CheckSuite(Pass)),

                        CheckFuncDef("f", new[] { CheckParameter("a", annotation: One) }, CheckSuite(Pass)),
                        CheckFuncDef("f", new[] { CheckParameter("a", ParameterKind.List, annotation: One) }, CheckSuite(Pass)),
                        CheckFuncDef("f", new[] { CheckParameter("a", ParameterKind.Dictionary, annotation: One) }, CheckSuite(Pass)),
                        CheckFuncDef("f", new[] { CheckParameter("a", annotation: Zero), CheckParameter("b", ParameterKind.List, annotation: One), CheckParameter("c", ParameterKind.Dictionary, annotation: Two) }, CheckSuite(Pass)),

                        CheckFuncDef("f", NoParameters, CheckSuite(Pass), returnAnnotation: One),

                        CheckFuncDef("f", new[] { CheckParameter("a", annotation: One) }, CheckSuite(Pass), returnAnnotation: One),

                        CheckFuncDef("f", new[] { CheckParameter("a", defaultValue: Two, annotation: One) }, CheckSuite(Pass)),
                        CheckFuncDef("f", new[] { CheckParameter(null, ParameterKind.List), CheckParameter("a", ParameterKind.KeywordOnly) }, CheckSuite(Pass))

                    )
                );
            }

            foreach (var version in V2Versions) {
                ParseErrors("FuncDefV3.py", version,
                    new ErrorInfo("positional parameter after * args not allowed", 10, 1, 11, 11, 1, 12),
                    new ErrorInfo("positional parameter after * args not allowed", 30, 2, 11, 31, 2, 12),
                    new ErrorInfo("invalid syntax, parameter annotations require 3.x", 55, 4, 10, 56, 4, 11),
                    new ErrorInfo("invalid syntax, parameter annotations require 3.x", 75, 5, 11, 76, 5, 12),
                    new ErrorInfo("invalid syntax, parameter annotations require 3.x", 96, 6, 12, 97, 6, 13),
                    new ErrorInfo("invalid syntax, parameter annotations require 3.x", 115, 7, 10, 116, 7, 11),
                    new ErrorInfo("invalid syntax, parameter annotations require 3.x", 122, 7, 17, 123, 7, 18),
                    new ErrorInfo("invalid syntax, parameter annotations require 3.x", 130, 7, 25, 131, 7, 26),
                    new ErrorInfo("invalid syntax, function annotations require 3.x", 154, 9, 12, 155, 9, 13),
                    new ErrorInfo("invalid syntax, parameter annotations require 3.x", 174, 11, 10, 175, 11, 11),
                    new ErrorInfo("invalid syntax, function annotations require 3.x", 180, 11, 16, 181, 11, 17),
                    new ErrorInfo("invalid syntax, parameter annotations require 3.x", 200, 13, 10, 201, 13, 11),
                    new ErrorInfo("invalid syntax", 223, 15, 8, 233, 15, 18)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void FuncDefV3Illegal() {
            foreach (var version in V3Versions) {
                ParseErrors("FuncDefV3Illegal.py", version,
                    new ErrorInfo("named arguments must follow bare *", 6, 1, 7, 7, 1, 8),
                    new ErrorInfo("named arguments must follow bare *", 22, 2, 7, 23, 2, 8)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void CoroutineDef() {
            foreach (var version in V35AndUp) {
                CheckAst(
                    ParseFileNoErrors("CoroutineDef.py", version),
                    CheckSuite(
                        CheckFuncDef("f", NoParameters, CheckSuite(
                            CheckAsyncForStmt(CheckForStmt(Fob, Oar, CheckSuite(Pass))),
                            CheckAsyncWithStmt(CheckWithStmt(Baz, CheckSuite(Pass)))
                        ), isAsync: true)
                    )
                );

                ParseErrors("CoroutineDefIllegal.py", version,
                    new ErrorInfo("'yield' in async function", 20, 2, 5, 25, 2, 10),
                    new ErrorInfo("'yield' in async function", 40, 3, 9, 45, 3, 14),
                    new ErrorInfo("'async for' outside of async function", 68, 6, 5, 73, 6, 10),
                    new ErrorInfo("'async for' outside of async function", 107, 9, 1, 112, 9, 6),
                    new ErrorInfo("'async with' outside of async function", 156, 13, 5, 161, 13, 10),
                    new ErrorInfo("'async with' outside of async function", 189, 16, 1, 194, 16, 6)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void ClassDef() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("ClassDef.py", version),
                    CheckSuite(
                        CheckClassDef("C", CheckSuite(Pass)),
                        CheckClassDef("C", CheckSuite(Pass), new[] { CheckArg("object") }),
                        CheckClassDef("C", CheckSuite(Pass), new[] { CheckArg("list"), CheckArg("object") }),
                        CheckClassDef("C",
                            CheckSuite(
                                CheckClassDef(n => { Assert.AreEqual("__D", n.Name); Assert.AreEqual("_C", n.Prefix); },
                                    CheckSuite(
                                        CheckClassDef(n => { Assert.AreEqual("__E", n.Name); Assert.AreEqual("_D", n.Prefix); },
                                            CheckSuite(Pass)
                                        )
                                    )
                                )
                            )
                        )
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void ClassDef3x() {
            foreach (var version in V3Versions) {
                CheckAst(
                    ParseFileNoErrors("ClassDef3x.py", version),
                    CheckSuite(
                        CheckClassDef("C", CheckSuite(Pass), new[] { CheckNamedArg("metaclass", One) }),
                        CheckClassDef("C", CheckSuite(Pass), new[] { CheckArg("object"), CheckNamedArg("metaclass", One) }),
                        CheckClassDef("C", CheckSuite(Pass), new[] { CheckArg("list"), CheckArg("object"), CheckNamedArg("fob", One) })
                    )
                );
            }

            foreach (var version in V2Versions) {
                ParseErrors("ClassDef3x.py", version,
                    new ErrorInfo("invalid syntax", 8, 1, 9, 19, 1, 20),
                    new ErrorInfo("invalid syntax", 44, 2, 17, 55, 2, 28),
                    new ErrorInfo("invalid syntax", 86, 3, 23, 91, 3, 28)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void AssignStmt() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("AssignStmt.py", version),
                    CheckSuite(
                        CheckAssignment(CheckIndexExpression(Fob, One), Two),
                        CheckAssignment(CheckMemberExpr(Fob, "oar"), One),
                        CheckAssignment(Fob, One),
                        CheckAssignment(CheckParenExpr(Fob), One),
                        CheckAssignment(CheckParenExpr(CheckTupleExpr(Fob, Oar)), CheckTupleExpr(One, Two)),
                        CheckAssignment(CheckTupleExpr(Fob, Oar), CheckTupleExpr(One, Two)),
                        CheckAssignment(CheckTupleExpr(Fob, Oar), Baz),
                        CheckAssignment(CheckListExpr(Fob, Oar), CheckTupleExpr(One, Two)),
                        CheckAssignment(CheckListExpr(Fob, Oar), Baz),
                        CheckAssignment(new[] { Fob, Oar }, Baz),
                        CheckAssignment(CheckTupleExpr(Fob), CheckListExpr(One))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void AssignStmt2x() {
            foreach (var version in V2Versions) {
                CheckAst(
                    ParseFileNoErrors("AssignStmt2x.py", version),
                    CheckSuite(
                        CheckAssignment(Fob, CheckUnaryExpression(PythonOperator.Negate, CheckBinaryExpression(CheckConstant((BigInteger)2), PythonOperator.Power, CheckConstant(31))))
                    )
                );
                ParseErrors("AssignStmt2x.py", version);
            }
        }

        [TestMethod, Priority(0)]
        public void AssignStmt25() {
            foreach (var version in V25AndUp) {
                CheckAst(
                    ParseFileNoErrors("AssignStmt25.py", version),
                    CheckSuite(
                        CheckFuncDef(
                            "f",
                            NoParameters,
                            CheckSuite(
                                CheckAssignment(Fob, CheckYieldExpr(One)),
                                CheckAssignment(Fob, PythonOperator.Add, CheckYieldExpr(One))
                            )
                        )
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void AssignStmtV3() {
            foreach (var version in V3Versions) {
                CheckAst(
                    ParseFileNoErrors("AssignStmtV3.py", version),
                    CheckSuite(
                        CheckAssignment(CheckTupleExpr(CheckStarExpr(Fob), Oar, Baz), CheckTupleExpr(One, Two, Three, Four)),
                        CheckAssignment(CheckTupleExpr(Fob, CheckStarExpr(Oar), Baz), CheckTupleExpr(One, Two, Three, Four)),
                        CheckAssignment(CheckListExpr(Fob, CheckStarExpr(Oar), Baz), CheckTupleExpr(One, Two, Three, Four)),
                        CheckAssignment(CheckListExpr(CheckStarExpr(Fob), Oar, Baz), CheckTupleExpr(One, Two, Three, Four))
                    )
                );
            }

            foreach (var version in V2Versions) {
                ParseErrors("AssignStmtV3.py", version,
                    new ErrorInfo("invalid syntax", 0, 1, 1, 4, 1, 5),
                    new ErrorInfo("invalid syntax", 34, 2, 6, 38, 2, 10),
                    new ErrorInfo("invalid syntax", 64, 3, 7, 68, 3, 11),
                    new ErrorInfo("invalid syntax", 90, 4, 2, 94, 4, 6)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void AssignStmtIllegalV3() {
            foreach (var version in V3Versions) {
                CheckAst(
                    ParseFileNoErrors("AssignStmtIllegalV3.py", version),
                    CheckSuite(
                        CheckAssignment(CheckTupleExpr(Fob, CheckStarExpr(Oar), CheckStarExpr(Baz)), CheckTupleExpr(One, Two, Three, Four))
                    )
                );
            }

            foreach (var version in V2Versions) {
                ParseErrors("AssignStmtIllegalV3.py", version,
                    new ErrorInfo("invalid syntax", 5, 1, 6, 9, 1, 10),
                    new ErrorInfo("invalid syntax", 11, 1, 12, 15, 1, 16)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void AssignStmtIllegal() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileIgnoreErrors("AssignStmtIllegal.py", version),
                    CheckSuite(
                        CheckAssignment(CheckBinaryExpression(Fob, PythonOperator.Add, Oar), One),
                        CheckAssignment(CheckCallExpression(Fob), One),
                        CheckAssignment(None, One),
                        CheckAssignment(Two, One),
                        CheckAssignment(CheckParenExpr(CheckGeneratorComp(Fob, CompFor(Fob, Oar))), One),
                        CheckAssignment(CheckTupleExpr(Fob, Oar), PythonOperator.Add, One),
                        CheckFuncDef("f", NoParameters,
                            CheckSuite(
                                CheckAssignment(CheckParenExpr(CheckYieldExpr(Fob)), One)
                            )
                        )
                    )
                );
            }

            foreach (var version in AllVersions) {
                ParseErrors("AssignStmtIllegal.py", version,
                    new ErrorInfo("can't assign to operator", 0, 1, 1, 9, 1, 10),
                    new ErrorInfo("can't assign to function call", 15, 2, 1, 20, 2, 6),
                    new ErrorInfo("assignment to None", 26, 3, 1, 30, 3, 5),
                    new ErrorInfo("can't assign to literal", 36, 4, 1, 37, 4, 2),
                    new ErrorInfo("can't assign to generator expression", 44, 5, 2, 62, 5, 20),
                    new ErrorInfo("illegal expression for augmented assignment", 69, 6, 1, 77, 6, 9),
                    new ErrorInfo("can't assign to yield expression", 99, 8, 6, 108, 8, 15)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void AwaitStmt() {
            var AwaitFob = CheckAwaitExpression(Fob);
            foreach (var version in V35AndUp) {
                CheckAst(
                    ParseFileNoErrors("AwaitStmt.py", version),
                    CheckSuite(CheckFuncDef("quox", NoParameters, CheckSuite(
                        CheckExprStmt(AwaitFob),
                        CheckExprStmt(CheckAwaitExpression(CheckCallExpression(Fob))),
                        CheckExprStmt(CheckCallExpression(CheckParenExpr(AwaitFob))),
                        CheckBinaryStmt(One, PythonOperator.Add, AwaitFob),
                        CheckBinaryStmt(One, PythonOperator.Power, AwaitFob),
                        CheckBinaryStmt(One, PythonOperator.Power, CheckUnaryExpression(PythonOperator.Negate, AwaitFob))
                    ), isAsync: true))
                );
                ParseErrors("AwaitStmt.py", version);
            }
        }

        [TestMethod, Priority(0)]
        public void AwaitStmtPreV35() {
            foreach (var version in AllVersions.Except(V35AndUp)) {
                ParseErrors("AwaitStmt.py", version,
                    new ErrorInfo("invalid syntax", 6, 1, 7, 17, 1, 18),
                    new ErrorInfo("unexpected indent", 19, 2, 1, 23, 2, 5),
                    new ErrorInfo("invalid syntax", 29, 2, 11, 32, 2, 14),
                    new ErrorInfo("unexpected indent", 34, 3, 1, 38, 3, 5),
                    new ErrorInfo("invalid syntax", 44, 3, 11, 49, 3, 16),
                    new ErrorInfo("unexpected indent", 51, 4, 1, 55, 4, 5),
                    new ErrorInfo("invalid syntax", 62, 4, 12, 68, 4, 18),
                    new ErrorInfo("unexpected indent", 70, 5, 1, 74, 5, 5),
                    new ErrorInfo("invalid syntax", 84, 5, 15, 87, 5, 18),
                    new ErrorInfo("unexpected indent", 89, 6, 1, 93, 6, 5),
                    new ErrorInfo("invalid syntax", 104, 6, 16, 107, 6, 19),
                    new ErrorInfo("unexpected indent", 109, 7, 1, 113, 7, 5),
                    new ErrorInfo("invalid syntax", 125, 7, 17, 128, 7, 20)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void AwaitAsyncNames() {
            var Async = CheckNameExpr("async");
            var Await = CheckNameExpr("await");
            foreach (var version in AllVersions) {
                var ast = ParseFileNoErrors("AwaitAsyncNames.py", version);
                CheckAst(
                    ast,
                    CheckSuite(
                        CheckExprStmt(Async),
                        CheckExprStmt(Await),
                        CheckAssignment(Async, Fob),
                        CheckAssignment(Await, Fob),
                        CheckAssignment(Fob, Async),
                        CheckAssignment(Fob, Await),
                        CheckFuncDef("async", NoParameters, CheckSuite(Pass)),
                        CheckFuncDef("await", NoParameters, CheckSuite(Pass)),
                        CheckClassDef("async", CheckSuite(Pass)),
                        CheckClassDef("await", CheckSuite(Pass)),
                        CheckCallStmt(Async, CheckArg("fob")),
                        CheckCallStmt(Await, CheckArg("fob")),
                        CheckCallStmt(Fob, CheckArg("async")),
                        CheckCallStmt(Fob, CheckArg("await")),
                        CheckMemberStmt(Fob, "async"),
                        CheckMemberStmt(Fob, "await"),
                        CheckFuncDef("fob", new[] { CheckParameter("async"), CheckParameter("await") }, CheckSuite(Pass))
                    )
                );
                ParseErrors("AwaitAsyncNames.py", version);
            }
        }

        [TestMethod, Priority(0)]
        public void AwaitStmtIllegal() {
            foreach (var version in V35AndUp) {
                CheckAst(
                    ParseFileIgnoreErrors("AwaitStmtIllegal.py", version),
                    CheckSuite(
                        CheckErrorStmt(),
                        CheckFuncDef("quox", NoParameters, CheckSuite(CheckErrorStmt())),
                        CheckClassDef("quox", CheckSuite(CheckErrorStmt()))
                    )
                );
            }

            foreach (var version in V35AndUp) {
                ParseErrors("AwaitStmtIllegal.py", version,
                    new ErrorInfo("invalid syntax", 6, 1, 7, 10, 1, 11),
                    new ErrorInfo("invalid syntax", 37, 4, 11, 40, 4, 14),
                    new ErrorInfo("invalid syntax", 67, 7, 11, 70, 7, 14)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void ConditionalExpr() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("ConditionalExpr.py", version),
                    CheckSuite(
                        CheckExprStmt(CheckConditionalExpression(One, Two, Three)),
                        CheckExprStmt(CheckConditionalExpression(One, Two, Three)),
                        CheckExprStmt(CheckConditionalExpression(One, Two, Three)),
                        CheckExprStmt(CheckConditionalExpression(CheckConstant(1.0), CheckConstant(2e10), Three)),
                        CheckExprStmt(CheckConditionalExpression(One, CheckConstant(2.0), Three))
                    )
                );
            }
        }

        [TestMethod, Priority(0)]
        public void ExecStmt() {
            foreach (var version in V2Versions) {
                CheckAst(
                    ParseFileNoErrors("ExecStmt.py", version),
                    CheckSuite(
                        CheckExecStmt(Fob),
                        CheckExecStmt(Fob, Oar),
                        CheckExecStmt(Fob, Oar, Baz),
                        CheckExecStmt(Fob),
                        CheckExecStmt(Fob, Oar),
                        CheckExecStmt(Fob, Oar, Baz)
                    )
                );
            }

            foreach (var version in V3Versions) {
                ParseErrors("ExecStmt.py", version,
                    new ErrorInfo("invalid syntax", 5, 1, 6, 8, 1, 9),
                    new ErrorInfo("invalid syntax", 15, 2, 6, 25, 2, 16),
                    new ErrorInfo("invalid syntax", 32, 3, 6, 47, 3, 21)
                );
            }

        }

        [TestMethod, Priority(0)]
        public void EllipsisExpr() {
            foreach (var version in V3Versions) {
                CheckAst(
                    ParseFileNoErrors("Ellipsis.py", version),
                    CheckSuite(
                        CheckCallStmt(Fob, PositionalArg(Ellipsis)),
                        CheckBinaryStmt(One, PythonOperator.Add, Ellipsis)
                    )
                );
            }

            foreach (var version in V2Versions) {
                ParseErrors("Ellipsis.py", version,
                    new ErrorInfo("unexpected token '.'", 4, 1, 5, 7, 1, 8),
                    new ErrorInfo("unexpected token '.'", 14, 2, 5, 17, 2, 8)
                );
            }
        }

        [TestMethod, Priority(0)]
        public void FromFuture() {
            foreach (var version in AllVersions) {
                CheckAst(
                    ParseFileNoErrors("FromFuture24.py", version),
                    CheckSuite(
                        CheckFromImport("__future__", new[] { "division" }),
                        CheckFromImport("__future__", new[] { "generators" })
                    )
                );

                CheckAst(
                    ParseFileNoErrors("FromFuture25.py", version),
                    CheckSuite(
                        CheckFromImport("__future__", new[] { "with_statement" }),
                        CheckFromImport("__future__", new[] { "absolute_import" })
                    )
                );

                if (version == PythonLanguageVersion.V25) {
                    ParseErrors("FromFuture26.py", version,
                        new ErrorInfo("future feature is not defined until 2.6: print_function", 23, 1, 24, 37, 1, 38),
                        new ErrorInfo("future feature is not defined until 2.6: unicode_literals", 62, 2, 24, 78, 2, 40)
                    );
                } else {
                    CheckAst(
                        ParseFileNoErrors("FromFuture26.py", version),
                        CheckSuite(
                            CheckFromImport("__future__", new[] { "print_function" }),
                            CheckFromImport("__future__", new[] { "unicode_literals" })
                        )
                    );
                }

                if (version < PythonLanguageVersion.V35) {
                    ParseErrors("FromFuture35.py", version,
                        new ErrorInfo("future feature is not defined until 3.5: generator_stop", 23, 1, 24, 37, 1, 38)
                    );
                } else {
                    CheckAst(
                        ParseFileNoErrors("FromFuture35.py", version),
                        CheckSuite(
                            CheckFromImport("__future__", new[] { "generator_stop" })
                        )
                    );
                }
            }
        }

        [TestMethod, Priority(1)]
        public void ParseComments() {
            var version = PythonLanguageVersion.V35;
            var tree = ParseFileNoErrors("Comments.py", version);
            Assert.Fail("Not yet implemented");
        }

        #endregion

        #region Checker Factories / Helpers

        class ErrorInfo {
            public readonly string Message;
            public readonly SourceSpan Span;

            public ErrorInfo(string msg, int startIndex, int startLine, int startCol, int endIndex, int endLine, int endCol) {
                Message = msg;
                Span = new SourceSpan(new SourceLocation(startIndex, startLine, startCol), new SourceLocation(endIndex, endLine, endCol));
            }
        }

        private void ParseErrors(string filename, PythonLanguageVersion version, params ErrorInfo[] expectedErrors) {
            ParseErrors(filename, version, Severity.Ignore, expectedErrors);
        }

        private void ParseErrors(string filename, PythonLanguageVersion version, Severity indentationSeverity, params ErrorInfo[] expectedErrors) {
            var parser = CreateParser(filename, version);
            var errors = new CollectingErrorSink();
            parser.IndentationInconsistencySeverity = indentationSeverity;
            parser.Parse(errors);

            StringBuilder foundErrors = new StringBuilder();
            foreach(var error in errors.Errors.OrderBy(e => e.Span.Start.Index)) {
                foundErrors.AppendFormat("new ErrorInfo(\"{0}\", {1}, {2}, {3}, {4}, {5}, {6})," + Environment.NewLine,
                    error.Message,
                    error.Span.Start.Index,
                    error.Span.Start.Line,
                    error.Span.Start.Column,
                    error.Span.End.Index,
                    error.Span.End.Line,
                    error.Span.End.Column
                );
            }

            string finalErrors = foundErrors.ToString();
            Console.WriteLine(finalErrors);
            Assert.AreEqual(expectedErrors.Length, errors.Errors.Count, "Version: " + version + Environment.NewLine + "Unexpected errors: " + Environment.NewLine + finalErrors);

            int i = 0;
            foreach(var e in expectedErrors.Zip(errors.Errors.OrderBy(e => e.Span.Start.Index), Tuple.Create)) {
                if (e.Item1.Message != e.Item2.Message) {
                    Assert.Fail("Wrong msg for error {0}: expected {1}, got {2}", i, e.Item1.Message, e.Item2.Message);
                }
                if (e.Item1.Span.Start != e.Item2.Span.Start || e.Item1.Span.End != e.Item2.Span.End) {
                    Assert.Fail("Wrong span for error {0}: expected ({1}, {2}, {3} - {4}, {5}, {6}), got ({7}, {8}, {9} - {10}, {11}, {12})",
                        i,
                        e.Item1.Span.Start.Index,
                        e.Item1.Span.Start.Line,
                        e.Item1.Span.Start.Column,
                        e.Item1.Span.End.Index,
                        e.Item1.Span.End.Line,
                        e.Item1.Span.End.Column,
                        e.Item2.Span.Start.Index,
                        e.Item2.Span.Start.Line,
                        e.Item2.Span.Start.Column,
                        e.Item2.Span.End.Index,
                        e.Item2.Span.End.Line,
                        e.Item2.Span.End.Column
                    );
                }
                i += 1;
            }
        }

        private static PythonAst ParseFileNoErrors(string filename, PythonLanguageVersion version) {
            var parser = CreateParser(filename, version);
            var errors = new CollectingErrorSink();
            var tree = parser.Parse(errors);
            foreach (var err in errors.Errors) {
                Trace.TraceInformation("ERR:  {0} {1}", err.Span, err.Message);
            }
            Assert.AreEqual(0, errors.Errors.Count, "Parse errors occurred");
            return tree;
        }

        private static PythonAst ParseFileIgnoreErrors(string filename, PythonLanguageVersion version) {
            var parser = CreateParser(filename, version);
            return parser.Parse();
        }

        private static Parser CreateParser(string filename, PythonLanguageVersion version) {
            Trace.TraceInformation("Parsing {0} with {1}", filename, version.ToVersion());
            var tokenization = Tokenization.TokenizeAsync(
                new FileSourceDocument(PythonTestData.GetTestDataSourcePath("Grammar\\" + filename)),
                version,
                CancellationToken.None
            ).WaitAndUnwrapExceptions();
            return new Parser(tokenization);
        }

        private void CheckAst(PythonAst ast, Action<Statement> checkBody) {
            checkBody(ast.Body);
        }

        private static Action<Expression> Zero = CheckConstant(0);
        private static Action<Expression> One = CheckConstant(1);
        private static Action<Expression> Two = CheckConstant(2);
        private static Action<Expression> Three = CheckConstant(3);
        private static Action<Expression> Four = CheckConstant(4);
        private static Action<Expression> None = CheckConstant(null);
        private static Action<Expression> EmptyExpr = CheckEmptyExpr();
        private static Action<Expression> Fob = CheckNameExpr("fob");
        private static Action<Expression> Ellipsis = CheckConstant(Microsoft.PythonTools.Analysis.Parsing.Ellipsis.Value);
        private static Action<Expression> Oar = CheckNameExpr("oar");
        private static Action<Expression> Baz = CheckNameExpr("baz");
        private static Action<Expression> Quox = CheckNameExpr("quox");
        private static Action<Expression> Exception = CheckNameExpr("Exception");
        private static Action<Statement> Empty = CheckEmptyStmt();
        private static Action<Statement> Pass = CheckPassStmt();
        private static Action<Statement> Break = CheckBreakStmt();
        private static Action<Statement> Continue = CheckContinueStmt();


        private static Action<Statement> CheckSuite(params Action<Statement>[] statements) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(SuiteStatement));
                SuiteStatement suite = (SuiteStatement)stmt;
                var suiteStatements = suite.Statements.Where(s => !(s is EmptyStatement)).ToList();
                for (int i = 0; i < suiteStatements.Count && i < statements.Length; i++) {
                    try {
                        statements[i](suiteStatements[i]);
                    } catch (AssertFailedException e) {
                        Trace.TraceError(e.ToString());
                        throw new AssertFailedException(String.Format("Suite Item {0}: {1}", i, e.Message), e);
                    }
                }

                var extras = string.Join(Environment.NewLine, suiteStatements.Skip(statements.Length));
                Assert.AreEqual(statements.Length, suiteStatements.Count, "Extra statements:" + Environment.NewLine + extras);
            };
        }

        private static Action<Statement> CheckForStmt(Action<Expression> index, Action<Expression> list, Action<Statement> body, Action<Statement> _else = null) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(ForStatement));
                ForStatement forStmt = (ForStatement)stmt;

                index(forStmt.Index);
                list(forStmt.Expression);
                body(forStmt.Body);
                if (_else != null) {
                    _else(forStmt.Else.Body);
                } else {
                    Assert.IsNull(forStmt.Else);
                }
            };
        }

        private Action<Statement> CheckAsyncForStmt(Action<Statement> checkForStmt) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(ForStatement));
                var forStmt = (ForStatement)stmt;

                Assert.IsTrue(forStmt.IsAsync);

                checkForStmt(stmt);
            };
        }

        private static Action<Statement> CheckWhileStmt(Action<Expression> test, Action<Statement> body, Action<Statement> _else = null) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(WhileStatement));
                var whileStmt = (WhileStatement)stmt;

                test(whileStmt.Expression);
                body(whileStmt.Body);
                if (_else != null) {
                    _else(whileStmt.Else.Body);
                } else {
                    Assert.IsNull(whileStmt.Else);
                }
            };
        }

        private static Action<Expression> CheckAsExpression(Action<Expression> expression, Action<Expression> name) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(AsExpression));
                var ae = (AsExpression)expr;

                expression(ae.Expression);
                name(ae.NameExpression);
            };
        }

        private static Action<CompoundStatement> CheckHandler(Action<Expression> test, Action<Statement> body) {
            return stmt => {
                Assert.AreEqual(TokenKind.KeywordExcept, stmt.Kind);

                if (test != null) {
                    test(stmt.Expression);
                } else {
                    Assert.IsNull(stmt.Expression);
                }

                body(stmt.Body);
            };
        }

        private static Action<CompoundStatement> CheckElse(Action<Statement> body) {
            return stmt => {
                Assert.AreEqual(TokenKind.KeywordElse, stmt.Kind);
                body(stmt.Body);
            };
        }

        private static Action<CompoundStatement> CheckFinally(Action<Expression> test, Action<Expression> target, Action<Statement> body) {
            return stmt => {
                Assert.AreEqual(TokenKind.KeywordFinally, stmt.Kind);
                body(stmt.Body);
            };
        }

        private static Action<Statement> CheckTryStmt(Action<Statement> body, params Action<CompoundStatement>[] handlers) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(TryStatement));
                var tryStmt = (TryStatement)stmt;

                body(tryStmt.Body);

                Assert.AreEqual(handlers.Length, tryStmt.Handlers.Count);
                for (int i = 0; i < handlers.Length; i++) {
                    handlers[i](tryStmt.Handlers[i]);
                }
            };
        }

        private static Action<Statement> CheckRaiseStmt(Action<Expression> exceptionType = null, Action<Expression> exceptionValue = null, Action<Expression> traceBack = null, Action<Expression> cause = null) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(RaiseStatement));
                var raiseStmt = (RaiseStatement)stmt;

                if (exceptionType != null) {
                    exceptionType(raiseStmt.Type);
                } else {
                    EmptyExpr(raiseStmt.Type);
                }

                if (exceptionValue != null) {
                    exceptionValue(raiseStmt.Value);
                } else {
                    Assert.IsNull(raiseStmt.Value);
                }

                if (traceBack != null) {
                    traceBack(raiseStmt.Traceback);
                } else {
                    Assert.IsNull(raiseStmt.Traceback);
                }

            };
        }

        private static Action<Statement> CheckPrintStmt(Action<Expression>[] expressions, Action<Expression> destination = null, bool trailingComma = false) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(PrintStatement));
                var printStmt = (PrintStatement)stmt;

                Assert.AreEqual(trailingComma, printStmt.TrailingComma);

                if (expressions != null) {
                    int actualLength = printStmt.Expressions?.Count ?? 0;
                    if (printStmt.TrailingComma) {
                        actualLength -= 1;
                    }
                    Assert.AreEqual(expressions.Length, actualLength);

                    for (int i = 0; i < actualLength; i++) {
                        expressions[i](printStmt.Expressions[i]);
                    }
                }

                if (destination != null) {
                    destination(printStmt.Destination);
                } else {
                    Assert.IsNull(printStmt.Destination);
                }
            };
        }


        private static Action<Statement> CheckAssertStmt(Action<Expression> test, Action<Expression> message = null) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(AssertStatement));
                var assertStmt = (AssertStatement)stmt;

                test(assertStmt.Test);


                if (message != null) {
                    message(assertStmt.Message);
                } else {
                    Assert.IsNull(assertStmt.Message);
                }
            };
        }

        private static Action<CompoundStatement> CheckIf(Action<Expression> expectedTest, Action<Statement> body) {
            return test => {
                expectedTest(test.Expression);
                body(test.Body);
            };
        }

        private static Action<Statement> CheckIfStmt(params Action<CompoundStatement>[] expectedTests) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(IfStatement));
                var ifStmt = (IfStatement)stmt;
                var tests = ifStmt.Tests;

                Assert.AreEqual(expectedTests.Length, tests.Count);
                for (int i = 0; i < expectedTests.Length; i++) {
                    expectedTests[i](tests[i]);
                }
            };
        }

        private static Action<Expression> CheckConditionalExpression(Action<Expression> trueExpression, Action<Expression> test, Action<Expression> falseExpression) {
            return expr => {
                Assert.AreEqual(typeof(ConditionalExpression), expr.GetType(), "Not a Conditional Expression");
                var condExpr = (ConditionalExpression)expr;

                test(condExpr.Expression);
                trueExpression(condExpr.TrueExpression);
                falseExpression(condExpr.FalseExpression);
            };
        }

        private static Action<Statement> CheckFromImport(string fromName, string[] names, string[] asNames = null) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(FromImportStatement));
                var fiStmt = (FromImportStatement)stmt;

                Assert.AreEqual(fromName, fiStmt.Root.MakeString());
                Assert.AreEqual(names.Length, fiStmt.Names?.Count ?? 0);
                for (int i = 0; i < names.Length; i++) {
                    Assert.AreEqual(names[i], FromImportStatement.GetImportName(fiStmt.Names[i]) ?? "");
                    if (asNames == null) {
                        Assert.AreEqual(names[i], FromImportStatement.GetAsName(fiStmt.Names[i]) ?? "");
                    } else {
                        Assert.AreEqual(asNames[i], FromImportStatement.GetAsName(fiStmt.Names[i]));
                    }
                }
            };
        }

        private static Action<Statement> CheckImport(string[] names, string[] asNames = null) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(ImportStatement));
                var fiStmt = (ImportStatement)stmt;

                Assert.AreEqual(names.Length, fiStmt.Names.Count);
                for (int i = 0; i < names.Length; i++) {
                    Assert.AreEqual(names[i], ImportStatement.GetImportName(fiStmt.Names[i]));
                    Assert.AreEqual(asNames?[i] ?? names[i], ImportStatement.GetAsName(fiStmt.Names[i]));
                }
            };
        }

        private static Action<Statement> CheckExprStmt(Action<Expression> expr) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(ExpressionStatement));
                ExpressionStatement exprStmt = (ExpressionStatement)stmt;
                expr(exprStmt.Expression);
            };
        }

        private static Action<Statement> CheckConstantStmt(object value) {
            return CheckExprStmt(CheckConstant(value));
        }

        private static Action<Statement> CheckConstantStmtAndRepr(object value, string repr, PythonLanguageVersion ver) {
            return CheckExprStmt(CheckConstant(value, repr, ver));
        }

        private static Action<Statement> CheckLambdaStmt(Action<Parameter>[] args, Action<Expression> body) {
            return CheckExprStmt(CheckLambda(args, body));
        }

        private static Action<Expression> CheckLambda(Action<Parameter>[] args, Action<Expression> body) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(LambdaExpression));

                var lambda = (LambdaExpression)expr;
                body(lambda.Expression);
            };
        }

        private static Action<Statement> CheckReturnStmt(Action<Expression> retVal = null) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(ReturnStatement));
                var retStmt = (ReturnStatement)stmt;

                if (retVal != null) {
                    retVal(retStmt.Expression);
                } else {
                    Assert.IsInstanceOfType(retStmt.Expression, typeof(EmptyExpression));
                }
            };
        }

        private static Action<Statement> CheckFuncDef(string name, Action<Parameter>[] args, Action<Statement> body, Action<Expression>[] decorators = null, Action<Expression> returnAnnotation = null, bool isAsync = false) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(FunctionDefinition));
                var funcDef = (FunctionDefinition)stmt;

                if (name != null) {
                    Assert.AreEqual(name, funcDef.Name);
                }

                if (decorators != null) {
                    int i = 0;
                    foreach (var d in funcDef.Decorators.MaybeEnumerate().OfType<DecoratorStatement>()) {
                        Assert.IsInstanceOfType(d, typeof(DecoratorStatement));
                        decorators[i++](d.Expression);
                    }
                }

                Assert.AreEqual(isAsync, funcDef.IsAsync);

                Assert.AreEqual(args.Length, funcDef.Parameters.Count);
                for (int i = 0; i < args.Length; i++) {
                    args[i](funcDef.Parameters[i]);
                }

                body(funcDef.Body);

                if (returnAnnotation != null) {
                    returnAnnotation(funcDef.ReturnAnnotation);
                } else {
                    Assert.AreEqual(null, funcDef.ReturnAnnotation);
                }
            };
        }

        private static Action<Statement> CheckClassDef(string name, Action<Statement> body, Action<Arg>[] bases = null, Action<Expression>[] decorators = null) {
            return CheckClassDef(n => {
                Assert.AreEqual(name ?? "", n?.Name ?? "");
            }, body, bases, decorators);
        }

        private static Action<Statement> CheckClassDef(Action<NameExpression> name, Action<Statement> body, Action<Arg>[] bases = null, Action<Expression>[] decorators = null) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(ClassDefinition));
                var classDef = (ClassDefinition)stmt;

                if (name != null) {
                    name(classDef.NameExpression);
                }

                if (decorators != null) {
                    int i = 0;
                    foreach (var d in classDef.Decorators.MaybeEnumerate().OfType<DecoratorStatement>()) {
                        Assert.IsInstanceOfType(d, typeof(DecoratorStatement));
                        decorators[i++](d.Expression);
                    }
                }

                if (bases != null) {
                    Assert.AreEqual(bases.Length, classDef.Bases?.Count ?? 0);
                    for (int i = 0; i < bases.Length; i++) {
                        bases[i](classDef.Bases[i]);
                    }
                } else {
                    Assert.AreEqual(0, classDef.Bases?.Count ?? 0);
                }

                body(classDef.Body);
            };
        }


        private static Action<Parameter> CheckParameter(string name, ParameterKind kind = ParameterKind.Normal, Action<Expression> defaultValue = null, Action<Expression> annotation = null) {
            return param => {
                if (name == null) {
                    Assert.IsTrue(string.IsNullOrEmpty(param.Name), "Expected null/empty, not '" + param.Name + "'");
                } else {
                    Assert.AreEqual(name, param.Name);
                }
                Assert.AreEqual(kind, param.Kind);

                if (defaultValue != null) {
                    defaultValue(param.DefaultValue);
                } else {
                    Assert.AreEqual(null, param.DefaultValue);
                }

                if (annotation != null) {
                    annotation(param.Annotation);
                } else {
                    Assert.AreEqual(null, param.Annotation);
                }
            };
        }

        private static Action<Parameter> CheckSublistParameter(params string[] names) {
            return param => {
                Assert.IsInstanceOfType(param, typeof(Parameter));
                var sublistParam = (Parameter)param;
                Assert.IsTrue(sublistParam.IsSublist);

                Assert.AreEqual(names.Length, sublistParam.Sublist.Items.Count);
                for (int i = 0; i < names.Length; i++) {
                    Assert.AreEqual(names[i], ((NameExpression)sublistParam.Sublist.Items[i].Expression).Name);
                }
            };
        }

        private static Action<Statement> CheckBinaryStmt(Action<Expression> lhs, PythonOperator op, Action<Expression> rhs) {
            return CheckExprStmt(CheckBinaryExpression(lhs, op, rhs));
        }

        private static Action<Expression> CheckBinaryExpression(Action<Expression> lhs, PythonOperator op, Action<Expression> rhs) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(BinaryExpression));
                BinaryExpression bin = (BinaryExpression)expr;
                Assert.AreEqual(op, bin.Operator);
                lhs(bin.Left);
                rhs(bin.Right);
            };
        }

        private static Action<Statement> CheckUnaryStmt(PythonOperator op, Action<Expression> value) {
            return CheckExprStmt(CheckUnaryExpression(op, value));
        }

        private static Action<Expression> CheckUnaryExpression(PythonOperator op, Action<Expression> value) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(UnaryExpression));
                var unary = (UnaryExpression)expr;
                Assert.AreEqual(op, unary.Operator);
                value(unary.Expression);
            };
        }

        private static Action<Statement> CheckBackquoteStmt(Action<Expression> value) {
            return CheckExprStmt(CheckBackquoteExpr(value));
        }

        private static Action<Expression> CheckBackquoteExpr(Action<Expression> value) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(BackQuoteExpression));
                var bq = (BackQuoteExpression)expr;
                value(bq.Expression);
            };
        }

        private static Action<Expression> CheckAwaitExpression(Action<Expression> value) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(AwaitExpression));
                var await = (AwaitExpression)expr;
                value(await.Expression);
            };
        }
        private static Action<Expression> CheckAndExpression(Action<Expression> lhs, Action<Expression> rhs) {
            return CheckBinaryExpression(lhs, PythonOperator.And, rhs);
        }

        private static Action<Expression> CheckOrExpression(Action<Expression> lhs, Action<Expression> rhs) {
            return CheckBinaryExpression(lhs, PythonOperator.Or, rhs);
        }

        private static Action<Statement> CheckCallStmt(Action<Expression> target, params Action<Arg>[] args) {
            return CheckExprStmt(CheckCallExpression(target, args));
        }

        private static Action<Expression> CheckCallExpression(Action<Expression> target, params Action<Arg>[] args) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(CallExpression));
                var call = (CallExpression)expr;
                target(call.Expression);

                Assert.AreEqual(args.Length, call.Args?.Count ?? 0);
                for (int i = 0; i < args.Length; i++) {
                    args[i](call.Args[i]);
                }
            };
        }

        private static Action<Expression> DictItem(Action<Expression> key, Action<Expression> value) {
            return CheckSlice(key, value);
        }

        private static Action<Expression> CheckSlice(Action<Expression> start, Action<Expression> stop, Action<Expression> step = null) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(SliceExpression));
                var slice = (SliceExpression)expr;

                if (start != null) {
                    start(slice.SliceStart);
                } else {
                    Assert.IsInstanceOfType(slice.SliceStart, typeof(EmptyExpression));
                }

                if (stop != null) {
                    stop(slice.SliceStop);
                } else {
                    Assert.IsInstanceOfType(slice.SliceStop, typeof(EmptyExpression));
                }

                if (step != null) {
                    step(slice.SliceStep);
                } else if (slice.SliceStep != null) {
                    Assert.IsInstanceOfType(slice.SliceStep, typeof(EmptyExpression));
                }
            };
        }

        private static Action<Statement> CheckMemberStmt(Action<Expression> target, string name) {
            return CheckExprStmt(CheckMemberExpr(target, name));
        }

        private static Action<Expression> CheckMemberExpr(Action<Expression> target, string name) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(MemberExpression));
                var member = (MemberExpression)expr;
                Assert.AreEqual(name, member.Name);
                target(member.Expression);
            };
        }

        private static Action<Arg> CheckArg(string name) {
            return expr => {
                Assert.AreEqual(null, expr.Name);
                Assert.AreEqual(typeof(NameExpression), expr.Expression.GetType());
                var nameExpr = (NameExpression)expr.Expression;
                Assert.AreEqual(nameExpr.Name, name);
            };
        }


        private static Action<Arg> CheckNamedArg(string argName, Action<Expression> value) {
            return expr => {
                Assert.AreEqual(argName, expr.Name);
                value(expr.Expression);
            };
        }


        private static Action<Expression> CheckNameExpr(string name) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(NameExpression));
                var nameExpr = (NameExpression)expr;
                Assert.AreEqual(nameExpr.Name, name);
            };
        }

        private static Action<Statement> CheckNameStmt(string name) {
            return CheckExprStmt(CheckNameExpr(name));
        }

        private static Action<Arg> PositionalArg(Action<Expression> value) {
            return arg => {
                Assert.AreEqual(true, String.IsNullOrEmpty(arg.Name));
                value(arg.Expression);
            };
        }

        private static Action<Arg> NamedArg(string name, Action<Expression> value) {
            return arg => {
                Assert.AreEqual(name, arg.Name);
                value(arg.Expression);
            };
        }

        private static Action<Arg> ListArg(Action<Expression> value) {
            return arg => {
                Assert.IsInstanceOfType(arg.Expression, typeof(StarredExpression));
                var starArg = (StarredExpression)arg.Expression;
                Assert.IsTrue(starArg.IsStar);
                value(starArg.Expression);
            };
        }

        private static Action<Arg> DictArg(Action<Expression> value) {
            return arg => {
                Assert.IsInstanceOfType(arg.Expression, typeof(StarredExpression));
                var starArg = (StarredExpression)arg.Expression;
                Assert.IsTrue(starArg.IsDoubleStar);
                value(starArg.Expression);
            };
        }

        private static Action<Statement> CheckIndexStmt(Action<Expression> target, Action<Expression> index) {
            return CheckExprStmt(CheckIndexExpression(target, index));
        }

        private static Action<Expression> CheckIndexExpression(Action<Expression> target, Action<Expression> index) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(IndexExpression));
                var indexExpr = (IndexExpression)expr;
                target(indexExpr.Expression);
                index(indexExpr.Index);
            };
        }

        private static Action<Statement> CheckDictionaryStmt(params Action<SliceExpression>[] items) {
            return CheckExprStmt(CheckDictionaryExpr(items));
        }

        private static Action<Expression> CheckDictionaryExpr(params Action<SliceExpression>[] items) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(DictionaryExpression));
                var dictExpr = (DictionaryExpression)expr;
                Assert.AreEqual(items.Length, dictExpr.Count);

                for (int i = 0; i < dictExpr.Count; i++) {
                    items[i]((SliceExpression)dictExpr.Items[i].Expression);
                }
            };
        }

        private static Action<Statement> CheckTupleStmt(params Action<Expression>[] items) {
            return CheckExprStmt(CheckTupleExpr(items));
        }

        private static Action<Expression> CheckTupleExpr(params Action<Expression>[] items) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(TupleExpression));
                var tupleExpr = (TupleExpression)expr;
                Assert.AreEqual(items.Length, tupleExpr.Items.Count);

                for (int i = 0; i < tupleExpr.Items.Count; i++) {
                    if (items[i] == null) {
                        Assert.IsInstanceOfType(tupleExpr.Items[i].Expression, typeof(EmptyExpression));
                    } else {
                        items[i](tupleExpr.Items[i].Expression);
                    }
                }
            };
        }

        private static Action<Expression> CheckListExpr(params Action<Expression>[] items) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(ListExpression));
                var listExpr = (ListExpression)expr;
                Assert.AreEqual(items.Length, listExpr.Count);

                for (int i = 0; i < listExpr.Count; i++) {
                    items[i](listExpr.Items[i].Expression);
                }
            };
        }

        private static Action<Statement> CheckAssignment(Action<Expression> lhs, Action<Expression> rhs) {
            return CheckAssignment(new[] { lhs }, rhs);
        }

        private static Action<Statement> CheckAssignment(Action<Expression>[] lhs, Action<Expression> rhs) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(AssignmentStatement));
                var assign = (AssignmentStatement)expr;

                Assert.AreEqual(lhs.Length, assign.Targets.Count);
                for (int i = 0; i < lhs.Length; i++) {
                    lhs[i](assign.Targets[i]);
                }
                rhs(assign.Expression);
            };
        }

        private static Action<Expression> CheckErrorExpr() {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(ErrorExpression));
            };
        }

        private static Action<Statement> CheckErrorStmt() {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(ErrorStatement));
            };
        }

        private static Action<Expression> CheckEmptyExpr() {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(EmptyExpression));
            };
        }

        private static Action<Statement> CheckEmptyStmt() {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(EmptyStatement));
            };
        }

        private static Action<Statement> CheckPassStmt() {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(PassStatement));
            };
        }

        private static Action<Statement> CheckBreakStmt() {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(BreakStatement));
            };
        }

        private static Action<Statement> CheckContinueStmt() {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(ContinueStatement));
            };
        }

        private static Action<Statement> CheckAssignment(Action<Expression> lhs, PythonOperator op, Action<Expression> rhs) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(AugmentedAssignStatement));
                var assign = (AugmentedAssignStatement)stmt;

                Assert.AreEqual(assign.Operator, op);

                lhs(assign.Target);
                rhs(assign.Expression);
            };
        }

        private Action<Statement> CheckExecStmt(Action<Expression> code, Action<Expression> globals = null, Action<Expression> locals = null) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(ExecStatement));
                var exec = (ExecStatement)stmt;

                code(exec.Code);
                if (globals != null) {
                    globals(exec.Globals);
                } else {
                    Assert.IsNull(exec.Globals);
                }

                if (locals != null) {
                    locals(exec.Locals);
                } else {
                    Assert.IsNull(exec.Locals);
                }
            };
        }

        private Action<Statement> CheckWithStmt(Action<Expression> withItems, Action<Statement> body) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(WithStatement));
                var with = (WithStatement)stmt;

                if (withItems == null) {
                    if (!(with.Expression == null || with.Expression is EmptyExpression)) {
                        Assert.Fail("Expected null or EmptyExpression Test, not " + with.Expression.ToString());
                    }
                } else {
                    withItems(with.Expression);
                }

                body(with.Body);
            };
        }

        private Action<Statement> CheckAsyncWithStmt(Action<Statement> checkWithStmt) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(WithStatement));
                var withStmt = (WithStatement)stmt;

                Assert.IsTrue(withStmt.IsAsync);

                checkWithStmt(stmt);
            };
        }

        private static Action<Expression> CheckConstant(object value, string expectedRepr = null, PythonLanguageVersion ver = PythonLanguageVersion.V27) {
            return expr => {
                if (value is byte[] || value is string) {
                    Assert.IsInstanceOfType(expr, typeof(StringExpression));
                    var se = (StringExpression)expr;

                    if (value is byte[]) {
                        var b1 = (IReadOnlyList<byte>)value;
                        var b2 = se.ToSimpleByteString();
                        Assert.AreEqual(b1.Count, b2.Count);

                        for (int i = 0; i < b1.Count; i++) {
                            Assert.AreEqual(b1[i], b2[i]);
                        }
                    } else {
                        Assert.AreEqual((string)value, se.ToSimpleString());
                    }

                    if (expectedRepr != null) {
                        Assert.AreEqual(expectedRepr, se.GetConstantRepr(ver), "Reprs do not match");
                    }
                } else {
                    Assert.IsInstanceOfType(expr, typeof(ConstantExpression));
                    var ce = (ConstantExpression)expr;

                    Assert.AreEqual(value, ce.Value, "Values do not match");

                    if (expectedRepr != null) {
                        Assert.AreEqual(expectedRepr, ce.GetConstantRepr(ver), "Reprs do not match");
                    }
                }
            };
        }

        private Action<Statement> CheckDelStmt(params Action<Expression>[] deletes) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(DelStatement));
                var del = (DelStatement)stmt;
                var te = del.Expression as TupleExpression;
                if (te == null) {
                    Assert.AreEqual(deletes.Length, 1);
                    deletes[0](del.Expression);
                } else {
                    Assert.AreEqual(deletes.Length, te.Count);
                    for (int i = 0; i < deletes.Length; i++) {
                        deletes[i](te.Items[i].Expression);
                    }
                }
            };
        }

        private Action<Expression> CheckParenExpr(Action<Expression> value) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(ParenthesisExpression));
                var paren = (ParenthesisExpression)expr;

                value(paren.Expression);
            };
        }

        private Action<Expression> CheckStarExpr(Action<Expression> value) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(StarredExpression));
                var starred = (StarredExpression)expr;

                value(starred.Expression);
            };
        }

        private Action<Statement> CheckGlobal(params string[] names) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(GlobalStatement));
                var global = (GlobalStatement)stmt;

                Assert.AreEqual(names.Length, global.Names.Count);
                for (int i = 0; i < names.Length; i++) {
                    Assert.AreEqual(names[i], global.Names[i].Name);
                }
            };
        }

        private Action<Statement> CheckNonlocal(params string[] names) {
            return stmt => {
                Assert.IsInstanceOfType(stmt, typeof(NonlocalStatement));
                var nonlocal = (NonlocalStatement)stmt;

                Assert.AreEqual(names.Length, nonlocal.Names.Count);
                for (int i = 0; i < names.Length; i++) {
                    Assert.AreEqual(names[i], nonlocal.Names[i].Name);
                }
            };
        }

        private Action<Statement> CheckStrOrBytesStmt(PythonLanguageVersion version, string str) {
            return CheckExprStmt(CheckStrOrBytes(version, str));
        }

        private Action<Expression> CheckStrOrBytes(PythonLanguageVersion version, string str) {
            return expr => {
                if (version.Is2x()) {
                    CheckConstant(ToBytes(str));
                } else {
                    CheckConstant(str);
                }
            };
        }

        private Action<Statement> CheckYieldStmt(Action<Expression> value) {
            return CheckExprStmt(CheckYieldExpr(value));
        }

        private Action<Expression> CheckYieldExpr(Action<Expression> value) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(YieldExpression));
                var yield = (YieldExpression)expr;

                value(yield.Expression);
            };
        }

        private Action<Statement> CheckYieldFromStmt(Action<Expression> value) {
            return CheckExprStmt(CheckYieldFromExpr(value));
        }

        private Action<Expression> CheckYieldFromExpr(Action<Expression> value) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(YieldFromExpression));
                var yield = (YieldFromExpression)expr;

                value(yield.Expression);
            };
        }

        private Action<Expression> CheckListComp(Action<Expression> item, params Action<ComprehensionIterator>[] iterators) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(ListComprehension));
                var listComp = (ListComprehension)expr;

                Assert.AreEqual(iterators.Length, listComp.Iterators.Count);

                item(listComp.Item);
                for (int i = 0; i < iterators.Length; i++) {
                    iterators[i](listComp.Iterators[i]);
                }
            };
        }

        private Action<Expression> CheckGeneratorComp(Action<Expression> item, params Action<ComprehensionIterator>[] iterators) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(GeneratorExpression));
                var listComp = (GeneratorExpression)expr;

                Assert.AreEqual(iterators.Length, listComp.Iterators.Count);

                item(listComp.Item);
                for (int i = 0; i < iterators.Length; i++) {
                    iterators[i](listComp.Iterators[i]);
                }
            };
        }

        private Action<Expression> CheckDictComp(Action<Expression> key, Action<Expression> value, params Action<ComprehensionIterator>[] iterators) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(DictionaryComprehension));
                var dictComp = (DictionaryComprehension)expr;

                Assert.AreEqual(iterators.Length, dictComp.Iterators.Count);

                key(dictComp.Key);
                value(dictComp.Value);

                for (int i = 0; i < iterators.Length; i++) {
                    iterators[i](dictComp.Iterators[i]);
                }
            };
        }

        private Action<Expression> CheckSetComp(Action<Expression> item, params Action<ComprehensionIterator>[] iterators) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(SetComprehension));
                var setComp = (SetComprehension)expr;

                Assert.AreEqual(iterators.Length, setComp.Iterators.Count);

                item(setComp.Item);

                for (int i = 0; i < iterators.Length; i++) {
                    iterators[i](setComp.Iterators[i]);
                }
            };
        }

        private Action<Expression> CheckSetLiteral(params Action<Expression>[] values) {
            return expr => {
                Assert.IsInstanceOfType(expr, typeof(SetExpression));
                var setLiteral = (SetExpression)expr;

                Assert.AreEqual(values.Length, setLiteral.Items.Count);
                for (int i = 0; i < values.Length; i++) {
                    values[i](setLiteral.Items[i].Expression);
                }
            };
        }


        private Action<ComprehensionIterator> CompFor(Action<Expression> lhs, Action<Expression> list) {
            return iter => {
                Assert.IsInstanceOfType(iter, typeof(ComprehensionFor));
                var forIter = (ComprehensionFor)iter;

                lhs(forIter.Left);
                list(forIter.List);
            };
        }

        private Action<ComprehensionIterator> CompIf(Action<Expression> test) {
            return iter => {
                Assert.IsInstanceOfType(iter, typeof(ComprehensionIf));
                var ifIter = (ComprehensionIf)iter;

                test(ifIter.Test);
            };
        }

        private Action<Statement> CheckCommentStatement(string comment, PythonAst tree) {
            return CheckComment(comment, tree, CheckEmptyStmt());
        }

        private Action<T> CheckComment<T>(string comment, PythonAst tree, Action<T> checkNode) where T : Node {
            return node => {
                //Assert.AreEqual(comment, node.GetComment(tree));
                checkNode(node);
            };
        }


        private byte[] ToBytes(string str) {
            byte[] res = new byte[str.Length];
            for (int i = 0; i < str.Length; i++) {
                res[i] = (byte)str[i];
            }
            return res;
        }

        private static Action<Expression> IgnoreExpr() {
            return expr => { };
        }

        private static Action<Statement> IgnoreStmt() {
            return stmt => { };
        }

        private static Action<Parameter>[] NoParameters = new Action<Parameter>[0];

        private static void CollectFiles(string dir, List<string> files, IEnumerable<string> exceptions = null) {
            foreach (string file in Directory.GetFiles(dir)) {
                if (file.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) {
                    files.Add(file);
                }
            }
            foreach (string nestedDir in Directory.GetDirectories(dir)) {
                if (exceptions == null || !exceptions.Contains(Path.GetFileName(nestedDir))) {
                    CollectFiles(nestedDir, files, exceptions);
                }
            }
        }

        #endregion
    }
}
