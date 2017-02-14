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
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using IServiceProvider = System.IServiceProvider;
using Microsoft.PythonTools.Uwp.Interpreter;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell.Flavor;
using Microsoft.VisualStudio.Shell.Interop;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Uwp.Project {
    [Guid("27BB1268-135A-4409-914F-7AA64AD8195D")]
    partial class PythonUwpProject :
        FlavoredProjectBase,
        IOleCommandTarget,
        IVsProjectFlavorCfgProvider,
        IVsProject,
        IVsFilterAddProjectItemDlg {
        private PythonUwpPackage _package;
        internal IVsProject _innerProject;
        internal IVsProject3 _innerProject3;
        private IVsProjectFlavorCfgProvider _innerVsProjectFlavorCfgProvider;
        private static readonly Guid PythonProjectGuid = new Guid(PythonConstants.ProjectFactoryGuid);
        private IOleCommandTarget _menuService;
        private FileSystemWatcher _sitePackageWatcher;
        private readonly TaskScheduler _scheduler;
        private readonly TaskFactory _factory;

        public PythonUwpProject() {
            _scheduler = TaskScheduler.FromCurrentSynchronizationContext();
            _factory = new TaskFactory(_scheduler);
        }

        internal PythonUwpPackage Package {
            get { return _package; }
            set {
                Debug.Assert(_package == null);
                if (_package != null) {
                    throw new InvalidOperationException(Resources.PackageMustOnlyBeSetOnce);
                }
                _package = value;
            }
        }

        #region IVsAggregatableProject

        /// <summary>
        /// Do the initialization here (such as loading flavor specific
        /// information from the project)
        /// </summary>
        protected override void InitializeForOuter(string fileName, string location, string name, uint flags, ref Guid guidProject, out bool cancel) {
            var pythonProject = this.GetProject().GetPythonProject();
            var msbuildProject = pythonProject.GetMSBuildProjectInstance();
            msbuildProject.Build("CreatePythonUwpIoTPythonEnv", null);

            var sitePackagesDir = Path.Combine(
                pythonProject.ProjectDirectory,
                PythonUwpConstants.InterpreterRelativePath,
                PythonUwpConstants.InterpreterLibPath,
                PythonUwpConstants.InterpreterSitePackagesPath);

            try {
                var sitePackageDirInfo = new DirectoryInfo(sitePackagesDir);
                if (sitePackageDirInfo.Exists) {
                    _sitePackageWatcher = new FileSystemWatcher {
                        IncludeSubdirectories = true,
                        Path = sitePackagesDir,
                    };

                    _sitePackageWatcher.Created += SitePackageWatcher_Changed;
                    _sitePackageWatcher.Changed += SitePackageWatcher_Changed;
                    _sitePackageWatcher.Deleted += SitePackageWatcher_Changed;
                    _sitePackageWatcher.Renamed += SitePackageWatcher_Changed;
                    _sitePackageWatcher.EnableRaisingEvents = true;
                }
            } catch (PathTooLongException) {
            }

            base.InitializeForOuter(fileName, location, name, flags, ref guidProject, out cancel);
        }

        #endregion

        private void SitePackageWatcher_Changed(object sender, System.IO.FileSystemEventArgs e) {
            // Run on the UI thread
            _factory.StartNew(() => {
                var bps = this._innerProject as IVsBuildPropertyStorage;
                if (bps != null) {
                    bps.SetPropertyValue("SitePackageChangedTime", null, (uint)_PersistStorageType.PST_PROJECT_FILE, DateTime.Now.ToString());
                }
            });
        }

        protected override void Close() {
            base.Close();

            if (_sitePackageWatcher != null) {
                _sitePackageWatcher.Dispose();
                _sitePackageWatcher = null;
            }
        }

        protected override int QueryStatusCommand(uint itemid, ref Guid pguidCmdGroup, uint cCmds, VisualStudio.OLE.Interop.OLECMD[] prgCmds, IntPtr pCmdText) {
            if (pguidCmdGroup == GuidList.guidOfficeSharePointCmdSet) {
                for (int i = 0; i < prgCmds.Length; i++) {
                    // Report it as supported so that it's not routed any
                    // further, but disable it and make it invisible.
                    prgCmds[i].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_INVISIBLE);
                }
                return VSConstants.S_OK;
            }

            return base.QueryStatusCommand(itemid, ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        protected override void SetInnerProject(IntPtr innerIUnknown) {
            var inner = Marshal.GetObjectForIUnknown(innerIUnknown);

            // The reason why we keep a reference to those is that doing a QI after being
            // aggregated would do the AddRef on the outer object.
            _innerVsProjectFlavorCfgProvider = inner as IVsProjectFlavorCfgProvider;
            _innerProject = inner as IVsProject;
            _innerProject3 = inner as IVsProject3;
            _innerVsHierarchy = inner as IVsHierarchy;

            // Ensure we have a service provider as this is required for menu items to work
            if (this.serviceProvider == null) {
                this.serviceProvider = (IServiceProvider)Package;
            }

            // Now let the base implementation set the inner object
            base.SetInnerProject(innerIUnknown);

            // Get access to the menu service used by FlavoredProjectBase. We
            // need to forward IOleCommandTarget functions to this object, since
            // we override the FlavoredProjectBase implementation with no way to
            // call it directory.
            // (This must run after we called base.SetInnerProject)
            _menuService = (IOleCommandTarget)((IServiceProvider)this).GetService(typeof(IMenuCommandService));
            if (_menuService == null) {
                throw new InvalidOperationException(Resources.CannotInitializeUwpProjectException);
            }
        }

        protected override int GetProperty(uint itemId, int propId, out object property) {
            switch ((__VSHPROPID2)propId) {
            case __VSHPROPID2.VSHPROPID_CfgPropertyPagesCLSIDList:
                {
                    var res = base.GetProperty(itemId, propId, out property);
                    if (ErrorHandler.Succeeded(res)) {
                        var guids = GetGuidsFromList(property as string);
                        guids.RemoveAll(g => CfgSpecificPropertyPagesToRemove.Contains(g));
                        guids.AddRange(CfgSpecificPropertyPagesToAdd);
                        property = MakeListFromGuids(guids);
                    }
                    return res;
                }
            case __VSHPROPID2.VSHPROPID_PropertyPagesCLSIDList:
                {
                    var res = base.GetProperty(itemId, propId, out property);
                    if (ErrorHandler.Succeeded(res)) {
                        var guids = GetGuidsFromList(property as string);
                        guids.RemoveAll(g => PropertyPagesToRemove.Contains(g));
                        guids.AddRange(PropertyPagesToAdd);
                        property = MakeListFromGuids(guids);
                    }
                    return res;
                }
            }

            switch ((__VSHPROPID6)propId) {
            case __VSHPROPID6.VSHPROPID_Subcaption:
                {
                    var bps = this._innerProject as IVsBuildPropertyStorage;
                    string descriptor = null;

                    if (bps != null) {
                        var res = bps.GetPropertyValue("TargetOsAndVersion", null, (uint)_PersistStorageType.PST_PROJECT_FILE, out descriptor);
                        property = descriptor;
                        return res;
                    }
                    break;
                }
            }

            return base.GetProperty(itemId, propId, out property);
        }

        private static Guid[] PropertyPagesToAdd = new Guid[0];

        private static Guid[] CfgSpecificPropertyPagesToAdd = new Guid[] {
            new Guid(GuidList.guidUwpPropertyPageString)
        };

        private static HashSet<Guid> PropertyPagesToRemove = new HashSet<Guid> {
            new Guid("{8C0201FE-8ECA-403C-92A3-1BC55F031979}"),   // typeof(DeployPropertyPageComClass)
            new Guid("{ED3B544C-26D8-4348-877B-A1F7BD505ED9}"),   // typeof(DatabaseDeployPropertyPageComClass)
            new Guid("{909D16B3-C8E8-43D1-A2B8-26EA0D4B6B57}"),   // Microsoft.VisualStudio.Web.Application.WebPropertyPage
            new Guid("{379354F2-BBB3-4BA9-AA71-FBE7B0E5EA94}"),   // Microsoft.VisualStudio.Web.Application.SilverlightLinksPage
            new Guid("{A553AD0B-2F9E-4BCE-95B3-9A1F7074BC27}"),   // Package/Publish Web 
            new Guid("{9AB2347D-948D-4CD2-8DBE-F15F0EF78ED3}"),   // Package/Publish SQL 
            new Guid(PythonConstants.DebugPropertyPageGuid),
            new Guid(PythonConstants.GeneralPropertyPageGuid),
            new Guid(PythonConstants.PublishPropertyPageGuid)
        };

        internal static HashSet<Guid> CfgSpecificPropertyPagesToRemove = new HashSet<Guid>(new Guid[] { Guid.Empty });

        private static List<Guid> GetGuidsFromList(string guidList) {
            if (string.IsNullOrEmpty(guidList)) {
                return new List<Guid>();
            }

            Guid value;
            return guidList.Split(';')
                .Select(str => Guid.TryParse(str, out value) ? (Guid?)value : null)
                .Where(g => g.HasValue)
                .Select(g => g.Value)
                .ToList();
        }

        private static string MakeListFromGuids(IEnumerable<Guid> guidList) {
            return string.Join(";", guidList.Select(g => g.ToString("B")));
        }

        internal string RemovePropertyPagesFromList(string propertyPagesList, string[] pagesToRemove) {
            if (pagesToRemove == null || !pagesToRemove.Any()) {
                return propertyPagesList;
            }

            var guidsToRemove = new HashSet<Guid>(
                pagesToRemove.Select(str => { Guid guid; return Guid.TryParse(str, out guid) ? guid : Guid.Empty; })
            );
            guidsToRemove.Add(Guid.Empty);

            return string.Join(
                ";",
                propertyPagesList.Split(';')
                    .Where(str => !string.IsNullOrEmpty(str))
                    .Select(str => { Guid guid; return Guid.TryParse(str, out guid) ? guid : Guid.Empty; })
                    .Except(guidsToRemove)
                    .Select(guid => guid.ToString("B"))
            );
        }

        int IOleCommandTarget.Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
            return _menuService.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        int IOleCommandTarget.QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText) {
            return _menuService.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        #region IVsProjectFlavorCfgProvider Members

        public int CreateProjectFlavorCfg(IVsCfg pBaseProjectCfg, out IVsProjectFlavorCfg ppFlavorCfg) {
            // We're flavored with a Windows Store Application project and our normal
            // project...  But we don't want the web application project to
            // influence our config as that alters our debug launch story.  We
            // control that w/ the web project which is actually just letting
            // the base Python project handle it. So we keep the base Python
            // project config here.
            IVsProjectFlavorCfg uwpCfg;
            ErrorHandler.ThrowOnFailure(
                _innerVsProjectFlavorCfgProvider.CreateProjectFlavorCfg(
                    pBaseProjectCfg,
                    out uwpCfg
                )
            );
            ppFlavorCfg = new PythonUwpProjectConfig(pBaseProjectCfg, uwpCfg);
            return VSConstants.S_OK;
        }

        #endregion

        #region IVsProject Members

        int IVsProject.AddItem(uint itemidLoc, VSADDITEMOPERATION dwAddItemOperation, string pszItemName, uint cFilesToOpen, string[] rgpszFilesToOpen, IntPtr hwndDlgOwner, VSADDRESULT[] pResult) {
            return _innerProject.AddItem(itemidLoc, dwAddItemOperation, pszItemName, cFilesToOpen, rgpszFilesToOpen, hwndDlgOwner, pResult);
        }

        int IVsProject.GenerateUniqueItemName(uint itemidLoc, string pszExt, string pszSuggestedRoot, out string pbstrItemName) {
            return _innerProject.GenerateUniqueItemName(itemidLoc, pszExt, pszSuggestedRoot, out pbstrItemName);
        }

        int IVsProject.GetItemContext(uint itemid, out VisualStudio.OLE.Interop.IServiceProvider ppSP) {
            return _innerProject.GetItemContext(itemid, out ppSP);
        }

        int IVsProject.GetMkDocument(uint itemid, out string pbstrMkDocument) {
            return _innerProject.GetMkDocument(itemid, out pbstrMkDocument);
        }

        int IVsProject.IsDocumentInProject(string pszMkDocument, out int pfFound, VSDOCUMENTPRIORITY[] pdwPriority, out uint pitemid) {
            return _innerProject.IsDocumentInProject(pszMkDocument, out pfFound, pdwPriority, out pitemid);
        }

        int IVsProject.OpenItem(uint itemid, ref Guid rguidLogicalView, IntPtr punkDocDataExisting, out IVsWindowFrame ppWindowFrame) {
            return _innerProject.OpenItem(itemid, rguidLogicalView, punkDocDataExisting, out ppWindowFrame);
        }

        #endregion

        #region IVsFilterAddProjectItemDlg Members

        int IVsFilterAddProjectItemDlg.FilterListItemByLocalizedName(ref Guid rguidProjectItemTemplates, string pszLocalizedName, out int pfFilter) {
            pfFilter = 0;
            return VSConstants.S_OK;
        }

        int IVsFilterAddProjectItemDlg.FilterListItemByTemplateFile(ref Guid rguidProjectItemTemplates, string pszTemplateFile, out int pfFilter) {
            pfFilter = 0;
            return VSConstants.S_OK;
        }

        int IVsFilterAddProjectItemDlg.FilterTreeItemByLocalizedName(ref Guid rguidProjectItemTemplates, string pszLocalizedName, out int pfFilter) {
            pfFilter = 0;
            return VSConstants.S_OK;
        }

        int IVsFilterAddProjectItemDlg.FilterTreeItemByTemplateDir(ref Guid rguidProjectItemTemplates, string pszTemplateDir, out int pfFilter) {
            // https://pytools.codeplex.com/workitem/1313
            // ASP.NET will filter some things out, including .css files, which we don't want it to do.
            // So we shut that down by not forwarding this to any inner projects, which is fine, because
            // Python projects don't implement this interface either.
            pfFilter = 0;
            return VSConstants.S_OK;
        }

        #endregion
    }
}
