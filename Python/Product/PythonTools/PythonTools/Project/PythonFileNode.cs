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
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Intellisense;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.PythonTools.Project {

    internal class PythonFileNode : CommonFileNode {
        internal PythonFileNode(CommonProjectNode root, ProjectElement e)
            : base(root, e) { }

        public override string Caption {
            get {
                var res = base.Caption;
                if (res == "__init__.py" && Parent != null) {
                    StringBuilder fullName = new StringBuilder(res);
                    fullName.Append(" (");

                    GetPackageName(this, fullName);

                    fullName.Append(")");
                    res = fullName.ToString();
                }
                return res;
            }
        }

        internal static void GetPackageName(HierarchyNode self, StringBuilder fullName) {
            List<HierarchyNode> nodes = new List<HierarchyNode>();
            var curNode = self.Parent;
            do {
                nodes.Add(curNode);
                curNode = curNode.Parent;
            } while (curNode != null && curNode.FindImmediateChildByName("__init__.py") != null);

            for (int i = nodes.Count - 1; i >= 0; i--) {
                fullName.Append(GetNodeNameForPackage(nodes[i]));
                if (i != 0) {
                    fullName.Append('.');
                }
            }
        }

        private static string GetNodeNameForPackage(HierarchyNode node) {
            var project = node as ProjectNode;
            if (project != null) {
                return PathUtils.GetFileOrDirectoryName(project.ProjectHome);
            } else {
                return node.Caption;
            }
        }

        internal override int ExecCommandOnNode(Guid guidCmdGroup, uint cmd, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
            Debug.Assert(this.ProjectMgr != null, "The Dynamic FileNode has no project manager");
            Utilities.CheckNotNull(this.ProjectMgr);

            if (guidCmdGroup == GuidList.guidPythonToolsCmdSet) {
                switch (cmd) {
                    case CommonConstants.SetAsStartupFileCmdId:
                        // Set the StartupFile project property to the Url of this node
                        ProjectMgr.SetProjectProperty(
                            CommonConstants.StartupFile,
                            PathUtils.GetRelativeFilePath(this.ProjectMgr.ProjectHome, Url)
                        );
                        return VSConstants.S_OK;
                    case CommonConstants.StartDebuggingCmdId:
                    case CommonConstants.StartWithoutDebuggingCmdId:
                        IProjectLauncher starter = ((CommonProjectNode)ProjectMgr).GetLauncher();
                        if (starter != null) {
                            if (!Utilities.SaveDirtyFiles()) {
                                // Abort
                                return VSConstants.E_ABORT;
                            }

                            starter.LaunchFile(this.Url, cmd == CommonConstants.StartDebuggingCmdId);
                        }
                        return VSConstants.S_OK;
                }
            }

            return base.ExecCommandOnNode(guidCmdGroup, cmd, nCmdexecopt, pvaIn, pvaOut);
        }

        internal override int QueryStatusOnNode(Guid guidCmdGroup, uint cmd, IntPtr pCmdText, ref QueryStatusResult result) {
            if (guidCmdGroup == GuidList.guidPythonToolsCmdSet) {
                if (this.ProjectMgr.IsCodeFile(this.Url)) {
                    switch (cmd) {
                        case CommonConstants.SetAsStartupFileCmdId:
                            //We enable "Set as StartUp File" command only on current language code files, 
                            //the file is in project home dir and if the file is not the startup file already.
                            string startupFile = ((CommonProjectNode)ProjectMgr).GetStartupFile();
                            if (IsInProjectHome() && 
                                !PathUtils.IsSamePath(startupFile, Url) &&
                                !IsNonMemberItem) {
                                result |= QueryStatusResult.SUPPORTED | QueryStatusResult.ENABLED;
                            }
                            return VSConstants.S_OK;
                        case CommonConstants.StartDebuggingCmdId:
                        case CommonConstants.StartWithoutDebuggingCmdId:
                            result |= QueryStatusResult.SUPPORTED | QueryStatusResult.ENABLED;
                            return VSConstants.S_OK;
                    }
                }
            }
            return base.QueryStatusOnNode(guidCmdGroup, cmd, pCmdText, ref result);
        }

        private bool IsInProjectHome() {
            HierarchyNode parent = this.Parent;
            while (parent != null) {
                if (parent is CommonSearchPathNode) {
                    return false;
                }
                parent = parent.Parent;
            }
            return true;
        }

        private void TryDelete(string filename) {
            if (!File.Exists(filename)) {
                return;
            }

            var node = ((PythonProjectNode)ProjectMgr).FindNodeByFullPath(filename);
            if (node != null) {
                if (node.IsNonMemberItem) {
                    node.Remove(true);
                }
                return;
            }

            try {
                File.Delete(filename);
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            } catch (SecurityException) {
            }
        }

        public override bool Remove(bool removeFromStorage) {
            var analysis = GetAnalysisEntry();
            if (analysis != null) {
                ((PythonProjectNode)ProjectMgr).GetAnalyzer().UnloadFileAsync(analysis).DoNotWait();
            }

            if (Url.EndsWith(PythonConstants.FileExtension, StringComparison.OrdinalIgnoreCase) && removeFromStorage) {
                TryDelete(Url + "c");
                TryDelete(Url + "o");
            }

            return base.Remove(removeFromStorage);
        }

        public override string GetEditLabel() {
            if (IsLinkFile) {
                // cannot rename link files
                return null;
            }
            // dispatch to base class which doesn't include package name, just filename.
            return base.Caption;
        }

        public override string FileName {
            get {
                return base.Caption;
            }
            set {
                base.FileName = value;
            }
        }

        public AnalysisEntry GetAnalysisEntry() {
            return ((PythonProjectNode)ProjectMgr).GetAnalyzer().GetAnalysisEntryFromPath(Url);
        }

        private void TryRename(string oldFile, string newFile) {
            if (!File.Exists(oldFile) || File.Exists(newFile)) {
                return;
            }

            var node = ((PythonProjectNode)ProjectMgr).FindNodeByFullPath(oldFile);
            if (node != null && !node.IsNonMemberItem) {
                return;
            }

            try {
                File.Move(oldFile, newFile);
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            } catch (SecurityException) {
            }
        }

        internal override FileNode RenameFileNode(string oldFileName, string newFileName) {
            var res = base.RenameFileNode(oldFileName, newFileName);

            if (newFileName.EndsWith(PythonConstants.FileExtension, StringComparison.OrdinalIgnoreCase)) {
                TryRename(oldFileName + "c", newFileName + "c");
                TryRename(oldFileName + "o", newFileName + "o");
            }

            if (res != null) {
                var analyzer = ((PythonProjectNode)this.ProjectMgr).GetAnalyzer();
                var analysis = GetAnalysisEntry();
                if (analysis != null) {
                    analyzer.UnloadFileAsync(analysis).DoNotWait();
                }

                var textBuffer = GetTextBuffer(false);

                BufferParser parser = analysis?.TryGetBufferParser();
                if (parser != null) {
                    analyzer.ReAnalyzeTextBuffers(parser);
                }

            }
            return res;
        }

        internal override int IncludeInProject(bool includeChildren) {
            var analyzer = ((PythonProjectNode)this.ProjectMgr).GetAnalyzer();
            analyzer.AnalyzeFileAsync(Url).DoNotWait();

            return base.IncludeInProject(includeChildren);
        }

        internal override int ExcludeFromProject() {
            var analyzer = ((PythonProjectNode)this.ProjectMgr).GetAnalyzer();
            var analysis = GetAnalysisEntry();
            if (analysis != null) {
                analyzer.UnloadFileAsync(analysis).DoNotWait();
            }

            return base.ExcludeFromProject();
        }

        protected override ImageMoniker CodeFileIconMoniker {
            get { return KnownMonikers.PYFileNode; }
        }

        protected override ImageMoniker StartupCodeFileIconMoniker {
            get { return KnownMonikers.PYFileNode; }
        }
    }
}
