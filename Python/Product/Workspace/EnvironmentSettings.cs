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
