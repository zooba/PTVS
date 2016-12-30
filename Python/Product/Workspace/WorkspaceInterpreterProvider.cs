using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Indexing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Workspace {
    [InterpreterFactoryId(FactoryProviderName)]
    [Export(typeof(IPythonInterpreterFactoryProvider))]
    [Export(typeof(WorkspaceInterpreterFactoryProvider))]
    sealed class WorkspaceInterpreterFactoryProvider : IPythonInterpreterFactoryProvider {
        internal const string FactoryProviderName = "Workspace";
        private static readonly Guid WorkspaceInterpreterGuid = new Guid("{39AD2AA0-3D4C-438D-9C6A-B6B6562AB72C}");

        private readonly ConcurrentDictionary<string, InterpreterInfo> _configs;

        public WorkspaceInterpreterFactoryProvider() {
            _configs = new ConcurrentDictionary<string, InterpreterInfo>(StringComparer.OrdinalIgnoreCase);
        }

        public static string GetId(IWorkspace workspace, string prefixPath) {
            return "{0}|{1}|{2}".FormatInvariant(
                FactoryProviderName,
                workspace.Location,
                PathUtils.GetRelativeDirectoryPath(workspace.Location, prefixPath)
            );
        }

        public event EventHandler InterpreterFactoriesChanged;

        private async Task Workspace_IndexPropertyChanged(object sender, PropertyChangedEventArgs<IndexWorkspaceProperties> e) {
            
        }

        private async Task Workspace_IndexScanCompleted(object sender, FileScannerEventArgs e) {
            var index = sender as IIndexWorkspaceService;
            if (index == null) {
                Debug.Fail("Unexpected sender");
                return;
            }

            var changed = false;

            var items = await index.GetFilesDataValuesAsync<Dictionary<string, string>>(WorkspaceInterpreterGuid);
            foreach(var item in items.Values.SelectMany()) {
                Version v;
                var config = new InterpreterConfiguration(
                    item.Value["Id"],
                    item.Value["Description"],
                    item.Value["PrefixPath"],
                    item.Value["ExecutablePath"],
                    item.Value["WindowsExecutablePath"],
                    item.Value["PathEnvironmentVariable"],
                    InterpreterArchitecture.TryParse(item.Value["SysArchitecture"]),
                    Version.TryParse(item.Value["SysVersion"], out v) ? v : new Version()
                );

                var info = new InterpreterInfo(config);
                if (_configs.TryAdd(config.Id, info)) {
                    changed = true;
                } else {
                    // TODO: Figure out whether to update or not
                }
            }

            if (changed) {
                InterpreterFactoriesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private IWorkspace _workspace;

        public void SetWorkspace(IWorkspace workspace) {
            if (_workspace != null) {
                return;
            }
            _workspace = workspace;
            var index = workspace.GetIndexWorkspaceService();
            if (index.State == IndexWorkspaceState.Completed) {
                Workspace_IndexScanCompleted(index, null).DoNotWait();
            } else {
                index.OnFileScannerCompleted += Workspace_IndexScanCompleted;
            }
            //index.OnPropertyChanged += Workspace_IndexPropertyChanged;
        }



        public IEnumerable<InterpreterConfiguration> GetInterpreterConfigurations() {
            return _configs.Values.Select(ii => ii.Config).ToArray();
        }

        public IPythonInterpreterFactory GetInterpreterFactory(string id) {
            InterpreterInfo info;
            if (_configs.TryGetValue(id, out info)) {
                return info.Factory;
            }
            return null;
        }

        public object GetProperty(string id, string propName) {
            return null;
        }

        private sealed class InterpreterInfo : IDisposable {
            private readonly Lazy<IPythonInterpreterFactory> _factory;

            public InterpreterInfo(InterpreterConfiguration config) {
                Config = config;
                Options = new InterpreterFactoryCreationOptions {
                    DatabasePath = PathUtils.GetAbsoluteDirectoryPath(config.PrefixPath, ".ptvs"),
                    PackageManager = BuiltInPackageManagers.Pip,
                    WatchFileSystem = true
                };
                _factory = new Lazy<IPythonInterpreterFactory>(() => {
                    return new PythonInterpreterFactoryWithDatabase(Config, Options);
                });
            }

            public InterpreterConfiguration Config { get; }
            public InterpreterFactoryCreationOptions Options { get; }
            public IPythonInterpreterFactory Factory => _factory.Value;

            public void Dispose() {
                if (_factory.IsValueCreated) {
                    (_factory.Value as IDisposable)?.Dispose();
                }
            }
        }

        public FileDataValue GetConfigurationFromPrefixPath(IWorkspace workspace, string prefixPath) {
            if (!Directory.Exists(prefixPath)) {
                return null;
            }

            if (!PathUtils.IsSubpathOf(workspace.Location, prefixPath)) {
                return null;
            }

            var description = PathUtils.TrimEndSeparator(PathUtils.GetRelativeDirectoryPath(workspace.Location, prefixPath));

            var interpreterPath = PathUtils.FindFile(prefixPath, CPythonInterpreterFactoryConstants.ConsoleExecutable, firstCheck: new[] { "Scripts" });
            if (!File.Exists(interpreterPath)) {
                return null;
            }

            var winterpreterPath = PathUtils.GetAbsoluteFilePath(PathUtils.GetParent(interpreterPath), CPythonInterpreterFactoryConstants.WindowsExecutable);
            if (!File.Exists(winterpreterPath)) {
                winterpreterPath = null;
            }

            return new FileDataValue(
                WorkspaceInterpreterGuid,
                "WorkspaceInterpreter",
                new Dictionary<string, string> {
                    { "Id", GetId(workspace, prefixPath) },
                    { "Description", description },
                    { "PrefixPath", prefixPath },
                    { "ExecutablePath", interpreterPath },
                    { "WindowsExecutablePath", winterpreterPath },
                    { "PathEnvironmentVariable", "PYTHONPATH" },
                    { "SysArchitecture", "32bit" },
                    { "SysVersion", "3.5" }
                }
            );
        }

    }
}
