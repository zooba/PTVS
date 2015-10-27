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
using System.Reflection;
using Microsoft.PythonTools.Interpreter.Default;
using Microsoft.PythonTools.Parsing;

namespace Microsoft.PythonTools.Interpreter {
    /// <summary>
    /// Provides a factory for creating a Python interpreter factory based on an
    /// executable file and cached completion database.
    /// </summary>
    public static class InterpreterFactoryCreator {
        /// <summary>
        /// Creates a new interpreter factory with the specified options. This
        /// interpreter always includes a cached completion database.
        /// </summary>
        public static PythonInterpreterFactoryWithDatabase CreateInterpreterFactory(InterpreterFactoryCreationOptions options) {
            var ver = options.LanguageVersion ?? new Version(2, 7);
            var description = options.Description ?? string.Format("Unknown Python {0}", ver);
            var prefixPath = options.PrefixPath;
            if (string.IsNullOrEmpty(prefixPath) && !string.IsNullOrEmpty(options.InterpreterPath)) {
                prefixPath = Path.GetDirectoryName(options.InterpreterPath);
            }

            var id = (options.Id == default(Guid)) ? Guid.NewGuid() : options.Id;
            return new CPythonInterpreterFactory(
                id,
                description,
                new InterpreterConfiguration(
                    id.ToString(),
                    prefixPath ?? id.ToString(),
                    prefixPath ?? string.Empty,
                    options.InterpreterPath ?? string.Empty,
                    options.WindowInterpreterPath ?? string.Empty,
                    new string[0],
                    options.PathEnvironmentVariableName ?? "PYTHONPATH",
                    options.Architecture,
                    ver.ToLanguageVersion(),
                    InterpreterUIMode.Normal
                ),
                options.WatchLibraryForNewModules
            );
        }

        /// <summary>
        /// Creates a new interpreter factory with the specified database. This
        /// factory is suitable for analysis, but not execution.
        /// </summary>
        public static PythonInterpreterFactoryWithDatabase CreateAnalysisInterpreterFactory(
            Version languageVersion,
            PythonTypeDatabase database) {
            return new AnalysisOnlyInterpreterFactory(languageVersion, database);
        }

        /// <summary>
        /// Creates a new interpreter factory with the specified database path.
        /// This factory is suitable for analysis, but not execution.
        /// </summary>
        public static PythonInterpreterFactoryWithDatabase CreateAnalysisInterpreterFactory(
            Version languageVersion,
            string description,
            params string[] databasePaths) {
            return new AnalysisOnlyInterpreterFactory(languageVersion, databasePaths);
        }

        /// <summary>
        /// Creates a new interpreter factory with the default database. This
        /// factory is suitable for analysis, but not execution.
        /// </summary>
        public static PythonInterpreterFactoryWithDatabase CreateAnalysisInterpreterFactory(
            Version languageVersion,
            string description = null) {
            return new AnalysisOnlyInterpreterFactory(languageVersion, description);
        }
    }
}
