﻿// Python Tools for Visual Studio
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
using System.ComponentModel.Composition;
using System.Linq;
using System.Web.Script.Serialization;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Interpreter.Ast;
using Microsoft.PythonTools.Projects;

namespace Microsoft.PythonTools.TestAdapter {
    [Export(typeof(IAnalysisExtension))]
    [AnalysisExtensionName(Name)]
    partial class TestAnalyzer : IAnalysisExtension {
        internal const string Name = "ptvs_unittest";
        internal const string GetTestCasesCommand = "testcases";
        
        private PythonAnalyzer _analyzer;

        public string HandleCommand(string commandId, string body) {
            var serializer = new JavaScriptSerializer();
            switch (commandId) {
                case GetTestCasesCommand:
                    IProjectEntry projEntry;
                    IEnumerable<TestCaseInfo> testCases;
                    if (_analyzer.TryGetProjectEntryByPath(body, out projEntry)) {
                        testCases = GetTestCasesFromAnalysis(projEntry);
                    } else {
                        testCases = GetTestCasesFromAst(body);
                    }

                    return serializer.Serialize(testCases.Select(tc => tc.AsDictionary()).ToArray());
            }

            return "";
        }

        public void Register(PythonAnalyzer analyzer) {
            _analyzer = analyzer;
        }

        public static TestCaseInfo[] GetTestCases(string data) {
            var serializer = new JavaScriptSerializer();
            List<TestCaseInfo> tests = new List<TestCaseInfo>();
            foreach (var item in serializer.Deserialize<object[]>(data)) {
                var dict = item as Dictionary<string, object>;
                if (dict == null) {
                    continue;
                }

                object filename, className, methodName, startLine, startColumn, endLine, kind;
                if (dict.TryGetValue(Serialize.Filename, out filename) &&
                    dict.TryGetValue(Serialize.ClassName, out className) &&
                    dict.TryGetValue(Serialize.MethodName, out methodName) &&
                    dict.TryGetValue(Serialize.StartLine, out startLine) &&
                    dict.TryGetValue(Serialize.StartColumn, out startColumn) &&
                    dict.TryGetValue(Serialize.EndLine, out endLine) &&
                    dict.TryGetValue(Serialize.Kind, out kind)) {
                    tests.Add(
                        new TestCaseInfo(
                            filename.ToString(),
                            className.ToString(),
                            methodName.ToString(),
                            ToInt(startLine),
                            ToInt(startColumn),
                            ToInt(endLine)
                        )
                    );
                }
            }
            return tests.ToArray();
        }

        private static int ToInt(object value) {
            if (value is int) {
                return (int)value;
            }
            return 0;
        }

        public class Serialize {
            public const string Filename = "filename";
            public const string ClassName = "className";
            public const string MethodName = "methodName";
            public const string StartLine = "startLine";
            public const string StartColumn = "startColumn";
            public const string EndLine = "endLine";
            public const string Kind = "kind";
        }


        public static IEnumerable<TestCaseInfo> GetTestCasesFromAnalysis(IProjectEntry projEntry) {
            var entry = projEntry as IPythonProjectEntry;
            if (entry == null) {
                yield break;
            }
            var analysis = entry.Analysis;
            if (analysis == null || !entry.IsAnalyzed) {
                yield break;
            }

            foreach (var classValue in GetTestCaseClasses(analysis)) {
                // Check the name of all functions on the class using the
                // analyzer. This will return functions defined on this
                // class and base classes
                foreach (var member in GetTestCaseMembers(analysis, classValue)) {
                    // Find the definition to get the real location of the
                    // member. Otherwise decorators will confuse us.
                    var definition = entry.Analysis
                        .GetVariablesByIndex(classValue.Name + "." + member.Key, 0)
                        .FirstOrDefault(v => v.Type == VariableType.Definition);

                    var location = (definition != null) ?
                        definition.Location :
                        member.Value.SelectMany(m => m.Locations).FirstOrDefault(loc => loc != null);

                    int endLine = location?.EndLine ?? location?.StartLine ?? 0;

                    yield return new TestCaseInfo(
                        classValue.DeclaringModule?.FilePath,
                        classValue.Name,
                        member.Key,
                        location?.StartLine ?? 0,
                        location?.StartColumn ?? 1,
                        endLine
                    );
                }
            }
        }

        private static bool IsTestCaseClass(AnalysisValue cls) {
            return IsTestCaseClass(cls?.PythonType);
        }

