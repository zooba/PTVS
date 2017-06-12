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
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Project.Web;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.Project;
using MSBuild = Microsoft.Build.Evaluation;

namespace Microsoft.PythonTools.Project
{
    //Set the projectsTemplatesDirectory to a non-existant path to prevent VS from including the working directory as a valid template path
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [ProvideProjectFactory(typeof(PythonProjectFactory), PythonConstants.LanguageName, "#127", "pyproj", "pyproj", ".\\NullPath", LanguageVsTemplate = PythonConstants.LanguageName)]
    [ProvideObject(typeof(PythonGeneralPropertyPage))]
    [ProvideObject(typeof(PythonWebPropertyPage))]
    [ProvideObject(typeof(PythonDebugPropertyPage))]
    [ProvideObject(typeof(PublishPropertyPage))]
    [ProvideEditorExtension2(typeof(PythonEditorFactory), PythonConstants.FileExtension, 50, ProjectGuid = VSConstants.CLSID.MiscellaneousFilesProject_string, NameResourceID = 3016, EditorNameResourceId = 3004, DefaultName = "module", TemplateDir = "NewFileItems")]
    [ProvideEditorExtension2(typeof(PythonEditorFactory), PythonConstants.FileExtension, 50, __VSPHYSICALVIEWATTRIBUTES.PVA_SupportsPreview, "*:1", ProjectGuid = PythonConstants.ProjectFactoryGuid, NameResourceID = 3016, EditorNameResourceId = 3004, DefaultName = "module", TemplateDir = ".\\NullPath")]
    [ProvideEditorExtension2(typeof(PythonEditorFactory), PythonConstants.WindowsFileExtension, 60, __VSPHYSICALVIEWATTRIBUTES.PVA_SupportsPreview, null, ProjectGuid = PythonConstants.ProjectFactoryGuid, NameResourceID = 3016, EditorNameResourceId = 3004, DefaultName = "module", TemplateDir = ".\\NullPath")]
    [ProvideEditorExtension2(typeof(PythonEditorFactoryPromptForEncoding), PythonConstants.FileExtension, 50, __VSPHYSICALVIEWATTRIBUTES.PVA_SupportsPreview, ProjectGuid = PythonConstants.ProjectFactoryGuid, NameResourceID = 3016, EditorNameResourceId = 3015, LinkedEditorGuid = PythonConstants.EditorFactoryGuid, TemplateDir = ".\\NullPath")]
    [ProvideFileFilter(PythonConstants.ProjectFactoryGuid, "/1", "#128", 100)]
    [ProvideEditorLogicalView(typeof(PythonEditorFactory), VSConstants.LOGVIEWID.TextView_string)]
    [ProvideEditorLogicalView(typeof(PythonEditorFactory), VSConstants.LOGVIEWID.Designer_string)]
    [ProvideEditorLogicalView(typeof(PythonEditorFactory), VSConstants.LOGVIEWID.Code_string)]
    [ProvideEditorLogicalView(typeof(PythonEditorFactory), VSConstants.LOGVIEWID.Debugging_string)]
    [ProvideEditorLogicalView(typeof(PythonEditorFactoryPromptForEncoding), VSConstants.LOGVIEWID.TextView_string)]
    [ProvideEditorLogicalView(typeof(PythonEditorFactoryPromptForEncoding), VSConstants.LOGVIEWID.Designer_string)]
    [ProvideEditorLogicalView(typeof(PythonEditorFactoryPromptForEncoding), VSConstants.LOGVIEWID.Code_string)]
    [ProvideEditorLogicalView(typeof(PythonEditorFactoryPromptForEncoding), VSConstants.LOGVIEWID.Debugging_string)]
    
    [ProvideObject(typeof(PythonWebProject))]
    [ProvideProjectFactory(typeof(PythonWebProjectFactory), PythonConstants.LanguageName, "#127", "pyproj", "pyproj", ".\\NullPath", LanguageVsTemplate = PythonConstants.LanguageName)]
    [ProvideFileFilter(PythonConstants.WebProjectFactoryGuid, "/1", "#128", 100)]
    [ProvideLanguageTemplates("{349C5851-65DF-11DA-9384-00065B846F21}", "Python", PythonConstants.ProjectSystemPackageGuid, "Web", "Python Application Project Templates", "{888888a0-9f3d-457c-b088-3a5042f75d52}", ".py", "Python", "{9AF89C0F-85F6-4A20-9023-5D15D912F3B1}")]
    
    [Guid(PythonConstants.ProjectSystemPackageGuid)]
    [DeveloperActivity("Python", typeof(PythonProjectPackage))]
    public class PythonProjectPackage : CommonProjectPackage {
        protected override void Initialize() {
            // The variable is inherited by MSBuild processes and is used to resolve test target
            // files.
            var installPath = PathUtils.GetParent(PythonToolsInstallPath.GetFile("Microsoft.PythonTools.dll", GetType().Assembly));
            string rootDir;
            if (!((IServiceProvider)this).TryGetShellProperty((__VSSPROPID)__VSSPROPID2.VSSPROPID_InstallRootDir, out rootDir) ||
                !PathUtils.IsSubpathOf(rootDir, installPath)) {
                MSBuild.ProjectCollection.GlobalProjectCollection.SetGlobalProperty("_PythonToolsPath", installPath);
                Environment.SetEnvironmentVariable("_PythonToolsPath", installPath);

                // Also find and set the UWP tools path
                var uwp = PathUtils.FindFile(PathUtils.GetParent(PathUtils.GetParent(installPath)), "Microsoft.PythonTools.Uwp.targets", depthLimit: 3);
                if (!string.IsNullOrEmpty(uwp)) {
                    uwp = PathUtils.GetParent(uwp);
                    MSBuild.ProjectCollection.GlobalProjectCollection.SetGlobalProperty("_PythonUwpToolsPath", uwp);
                    Environment.SetEnvironmentVariable("_PythonUwpToolsPath", installPath);
                }
            }

            base.Initialize();
            RegisterProjectFactory(new PythonWebProjectFactory(this));
        }

