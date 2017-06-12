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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Settings;

namespace Microsoft.PythonTools.Workspace {
    class PythonWorkspaceSettings : IWorkspaceSettingsSource {
        private readonly PythonSettingsProvider _provider;
        private readonly string _scope;
        private string _interpreterId;

        private const string ExcludedKey = "ExcludedItems";
        private static readonly string[] ExcludeFilter = new[] {
            "*.pyc", "*.pyo", "__pycache__/"
        };

        public const string InterpreterIdKey = "InterpreterId";

        public PythonWorkspaceSettings(PythonSettingsProvider provider, string scope) {
            _provider = provider;
            _scope = scope;

            _interpreterId = _provider._site.GetPythonToolsService().DefaultAnalyzer.InterpreterFactory.Configuration.Id;
        }

        public IEnumerable<string> GetKeys() {
            yield return ExcludedKey;
            yield return InterpreterIdKey;
        }

        public Task SetActiveInterpreter(string interpreterId) {
            _interpreterId = interpreterId;
            return _provider.OnSettingChanged(_scope, InterpreterIdKey);
        }

        public WorkspaceSettingsResult GetProperty<T>(string key, out T value, T defaultValue = default(T)) {
            value = defaultValue;

            if (ExcludedKey.Equals(key, StringComparison.OrdinalIgnoreCase)) {
                if (typeof(T).IsAssignableFrom(ExcludeFilter.GetType())) {
                    value = (T)(object)ExcludeFilter;
                    return WorkspaceSettingsResult.Success;
                }
                return WorkspaceSettingsResult.Error;
            }

            if (InterpreterIdKey.Equals(key, StringComparison.OrdinalIgnoreCase)) {
                if (typeof(string).IsEquivalentTo(typeof(T))) {
                    value = (T)(object)_interpreterId;
                    return WorkspaceSettingsResult.Success;
                }
                return WorkspaceSettingsResult.Error;
            }

            return WorkspaceSettingsResult.Undefined;
        }
    }
}