        private static bool IsTestCaseClass(IPythonType cls) {
            if (cls == null ||
                cls.DeclaringModule == null) {
                return false;
            }
            var mod = cls.DeclaringModule.Name;
            return (mod == "unittest" || mod.StartsWith("unittest.")) && cls.Name == "TestCase";
        }
        /// <summary>
        /// Get Test Case Members for a class.  If the class has 'test*' tests 
        /// return those.  If there aren't any 'test*' tests return (if one at 
        /// all) the runTest overridden method
        /// </summary>
        private static IEnumerable<KeyValuePair<string, IAnalysisSet>> GetTestCaseMembers(
            ModuleAnalysis analysis,
            AnalysisValue classValue
        ) {
            var methodFunctions = classValue.GetAllMembers(analysis.InterpreterContext)
                .Where(v => v.Value.Any(m => m.MemberType == PythonMemberType.Function || m.MemberType == PythonMemberType.Method));

            var tests = methodFunctions.Where(v => v.Key.StartsWith("test"));
            var runTest = methodFunctions.Where(v => v.Key.Equals("runTest"));

            if (tests.Any()) {
                return tests;
            } else {
                return runTest;
            }
        }

        private static IEnumerable<AnalysisValue> GetTestCaseClasses(ModuleAnalysis analysis) {
            return analysis.GetAllAvailableMembersByIndex(0, GetMemberOptions.ExcludeBuiltins)
                .SelectMany(m => analysis.GetValuesByIndex(m.Name, 0))
                .Where(v => v.MemberType == PythonMemberType.Class)
                .Where(v => v.Mro.SelectMany(v2 => v2).Any(IsTestCaseClass));
        }

        private static IEnumerable<IPythonType> GetTestCaseClasses(IPythonModule module, IModuleContext context) {
            foreach (var name in module.GetMemberNames(context)) {
                var cls = module.GetMember(context, name) as IPythonType;
                if (cls != null) {
                    foreach (var baseCls in cls.Mro.MaybeEnumerate()) {
                        if (baseCls.Name == "TestCase" ||
                            baseCls.Name.StartsWith("unittest.") && baseCls.Name.EndsWith(".TestCase")) {
                            yield return cls;
                        }
                    }
                }
            }
        }

        private static IEnumerable<IPythonFunction> GetTestCaseMembers(IPythonType cls, IModuleContext context) {
            var methodFunctions = cls.GetMemberNames(context).Select(n => cls.GetMember(context, n))
                .OfType<IPythonFunction>()
                .ToArray();

            var tests = methodFunctions.Where(v => v.Name.StartsWith("test"));
            var runTest = methodFunctions.Where(v => v.Name.Equals("runTest"));

            if (tests.Any()) {
                return tests;
            } else {
                return runTest;
            }
        }

        public IEnumerable<TestCaseInfo> GetTestCasesFromAst(string path) {
            IPythonModule module;
            try {
                module = AstPythonModule.FromFile(_analyzer.Interpreter, path, _analyzer.LanguageVersion);
            } catch (Exception ex) when (!ex.IsCriticalException()) {
                return Enumerable.Empty<TestCaseInfo>();
            }

            var ctxt = _analyzer.Interpreter.CreateModuleContext();
            return GetTestCasesFromAst(module, ctxt);
        }

        internal static IEnumerable<TestCaseInfo> GetTestCasesFromAst(IPythonModule module, IModuleContext ctxt) {
            if (module == null) {
                throw new ArgumentNullException(nameof(module));
            }

            foreach (var classValue in GetTestCaseClasses(module, ctxt)) {
                // Check the name of all functions on the class using the
                // analyzer. This will return functions defined on this
                // class and base classes
                foreach (var member in GetTestCaseMembers(classValue, ctxt)) {
                    // Find the definition to get the real location of the
                    // member. Otherwise decorators will confuse us.
                    var location = (member as ILocatedMember)?.Locations?.FirstOrDefault(loc => loc != null);

                    int endLine = location?.EndLine ?? location?.StartLine ?? 0;

                    yield return new TestCaseInfo(
                        (classValue as ILocatedMember)?.Locations.FirstOrDefault()?.FilePath,
                        classValue.Name,
                        member.Name,
                        location?.StartLine ?? 0,
                        location?.StartColumn ?? 1,
                        endLine
                    );
                }
            }
        }
    }
}
