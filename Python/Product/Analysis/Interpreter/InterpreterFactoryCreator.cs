/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

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
