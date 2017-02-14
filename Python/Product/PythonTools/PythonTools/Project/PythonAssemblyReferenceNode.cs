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

using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.PythonTools.Project {
    sealed class PythonAssemblyReferenceNode : AssemblyReferenceNode {
        private bool _failedToAnalyze;

        public PythonAssemblyReferenceNode(PythonProjectNode root, ProjectElement element)
            : base(root, element) {
            AnalyzeReferenceAsync(root.GetAnalyzer())
                .HandleAllExceptions(ProjectMgr.Site, GetType())
                .DoNotWait();
        }

        public PythonAssemblyReferenceNode(PythonProjectNode root, string assemblyPath)
            : base(root, assemblyPath) {
            AnalyzeReferenceAsync(root.GetAnalyzer())
                .HandleAllExceptions(ProjectMgr.Site, GetType())
                .DoNotWait();
        }

        protected override void OnAssemblyReferenceChangedOnDisk(object sender, FileChangedOnDiskEventArgs e) {
            base.OnAssemblyReferenceChangedOnDisk(sender, e);

            ReferenceChangedOnDisk(e)
                .HandleAllExceptions(ProjectMgr.Site, GetType())
                .DoNotWait();
        }

        private async Task ReferenceChangedOnDisk(FileChangedOnDiskEventArgs e) {
            var analyzer = ((PythonProjectNode)ProjectMgr).GetAnalyzer();
            if (analyzer != null && PathUtils.IsSamePath(e.FileName, Url)) {
                if ((e.FileChangeFlag & (_VSFILECHANGEFLAGS.VSFILECHG_Attr | _VSFILECHANGEFLAGS.VSFILECHG_Size | _VSFILECHANGEFLAGS.VSFILECHG_Time | _VSFILECHANGEFLAGS.VSFILECHG_Add)) != 0) {
                    // file was modified, unload and reload the extension module from our database.
                    await analyzer.RemoveReferenceAsync(new ProjectAssemblyReference(AssemblyName, Url));
                    await AnalyzeReferenceAsync(analyzer);
                } else if ((e.FileChangeFlag & _VSFILECHANGEFLAGS.VSFILECHG_Del) != 0) {
                    // file was deleted, unload from our extension database
                    await analyzer.RemoveReferenceAsync(new ProjectAssemblyReference(AssemblyName, Url));
                }
            }
        }

        private async Task AnalyzeReferenceAsync(VsProjectAnalyzer interp) {
            if (interp != null) {
                _failedToAnalyze = false;

                var resp = await interp.AddReferenceAsync(new ProjectAssemblyReference(AssemblyName, Url));
                if (resp == null) {
                    _failedToAnalyze = true;
                }
            }
        }

        protected override bool CanShowDefaultIcon() {
            if (_failedToAnalyze) {
                return false;
            }

            return base.CanShowDefaultIcon();
        }

        public override bool Remove(bool removeFromStorage) {
            if (base.Remove(removeFromStorage)) {
                var interp = ((PythonProjectNode)ProjectMgr).GetAnalyzer();
                if (interp != null) {
                    interp.RemoveReferenceAsync(new ProjectAssemblyReference(AssemblyName, Url)).Wait();
                }
                return true;
            }
            return false;
        }


        class TaskFailureHandler {
            private readonly TaskScheduler _uiScheduler;
            private readonly PythonAssemblyReferenceNode _node;
            public TaskFailureHandler(TaskScheduler uiScheduler, PythonAssemblyReferenceNode refNode) {
                _uiScheduler = uiScheduler;
                _node = refNode;
            }

            public void HandleAddRefFailure(Task task) {
                if (task.Exception != null) {
                    Task.Factory.StartNew(MarkFailed, default(CancellationToken), TaskCreationOptions.None, _uiScheduler);
                }
            }

            public void MarkFailed() {
                _node._failedToAnalyze = true;
            }
        }
    }
}
