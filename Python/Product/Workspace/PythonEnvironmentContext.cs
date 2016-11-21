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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Workspace;

namespace Microsoft.PythonTools.Workspace {
    class PythonEnvironmentContext {
        public const string ContextType = "{9545F0EB-133D-4148-9B6F-8312BCA5DBC2}";
        public static readonly Guid ContextTypeGuid = new Guid(ContextType);

        private readonly IWorkspace _workspace;
        private readonly IInterpreterRegistryService _registry;

        public PythonEnvironmentContext(IWorkspace workspace, IInterpreterRegistryService registry) {
            _workspace = workspace;
            _registry = registry;
        }

        public async Task<IReadOnlyList<InterpreterConfiguration>> GetConfigurationsAsync(
            CancellationToken cancellationToken
        ) {
            var res = new List<InterpreterConfiguration>();

            res.AddRange(await GetVirtualEnvironmentsAsync(_workspace, cancellationToken));
            res.AddRange(_registry.Configurations);

            return res;
        }
        
        public static async Task<IReadOnlyList<InterpreterConfiguration>> GetVirtualEnvironmentsAsync(
            IWorkspace workspace,
            CancellationToken cancellationToken
        ) {
            var res = new List<InterpreterConfiguration>();

            var py3Venv = new DirectoryCollector(1);
            //await workspace.GetFindFilesService().FindFilesAsync("pyvenv.cfg", py3Venv, cancellationToken);
            foreach (var d in PathUtils.EnumerateFiles(workspace.Location, "pyvenv.cfg")) {
                py3Venv.Report(d);
            }
            var py2Venv = new DirectoryCollector(2);
            //await workspace.GetFindFilesService().FindFilesAsync("orig-prefix.txt", py2Venv, cancellationToken);
            foreach (var d in PathUtils.EnumerateFiles(workspace.Location, "orig-prefix")) {
                py2Venv.Report(d);
            }

            foreach (var prefix in py3Venv.Concat(py2Venv)) {
                var intName = PathUtils.GetFileOrDirectoryName(prefix);
                if (string.IsNullOrEmpty(intName)) {
                    continue;
                }

                // TODO: Don't just do this
                var interpreter = PathUtils.GetAbsoluteFilePath(prefix, "Scripts\\python.exe");
                if (!File.Exists(interpreter)) {
                    continue;
                }

                res.Add(new InterpreterConfiguration(
                    intName,
                    intName,
                    prefix,
                    interpreter,
                    interpreter,
                    "PYTHONPATH"
                ));
            }

            return res;
        }

        class DirectoryCollector : IProgress<string>, IEnumerable<string> {
            public readonly List<string> Collection = new List<string>();
            private readonly int _removeSegments;

            public DirectoryCollector(int removeSegments) {
                _removeSegments = removeSegments;
            }

            public IEnumerator<string> GetEnumerator() {
                return Collection.GetEnumerator();
            }

            public void Report(string value) {
                if (PathUtils.IsValidPath(value)) {
                    for (int i = _removeSegments; i > 0 && !string.IsNullOrEmpty(value); --i) {
                        value = PathUtils.GetParent(value);
                    }

                    if (!string.IsNullOrEmpty(value)) {
                        Collection.Add(value);
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return Collection.GetEnumerator();
            }
        }
    }
}
