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
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudioTools.Project;
using NativeMethods = Microsoft.VisualStudioTools.Project.NativeMethods;
using OleConstants = Microsoft.VisualStudio.OLE.Interop.Constants;
using VsCommands = Microsoft.VisualStudio.VSConstants.VSStd97CmdID;
using VsMenus = Microsoft.VisualStudioTools.Project.VsMenus;

namespace Microsoft.PythonTools.Project {
    /// <summary>
    /// Represents a package installed in a virtual env as a node in the Solution Explorer.
    /// </summary>
    [ComVisible(true)]
    internal class InterpretersPackageNode : HierarchyNode {
        private static readonly Regex PipFreezeRegex = new Regex(
            "^(?<name>[^=]+)==(?<version>.+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );

        private static readonly IEnumerable<string> CannotUninstall = new[] { "pip", "wsgiref" };
        private readonly bool _canUninstall;
        private readonly string _caption;
        private readonly string _packageName;

        public InterpretersPackageNode(PythonProjectNode project, string name)
            : base(project, new VirtualProjectElement(project)) {
            ExcludeNodeFromScc = true;
            _packageName = name;
            var match = PipFreezeRegex.Match(name);
            if (match.Success) {
                var namePart = match.Groups["name"].Value;
                _caption = string.Format("{0} ({1})", namePart, match.Groups["version"]);
                _canUninstall = !CannotUninstall.Contains(namePart);
            } else {
                _caption = name;
                _canUninstall = false;
            }
        }

        public override int MenuCommandId {
            get { return PythonConstants.EnvironmentPackageMenuId; }
        }

        public override Guid MenuGroupId {
            get { return GuidList.guidPythonToolsCmdSet; }
        }

        public override string Url {
            get { return _packageName; }
        }

        public override Guid ItemTypeGuid {
            get { return PythonConstants.InterpretersPackageItemTypeGuid; }
        }

        public new PythonProjectNode ProjectMgr {
            get {
                return (PythonProjectNode)base.ProjectMgr;
            }
        }

        internal override bool CanDeleteItem(__VSDELETEITEMOPERATION deleteOperation) {
            return _canUninstall && deleteOperation == __VSDELETEITEMOPERATION.DELITEMOP_RemoveFromProject;
        }

        protected internal override void ShowDeleteMessage(IList<HierarchyNode> nodes, __VSDELETEITEMOPERATION action, out bool cancel, out bool useStandardDialog) {
            if (nodes.All(n => n is InterpretersPackageNode) &&
                nodes.Cast<InterpretersPackageNode>().All(n => n.Parent == Parent)) {
                string message;
                if (nodes.Count == 1) {
                    message = Strings.UninstallPackage.FormatUI(
                        Caption,
                        Parent._factory.Description,
                        Parent._factory.Configuration.PrefixPath
                    );
                } else {
                    message = Strings.UninstallPackages.FormatUI(
                        string.Join(Environment.NewLine, nodes.Select(n => n.Caption)),
                        Parent._factory.Description,
                        Parent._factory.Configuration.PrefixPath
                    );
                }
                useStandardDialog = false;
                cancel = VsShellUtilities.ShowMessageBox(
                    ProjectMgr.Site,
                    string.Empty,
                    message,
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OKCANCEL,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST
                ) != NativeMethods.IDOK;
            } else {
                useStandardDialog = false;
                cancel = true;
            }
        }

        public override void Remove(bool removeFromStorage) {
            PythonProjectNode.BeginUninstallPackage(Parent._factory, ProjectMgr.Site, Url, Parent);
        }

        public new InterpretersNode Parent {
            get {
                return (InterpretersNode)base.Parent;
            }
        }

        /// <summary>
        /// Show the name of the package.
        /// </summary>
        public override string Caption {
            get {
                return _caption;
            }
        }

        /// <summary>
        /// Disable inline editing of Caption of a package node
        /// </summary>
        public override string GetEditLabel() {
            return null;
        }

        protected override bool SupportsIconMonikers {
            get { return true; }
        }

        protected override ImageMoniker GetIconMoniker(bool open) {
            return KnownMonikers.PythonPackage;
        }

        /// <summary>
        /// Package node cannot be dragged.
        /// </summary>
        protected internal override string PrepareSelectedNodesForClipBoard() {
            return null;
        }

        /// <summary>
        /// Package node cannot be excluded.
        /// </summary>
        internal override int ExcludeFromProject() {
            return (int)OleConstants.OLECMDERR_E_NOTSUPPORTED;
        }

        internal override int QueryStatusOnNode(Guid cmdGroup, uint cmd, IntPtr pCmdText, ref QueryStatusResult result) {
            if (cmdGroup == VsMenus.guidStandardCommandSet97) {
                switch ((VsCommands)cmd) {
                    case VsCommands.Copy:
                    case VsCommands.Cut:
                        result |= QueryStatusResult.SUPPORTED | QueryStatusResult.INVISIBLE;
                        return VSConstants.S_OK;
                    case VsCommands.Delete:
                        if (!_canUninstall) {
                            // If we can't uninstall the package, still show the
                            // item but disable it. Otherwise, let the default
                            // query handle it, which will display "Remove".
                            result |= QueryStatusResult.SUPPORTED;
                            return VSConstants.S_OK;
                        }
                        break;
                }
            }
            return base.QueryStatusOnNode(cmdGroup, cmd, pCmdText, ref result);
        }

        /// <summary>
        /// Defines whether this node is valid node for painting the package icon.
        /// </summary>
        /// <returns></returns>
        protected override bool CanShowDefaultIcon() {
            return true;
        }

        public override bool CanAddFiles {
            get {
                return false;
            }
        }

        protected override NodeProperties CreatePropertiesObject() {
            return new InterpretersPackageNodeProperties(this);
        }
    }
}
