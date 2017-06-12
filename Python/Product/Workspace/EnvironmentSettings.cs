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
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Workspace.Settings;

namespace Microsoft.PythonTools.Workspace {
    class EnvironmentSettings : IWorkspaceSettingsSource {
        private readonly PythonSettingsProvider _provider;
        private readonly string _prefix;
        private readonly InterpreterConfiguration _configuration;

        private const string ExcludedKey = "ExcludedItems";

        private static readonly string[] ExcludeFilter = new[] {
            ".ptvs/", "Include/", "Lib/", "Scripts/", "*.*"
        };

        public EnvironmentSettings(PythonSettingsProvider provider, string path) {
            _provider = provider;
            _prefix = path;
            var name = PathUtils.TrimEndSeparator(PathUtils.GetRelativeDirectoryPath(
                _provider._workspace.Location, _prefix
            ));
            _configuration = new InterpreterConfiguration(
                "Workspace|" + name,
                name,
                _prefix,
                PathUtils.FindFile(_prefix, "python.exe", firstCheck: new[] { "Scripts" }),
                PathUtils.FindFile(_prefix, "pythonw.exe", firstCheck: new[] { "Scripts" }),
                "PYTHONPATH",
                uiMode: InterpreterUIMode.CannotBeAutoDefault | InterpreterUIMode.CannotBeConfigured | InterpreterUIMode.CannotBeDefault | InterpreterUIMode.SupportsDatabase
            );
        }

        public InterpreterConfiguration Configuration => _configuration;

        public IEnumerable<string> GetKeys() {
            yield return ExcludedKey;
        }

        public WorkspaceSettingsResult GetProperty<T>(string key, out T value, T defaultValue = default(T)) {
            if (ExcludedKey.Equals(key, StringComparison.OrdinalIgnoreCase)) {
                if (typeof(T).IsAssignableFrom(ExcludeFilter.GetType())) {
                    value = (T)(object)ExcludeFilter;
                    return WorkspaceSettingsResult.Success;
                }
                value = defaultValue;
                return WorkspaceSettingsResult.Error;
            }

            value = defaultValue;
            return WorkspaceSettingsResult.Undefined;
        }
    }
}
