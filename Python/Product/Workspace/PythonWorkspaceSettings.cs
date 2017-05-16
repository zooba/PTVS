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