        public override ProjectFactory CreateProjectFactory() {
            return new PythonProjectFactory(this);
        }

        public override CommonEditorFactory CreateEditorFactory() {
            return new PythonEditorFactory(this);
        }

        public override CommonEditorFactory CreateEditorFactoryPromptForEncoding() {
            return new PythonEditorFactoryPromptForEncoding(this);
        }

        /// <summary>
        /// This method is called to get the icon that will be displayed in the
        /// Help About dialog when this package is selected.
        /// </summary>
        /// <returns>The resource id corresponding to the icon to display on the Help About dialog</returns>
        public override uint GetIconIdForAboutBox() {
            return PythonConstants.IconIdForAboutBox;
        }
        /// <summary>
        /// This method is called during Devenv /Setup to get the bitmap to
        /// display on the splash screen for this package.
        /// </summary>
        /// <returns>The resource id corresponding to the bitmap to display on the splash screen</returns>
        public override uint GetIconIdForSplashScreen() {
            return PythonConstants.IconIfForSplashScreen;
        }
        /// <summary>
        /// This methods provides the product official name, it will be
        /// displayed in the help about dialog.
        /// </summary>
        public override string GetProductName() {
            return PythonConstants.LanguageName;
        }

        /// <summary>
        /// This methods provides the product description, it will be
        /// displayed in the help about dialog.
        /// </summary>
        public override string GetProductDescription() {
            return PythonConstants.LanguageName;
            //return Resources.ProductDescription;
        }
        /// <summary>
        /// This methods provides the product version, it will be
        /// displayed in the help about dialog.
        /// </summary>
        public override string GetProductVersion() {
            return this.GetType().Assembly.GetName().Version.ToString();
        }

        public static void ReadProjectFile(
            IServiceProvider site,
            string projectPath,
            out LaunchConfiguration config,
            out IReadOnlyDictionary<string, IReadOnlyList<string>> items
        ) {
            using (var coll = new MSBuild.ProjectCollection()) {
                var project = coll.LoadProject(projectPath);

                InterpreterConfiguration intConfig = null;

                var interpId = project.GetPropertyValue(MSBuildConstants.InterpreterIdProperty);
                if (!string.IsNullOrEmpty(interpId)) {
                    intConfig = ReadInterpreterFromProject(project, projectPath, interpId) ??
                        site.GetComponentModel().GetService<IInterpreterRegistryService>().FindConfiguration(interpId);
                }

                if (intConfig == null) {
                    intConfig = site.GetPythonToolsService().DefaultAnalyzer.InterpreterFactory.Configuration;
                }

                if (intConfig == null) {
                    config = null;
                } else {
                    config = new LaunchConfiguration(intConfig) {
                        // TODO: Fill out project settings
                    };
                }

                items = new Dictionary<string, IReadOnlyList<string>>();
            }
        }

        private static InterpreterConfiguration ReadInterpreterFromProject(
            MSBuild.Project project,
            string projectPath,
            string interpreterId
        ) {
            var relId = MSBuildProjectInterpreterFactoryProvider.GetProjectRelativeId(projectPath, interpreterId);
            if (string.IsNullOrEmpty(relId)) {
                return null;
            }

            var item = project.AllEvaluatedItems
                .FirstOrDefault(i => {
                    return i.ItemType.Equals(MSBuildConstants.InterpreterItem, StringComparison.InvariantCultureIgnoreCase) &&
                        i.EvaluatedInclude.Equals(relId, StringComparison.OrdinalIgnoreCase);
                });

            var projectHome = project.DirectoryPath;
            var relHome = project.GetPropertyValue(CommonConstants.ProjectHome);
            if (PathUtils.IsValidPath(relHome)) {
                projectHome = PathUtils.GetAbsoluteDirectoryPath(projectHome, relHome);
            }

            if (item != null) {
                Version version = null;
                Version.TryParse(item.GetMetadataValue(MSBuildConstants.VersionKey), out version);
                return new InterpreterConfiguration(
                    interpreterId,
                    item.GetMetadataValue(MSBuildConstants.DescriptionKey),
                    PathUtils.GetAbsoluteDirectoryPath(projectHome, relId),
                    item.GetMetadataValue(MSBuildConstants.InterpreterPathKey),
                    item.GetMetadataValue(MSBuildConstants.WindowsPathKey),
                    item.GetMetadataValue(MSBuildConstants.PathEnvVarKey),
                    InterpreterArchitecture.TryParse(item.GetMetadataValue(MSBuildConstants.ArchitectureKey)),
                    version,
                    InterpreterUIMode.CannotBeAutoDefault | InterpreterUIMode.CannotBeConfigured | InterpreterUIMode.CannotBeDefault
                );
            }

            return null;
        }
    }
}
