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
    sealed class WorkspaceEnvironmentProviderFactory : IPythonInterpreterFactoryProvider, IDisposable {
        private readonly ConcurrentBag<PythonSettingsProvider> _settingsProviders;
        private readonly ConcurrentDictionary<string, InterpreterInfo> _factories;

        public event EventHandler InterpreterFactoriesChanged;

        public WorkspaceEnvironmentProviderFactory() {
            _settingsProviders = new ConcurrentBag<PythonSettingsProvider>();
            _factories = new ConcurrentDictionary<string, InterpreterInfo>();
        }

        public void AddSettingsProvider(PythonSettingsProvider provider) {
            _settingsProviders.Add(provider);
            RefreshFactories();
            var evt = provider.OnWorkspaceSettingsChanged;
            if (evt == null) {
                evt = provider.OnWorkspaceSettingsChanged = new AsyncEvent<WorkspaceSettingsChangedEventArgs>();
            }
            evt += OnSettingsChange;
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
            //if ("Company".Equals(propName) && id.StartsWith("Workspace|")) {
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
                return new PythonInterpreterFactoryWithDatabase(
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
