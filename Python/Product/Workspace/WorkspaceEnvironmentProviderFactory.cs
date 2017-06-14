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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Settings;

namespace Microsoft.PythonTools.Workspace {
    [Export(typeof(IPythonInterpreterFactoryProvider))]
    [Export(typeof(WorkspaceEnvironmentProviderFactory))]
    [InterpreterFactoryId(FactoryId)]
    sealed class WorkspaceEnvironmentProviderFactory : IPythonInterpreterFactoryProvider, IDisposable {
        private readonly ConcurrentBag<PythonSettingsProvider> _settingsProviders;
        private readonly ConcurrentDictionary<string, InterpreterInfo> _factories;

        public event EventHandler InterpreterFactoriesChanged;

        public const string FactoryId = "VSWorkspace";

        public WorkspaceEnvironmentProviderFactory() {
            _settingsProviders = new ConcurrentBag<PythonSettingsProvider>();
            _factories = new ConcurrentDictionary<string, InterpreterInfo>();
        }

        public void AddSettingsProvider(PythonSettingsProvider provider) {
            _settingsProviders.Add(provider);
            RefreshFactories();
            provider.OnWorkspaceSettingsChanged += OnSettingsChange;
        }

        private async Task OnSettingsChange(object sender, WorkspaceSettingsChangedEventArgs e) {
            RefreshFactories();
        }

        private void RefreshFactories() {
            bool changed = false;

            var toRemove = new HashSet<string>(_factories.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var env in _settingsProviders.SelectMany(sp => sp.AllEnvironmentSettings)) {
                InterpreterInfo info;
                if (_factories.TryGetValue(env.Configuration.Id, out info)) {
                    toRemove.Remove(env.Configuration.Id);
                    if (info.Configuration != env.Configuration) {
                        info.Dispose();
                        _factories[env.Configuration.Id] = new InterpreterInfo(env.Configuration);
                        changed = true;
                    }
                } else {
                    _factories[env.Configuration.Id] = new InterpreterInfo(env.Configuration);
                    changed = true;
                }
            }
            foreach (var id in toRemove) {
                InterpreterInfo dummy;
                _factories.TryRemove(id, out dummy);
                changed = true;
            }

            if (changed) {
                InterpreterFactoriesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public IEnumerable<InterpreterConfiguration> GetInterpreterConfigurations() {
            return _factories.Select(f => f.Value.Configuration).ToArray();
        }

        public IPythonInterpreterFactory GetInterpreterFactory(string id) {
            InterpreterInfo info;
            if (_factories.TryGetValue(id, out info)) {
                return info.Factory.Value;
            }
            return null;
        }

        public object GetProperty(string id, string propName) {
            // TODO: Identify source
            //if ("Company".Equals(propName) && id.StartsWith("VSWorkspace|")) {
            //    return "";
            //}
            return null;
        }

        public void Dispose() {
            foreach (var info in _factories.Values) {
                info.Dispose();
            }
        }

        private sealed class InterpreterInfo : IDisposable {
            public InterpreterInfo(InterpreterConfiguration config) {
                Configuration = config;
                Factory = new Lazy<IPythonInterpreterFactory>(Create);
            }

            private IPythonInterpreterFactory Create() {
                return InterpreterFactoryCreator.CreateInterpreterFactory(
                    Configuration,
                    new InterpreterFactoryCreationOptions {
                        DatabasePath = PathUtils.GetAbsoluteDirectoryPath(Configuration.PrefixPath, ".ptvs"),
                        PackageManager = BuiltInPackageManagers.Pip,
                        WatchFileSystem = true
                    }
                );
            }

            public void Dispose() {
                if (Factory.IsValueCreated) {
                    (Factory.Value as IDisposable)?.Dispose();
                }
            }

            public InterpreterConfiguration Configuration;
            public readonly Lazy<IPythonInterpreterFactory> Factory;
        }
    }
}
