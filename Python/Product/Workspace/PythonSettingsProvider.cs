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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Settings;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.PythonTools.Workspace {
    [Export(typeof(IWorkspaceSettingsProviderFactory))]
    class PythonSettingsProviderFactory : IWorkspaceSettingsProviderFactory {
        public int Priority => 100;

        [Import(typeof(SVsServiceProvider))]
        IServiceProvider _site = null;

        [Import]
        WorkspaceEnvironmentProviderFactory _factoryProvider = null;

        public IWorkspaceSettingsProvider CreateSettingsProvider(IWorkspace workspace) {
            var res = new PythonSettingsProvider(_site, workspace);
            _factoryProvider.AddSettingsProvider(res);
            return res;
        }
    }

    class PythonSettingsProvider : IWorkspaceSettingsProvider {
        internal readonly IServiceProvider _site;
        internal readonly IWorkspace _workspace;

        private readonly ConcurrentDictionary<string, IWorkspaceSettingsSource> _settings;

        public PythonSettingsProvider(IServiceProvider site, IWorkspace workspace) {
            _site = site;
            _workspace = workspace;
            _settings = new ConcurrentDictionary<string, IWorkspaceSettingsSource>(StringComparer.OrdinalIgnoreCase);
        }
        
        public AsyncEvent<WorkspaceSettingsChangedEventArgs> OnWorkspaceSettingsChanged { get; set; }

        public Task OnSettingChanged(string scope, string key) {
            return OnWorkspaceSettingsChanged?.InvokeAsync(this, new WorkspaceSettingsChangedEventArgs(scope, key)) ?? Task.FromResult<object>(null);
        }

        public Task DisposeAsync() {
            return Task.FromResult<object>(null);
        }

        public IWorkspaceSettingsSource GetSingleSettings(string type, string scopePath) {
            if (type.Equals(SettingsTypes.Generic, StringComparison.OrdinalIgnoreCase)) {
                IWorkspaceSettingsSource result;
                if (_settings.TryGetValue(scopePath, out result)) {
                    return result;
                }

                if (PathUtils.IsSameDirectory(scopePath, _workspace.Location)) {
                    return _settings.GetOrAdd(scopePath, p => new PythonWorkspaceSettings(this, p));
                }

                if (File.Exists(Path.Combine(scopePath, "pyvenv.cfg"))) {
                    return _settings.GetOrAdd(scopePath, p => new EnvironmentSettings(this, p));
                }

                return null;
            }

            return null;
        }

        public IEnumerable<EnvironmentSettings> AllEnvironmentSettings => _settings.Values.OfType<EnvironmentSettings>();
    }
}
