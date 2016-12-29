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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.XPath;
using Microsoft.Build.Execution;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Commands;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Logging;
using Microsoft.PythonTools.Navigation;
using Microsoft.PythonTools.Projects;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Azure;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.Project;
using IServiceProvider = System.IServiceProvider;
using MessageBox = System.Windows.Forms.MessageBox;
using NativeMethods = Microsoft.VisualStudioTools.Project.NativeMethods;
using Task = System.Threading.Tasks.Task;
using VsCommands2K = Microsoft.VisualStudio.VSConstants.VSStd2KCmdID;
using VsMenus = Microsoft.VisualStudioTools.Project.VsMenus;

namespace Microsoft.PythonTools.Project {
    [Guid(PythonConstants.ProjectNodeGuid)]
    internal class PythonProjectNode :
        CommonProjectNode,
        IPythonProject,
        IAzureRoleProject,
        IProjectInterpreterDbChanged,
        IPythonProjectProvider
    {
        // For files that are analyzed because they were directly or indirectly referenced in the search path, store the information
        // about the directory from the search path that referenced them in IProjectEntry.Properties[_searchPathEntryKey], so that
        // they can be located and removed when that directory is removed from the path.
        private static readonly object _searchPathEntryKey = new { Name = "SearchPathEntry" };

        private object _designerContext;
        private VsProjectAnalyzer _analyzer;
        private readonly HashSet<AnalysisEntry> _warnOnLaunchFiles = new HashSet<AnalysisEntry>();
        private PythonDebugPropertyPage _debugPropPage;
        internal readonly SearchPathManager _searchPaths = new SearchPathManager();
        private CommonSearchPathContainerNode _searchPathContainer;
        private InterpretersContainerNode _interpretersContainer;
        private readonly HashSet<string> _validFactories = new HashSet<string>();
        public IPythonInterpreterFactory _active;

        internal List<CustomCommand> _customCommands;
        private string _customCommandsDisplayLabel;
        private Dictionary<object, Action<object>> _actionsOnClose;
        private readonly PythonProject _pythonProject;

        public PythonProjectNode(IServiceProvider serviceProvider) : base(serviceProvider, null) {
            _searchPaths.Changed += SearchPaths_Changed;

            Type projectNodePropsType = typeof(PythonProjectNodeProperties);
            AddCATIDMapping(projectNodePropsType, projectNodePropsType.GUID);
            ActiveInterpreterChanged += OnActiveInterpreterChanged;
            InterpreterFactoriesChanged += OnInterpreterFactoriesChanged;
            // _active starts as null, so we need to start with this event
            // hooked up.
            InterpreterOptions.DefaultInterpreterChanged += GlobalDefaultInterpreterChanged;
            InterpreterRegistry.InterpretersChanged += OnInterpreterRegistryChanged;
            _pythonProject = new VsPythonProject(this);
        }

        private static KeyValuePair<string, string>[] outputGroupNames = {
                                             // Name                     ItemGroup (MSBuild)
            new KeyValuePair<string, string>("Built",                 "BuiltProjectOutputGroup"),
            new KeyValuePair<string, string>("ContentFiles",          "ContentFilesProjectOutputGroup"),
            new KeyValuePair<string, string>("SourceFiles",           "SourceFilesProjectOutputGroup"),
        };

        protected internal override IList<KeyValuePair<string, string>> GetOutputGroupNames() {
            return outputGroupNames.ToList();
        }

        protected override void NewBuildProject(Build.Evaluation.Project project) {
            base.NewBuildProject(project);

            // Remove old custom commands
            if (_customCommands != null) {
                foreach (var c in _customCommands) {
                    c.Dispose();
                }
            }
            _customCommands = null;

            var contextProvider = Site.GetComponentModel().GetService<VsProjectContextProvider>();
            contextProvider.UpdateProject(this, project);

            // Project has been cleared, so nothing else to do here
            if (project == null) {
                return;
            }

            // collect the valid interpreter factories for this project...
            _validFactories.Clear();
            foreach (var item in project.GetItems(MSBuildConstants.InterpreterItem)) {
                var id = item.GetMetadataValue(MSBuildConstants.IdKey);
                if (!String.IsNullOrWhiteSpace(id)) {
                    _validFactories.Add(MSBuildProjectInterpreterFactoryProvider.GetInterpreterId(BuildProject.FullPath, id));
                }
            }

            try {
                Site.GetPythonToolsService().Logger.LogEvent(PythonLogEvent.VirtualEnvironments, _validFactories.Count);
            } catch (Exception ex) {
                Debug.Fail(ex.ToUnhandledExceptionMessage(GetType()));
            }

            foreach (var item in project.GetItems(MSBuildConstants.InterpreterReferenceItem)) {
                var id = item.EvaluatedInclude;
                if (!String.IsNullOrWhiteSpace(id)) {
                    _validFactories.Add(id);
                }
            }

            // Add any custom commands
            _customCommands = CustomCommand.GetCommands(project, this).ToList();
            _customCommandsDisplayLabel = CustomCommand.GetCommandsDisplayLabel(project, this);
        }

        public IAsyncCommand FindCommand(string canonicalName) {
            return _customCommands.FirstOrDefault(cc => cc.Target == canonicalName);
        }

        public ProjectInstance GetMSBuildProjectInstance() {
            if (CurrentConfig == null) {
                SetCurrentConfiguration();
                if (CurrentConfig == null) {
                    if (BuildProject == null) {
                        return null;
                    }
                    return BuildProject.CreateProjectInstance();
                }
            }
            return CurrentConfig;
        }

        private void OnInterpreterFactoriesChanged(object sender, EventArgs e) {
            Site.GetUIThread().Invoke(() => RefreshInterpreters());
        }

        private void OnInterpreterRegistryChanged(object sender, EventArgs e) {
            Site.GetUIThread().Invoke(() => {
                // Check whether the active interpreter factory has changed.
                var fact = InterpreterRegistry.FindInterpreter(ActiveInterpreter.Configuration.Id);
                if (fact != null && fact != ActiveInterpreter) {
                    ActiveInterpreter = fact;
                }
                InterpreterFactoriesChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        public IInterpreterOptionsService InterpreterOptions {
            get {
                return Site.GetComponentModel().GetService<IInterpreterOptionsService>();
            }
        }

        public IInterpreterRegistryService InterpreterRegistry {
            get {
                return Site.GetComponentModel().GetService<IInterpreterRegistryService>();
            }
        }

        public IPythonInterpreterFactory ActiveInterpreter {
            get {
                return _active ?? InterpreterOptions.DefaultInterpreter;
            }
            internal set {
                Debug.Assert(this.FileName != null);
                var oldActive = _active;

                lock (_validFactories) {
                    if (_validFactories.Count == 0) {
                        // No factories, so we must use the global default.
                        _active = null;
                    } else if (value == null || !_validFactories.Contains(value.Configuration.Id)) {
                        // Choose a factory and make it our default.
                        // TODO: We should have better ordering than this...
                        var compModel = Site.GetComponentModel();

                        _active = InterpreterRegistry.FindInterpreter(
                                _validFactories.ToList().OrderBy(f => f).LastOrDefault()
                        );
                    } else {
                        _active = value;
                    }
                }

                if (_active != oldActive) {
                    if (oldActive == null) {
                        var defaultInterp = InterpreterOptions.DefaultInterpreter as PythonInterpreterFactoryWithDatabase;
                        if (defaultInterp != null) {
                            defaultInterp.NewDatabaseAvailable -= OnNewDatabaseAvailable;
                        }
                    } else {
                        var oldInterpWithDb = oldActive as PythonInterpreterFactoryWithDatabase;
                        if (oldInterpWithDb != null) {
                            oldInterpWithDb.NewDatabaseAvailable -= OnNewDatabaseAvailable;
                        }
                    }

                    if (_active != null) {
                        var newInterpWithDb = _active as PythonInterpreterFactoryWithDatabase;
                        if (newInterpWithDb != null) {
                            newInterpWithDb.NewDatabaseAvailable += OnNewDatabaseAvailable;
                        }
                        BuildProject.SetProperty(
                            MSBuildConstants.InterpreterIdProperty,
                            ReplaceMSBuildPath(_active.Configuration.Id)
                        );
                    } else {
                        BuildProject.SetProperty(MSBuildConstants.InterpreterIdProperty, "");
                        var defaultInterp = InterpreterOptions.DefaultInterpreter as PythonInterpreterFactoryWithDatabase;
                        if (defaultInterp != null) {
                            defaultInterp.NewDatabaseAvailable += OnNewDatabaseAvailable;
                        }
                    }
                    BuildProject.MarkDirty();
                }

                // https://github.com/Microsoft/PTVS/issues/1739
                // When we go from "no interpreters" to "global default", we see
                // _active == oldActive == null. Previously we would not trigger
                // new analysis in this case.
                if (_active != oldActive || oldActive == null) {
                    ActiveInterpreterChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private string ReplaceMSBuildPath(string id) {
            int index = id.IndexOf(BuildProject.FullPath, StringComparison.OrdinalIgnoreCase);
            if (index != -1) {
                id = id.Substring(0, index) + "$(MSBuildProjectFullPath)" + id.Substring(index + BuildProject.FullPath.Length);
            }
            return id;
        }

        private void OnNewDatabaseAvailable(object sender, EventArgs e) {
            InterpreterDbChanged?.Invoke(this, EventArgs.Empty);
        }

        private void GlobalDefaultInterpreterChanged(object sender, EventArgs e) {
            if (_active == null) {
                // This event is only raised when our active interpreter is the
                // global default.
                ActiveInterpreterChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler ActiveInterpreterChanged;

        internal event EventHandler InterpreterFactoriesChanged;

        public void AddInterpreter(string id) {
            lock (_validFactories) {
                if (!_validFactories.Add(id)) {
                    return;
                }
            }

            BuildProject.AddItem(MSBuildConstants.InterpreterReferenceItem, id);
            if (IsActiveInterpreterGlobalDefault) {
                // force an update to 
                BuildProject.SetProperty(MSBuildConstants.InterpreterIdProperty, id);
            }

            Site.GetComponentModel().GetService<VsProjectContextProvider>().OnProjectChanged(
                BuildProject
            );
            UpdateActiveInterpreter();
            InterpreterFactoriesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AddInterpreterReference(InterpreterConfiguration config) {
            lock(_validFactories) {
                if (_validFactories.Contains(config.Id)) {
                    return;
                }
            }

            var projectHome = PathUtils.GetAbsoluteDirectoryPath(BuildProject.DirectoryPath, BuildProject.GetPropertyValue("ProjectHome"));
            var rootPath = PathUtils.EnsureEndSeparator(config.PrefixPath);

            var id = MSBuildProjectInterpreterFactoryProvider.GetProjectRelativeId(BuildProject.FullPath, config.Id);
            if (string.IsNullOrEmpty(id)) {
                throw new InvalidOperationException("Adding project environment {0} to wrong project {1}".FormatInvariant(config.Id, BuildProject.FullPath));
            }

            BuildProject.AddItem(MSBuildConstants.InterpreterItem,
                PathUtils.GetRelativeDirectoryPath(projectHome, rootPath),
                new Dictionary<string, string> {
                    { MSBuildConstants.IdKey, id },
                    { MSBuildConstants.VersionKey, config.Version.ToString() },
                    { MSBuildConstants.DescriptionKey, config.Description },
                    { MSBuildConstants.InterpreterPathKey, PathUtils.GetRelativeFilePath(rootPath, config.InterpreterPath) },
                    { MSBuildConstants.WindowsPathKey, PathUtils.GetRelativeFilePath(rootPath, config.WindowsInterpreterPath) },
                    { MSBuildConstants.PathEnvVarKey, config.PathEnvironmentVariable },
                    { MSBuildConstants.ArchitectureKey, config.Architecture.ToString("X") }
                });

            lock (_validFactories) {
                _validFactories.Add(config.Id);
                if (IsActiveInterpreterGlobalDefault) {
                    // force an update to 
                    BuildProject.SetProperty(MSBuildConstants.InterpreterIdProperty, config.Id);
                }
            }
            Site.GetComponentModel().GetService<VsProjectContextProvider>().OnProjectChanged(BuildProject);
            UpdateActiveInterpreter();
            InterpreterFactoriesChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void SaveMSBuildProjectFile(string filename) {
            base.SaveMSBuildProjectFile(filename);
            Site.GetComponentModel().GetService<VsProjectContextProvider>().UpdateProject(
                this,
                BuildProject
            );
        }

        /// <summary>
        /// Removes an interpreter factory from the project. This function will
        /// modify the project, but does not handle source control.
        /// </summary>
        /// <param name="factory">
        /// The id of the factory to remove. The function returns silently if
        /// the factory is not known by this provider.
        /// </param>
        public void RemoveInterpreterFactory(IPythonInterpreterFactory factory) {
            if (factory == null) {
                throw new ArgumentNullException(nameof(factory));
            }

            RemoveInterpreterFactory(factory.Configuration?.Id);
        }

        internal void RemoveInterpreterFactory(string id) {
            if (string.IsNullOrEmpty(id)) {
                throw new ArgumentNullException(nameof(id));
            }

            lock (_validFactories) {
                if (!_validFactories.Contains(id)) {
                    return;
                }
            }

            var subid = MSBuildProjectInterpreterFactoryProvider.GetProjectRelativeId(BuildProject.FullPath, id);
            bool projectChanged = false;

            if (!string.IsNullOrEmpty(subid)) {
                foreach (var item in BuildProject.GetItems(MSBuildConstants.InterpreterItem)) {
                    if (item.GetMetadataValue(MSBuildConstants.IdKey) == subid) {
                        try {
                            BuildProject.RemoveItem(item);
                            projectChanged = true;
                        } catch (InvalidOperationException) {
                        }
                        break;
                    }
                }
            }

            foreach (var item in BuildProject.GetItems(MSBuildConstants.InterpreterReferenceItem)) {
                if (id == item.EvaluatedInclude) {
                    try {
                        BuildProject.RemoveItem(item);
                        projectChanged = true;
                    } catch (InvalidOperationException) {
                    }
                    break;
                }
            }

            if (projectChanged) {
                BuildProject.MarkDirty();
                Site.GetComponentModel().GetService<VsProjectContextProvider>().OnProjectChanged(BuildProject);
            }

            lock (_validFactories) {
                if (!_validFactories.Remove(id)) {
                    // Wasn't removed, so don't update anything
                    return;
                }
            }
            UpdateActiveInterpreter();
            InterpreterFactoriesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateActiveInterpreter() {
            var newActive = _active;
            lock (_validFactories) {
                if (newActive == null ||
                    _validFactories.Count == 0 ||
                    !_validFactories.Contains(newActive.Configuration.Id)) {
                    newActive = InterpreterRegistry.FindInterpreter(
                        BuildProject.GetPropertyValue(MSBuildConstants.InterpreterIdProperty)
                    );
                }
            }
            ActiveInterpreter = newActive;
        }

        internal bool IsActiveInterpreterGlobalDefault => _active == null;

        internal IEnumerable<string> InterpreterIds => _validFactories.ToArray();

        internal IEnumerable<string> InvalidInterpreterIds {
            get {
                var compModel = Site.GetComponentModel();
                var registry = compModel.GetService<IInterpreterRegistryService>();

                foreach (var id in _validFactories) {
                    if (registry.FindConfiguration(id) == null) {
                        yield return id;
                    }
                }
            }
        }

        internal IEnumerable<InterpreterConfiguration> InterpreterConfigurations {
            get {
                var compModel = Site.GetComponentModel();
                var registry = compModel.GetService<IInterpreterRegistryService>();

                foreach (var config in _validFactories) {
                    var value = registry.FindConfiguration(config);
                    if (value != null) {
                        yield return value;
                    }
                }
            }
        }

        internal IEnumerable<IPythonInterpreterFactory> InterpreterFactories {
            get {
                var compModel = Site.GetComponentModel();
                var registry = compModel.GetService<IInterpreterRegistryService>();
                return InterpreterConfigurations
                    .Select(x => registry.FindInterpreter(x.Id))
                    .Where(x => x != null);
            }
        }

        protected override Stream ProjectIconsImageStripStream {
            get {
                throw new NotSupportedException("Python Tools does not support project image strip");
            }
        }

        protected internal override void SetCurrentConfiguration() {
            base.SetCurrentConfiguration();

            if (!IsProjectOpened)
                return;

            if (this.IsAppxPackageableProject()) {
                EnvDTE.Project automationObject = (EnvDTE.Project)GetAutomationObject();

                this.BuildProject.SetGlobalProperty(ProjectFileConstants.Platform, automationObject.ConfigurationManager.ActiveConfiguration.PlatformName);
            }
        }

        protected override bool SupportsIconMonikers {
            get { return true; }
        }

        protected override ImageMoniker GetIconMoniker(bool open) {
            return KnownMonikers.PYProjectNode;
        }

        internal override string IssueTrackerUrl {
            get { return Strings.IssueTrackerUrl; }
        }

        private static string GetSearchPathEntry(AnalysisEntry entry) {
            object result;
            entry.Properties.TryGetValue(_searchPathEntryKey, out result);
            return (string)result;
        }

        private static void SetSearchPathEntry(AnalysisEntry entry, string value) {
            entry.Properties[_searchPathEntryKey] = value;
        }

        public override CommonFileNode CreateCodeFileNode(ProjectElement item) {
            return new PythonFileNode(this, item);
        }

        public override CommonFileNode CreateNonCodeFileNode(ProjectElement item) {
            return new PythonNonCodeFileNode(this, item);
        }

        protected override ConfigProvider CreateConfigProvider() {
            return new CommonConfigProvider(this);
        }
        protected override ReferenceContainerNode CreateReferenceContainerNode() {
            return new PythonReferenceContainerNode(this);
        }

        protected override void LinkFileAdded(string filename) {
            if (Site.GetPythonToolsService().GeneralOptions.UpdateSearchPathsWhenAddingLinkedFiles) {
                // update our search paths.
                string dirToAdd;
                try {
                    dirToAdd = ModulePath.FromFullPath(filename).LibraryPath;
                } catch (ArgumentException) {
                    dirToAdd = null;
                }
                if (!string.IsNullOrEmpty(dirToAdd)) {
                    _searchPaths.Add(dirToAdd, true);
                }
            }

            base.LinkFileAdded(filename);
        }

        protected override Guid[] GetConfigurationIndependentPropertyPages() {
            return new[] {
                GetGeneralPropertyPageType().GUID,
                typeof(PythonDebugPropertyPage).GUID,
                typeof(PublishPropertyPage).GUID
            };
        }

        /// <summary>
        /// Evaluates if a file is a current language code file based on is extension
        /// </summary>
        /// <param name="strFileName">The filename to be evaluated</param>
        /// <returns>true if is a code file</returns>
        public override bool IsCodeFile(string strFileName) {
            return ModulePath.IsPythonSourceFile(strFileName);
        }

        public override string[] CodeFileExtensions {
            get {
                return new[] { PythonConstants.FileExtension, PythonConstants.WindowsFileExtension };
            }
        }

        public override Type GetProjectFactoryType() {
            return typeof(PythonProjectFactory);
        }

        public override string GetProjectName() {
            return "PythonProject";
        }

        protected override string ProjectCapabilities {
            get { return "Python"; }
        }

        public override string GetFormatList() {
            return PythonConstants.ProjectFileFilter;
        }

        public override Type GetGeneralPropertyPageType() {
            return typeof(PythonGeneralPropertyPage);
        }

        public override Type GetEditorFactoryType() {
            return typeof(PythonEditorFactory);
        }

        public override Type GetLibraryManagerType() {
            return typeof(IPythonLibraryManager);
        }

        protected override NodeProperties CreatePropertiesObject() {
            return new PythonProjectNodeProperties(this);
        }

        public override CommonProjectConfig MakeConfiguration(string activeConfigName) {
            return new PythonProjectConfig(this, activeConfigName);
        }

        protected internal override FolderNode CreateFolderNode(ProjectElement element) {
            return new PythonFolderNode(this, element);
        }

        protected override bool FilterItemTypeToBeAddedToHierarchy(string itemType) {
            if (MSBuildConstants.InterpreterReferenceItem.Equals(itemType, StringComparison.Ordinal) ||
                MSBuildConstants.InterpreterItem.Equals(itemType, StringComparison.Ordinal)) {
                return true;
            }
            return base.FilterItemTypeToBeAddedToHierarchy(itemType);
        }

        public override int QueryService(ref Guid guidService, out object result) {
#if DEV15
            // Sometimes this service is requested from us and it always seems
            // to lead to infinite recursion. All callers seem to handle the
            // failure case, so let's just bail immediately.
            if (guidService == typeof(SVSMDTypeResolutionService).GUID) {
                result = null;
                return VSConstants.E_FAIL;
            }
#endif

            var model = GetService(typeof(SComponentModel)) as IComponentModel;
            var designerSupport = model?.GetService<IXamlDesignerSupport>();

            if (designerSupport != null && guidService == designerSupport.DesignerContextTypeGuid) {
                result = DesignerContext;
                if (result == null) {
                    result = DesignerContext = designerSupport?.CreateDesignerContext();
                }
                return VSConstants.S_OK;
            }

            return base.QueryService(ref guidService, out result);
        }

        public override int GenerateUniqueItemName(uint itemIdLoc, string ext, string suggestedRoot, out string itemName) {
            if ("bin".Equals(suggestedRoot, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(ext)) {
                // This item should not have a number added to the name.
                itemName = (suggestedRoot ?? "") + (ext ?? "").Trim();
                return VSConstants.S_OK;
            }
            return base.GenerateUniqueItemName(itemIdLoc, ext, suggestedRoot, out itemName);
        }

        public override MSBuildResult Build(string config, string target) {
            if (this.IsAppxPackageableProject()) {
                // Ensure that AnyCPU is not the default Platform if this is an AppX project
                // Use x86 instead
                var platform = this.BuildProject.GetPropertyValue(GlobalProperty.Platform.ToString());

                if (platform == ProjectConfig.AnyCPU) {
                    this.BuildProject.SetGlobalProperty(GlobalProperty.Platform.ToString(), ConfigProvider.x86Platform);
                }
            }
            return base.Build(config, target);
        }

        protected override void Reload() {
            if (!this.IsAppxPackageableProject()) {
                _searchPathContainer = new CommonSearchPathContainerNode(this);
                this.AddChild(_searchPathContainer);
                RefreshCurrentWorkingDirectory();
            }

            _interpretersContainer = new InterpretersContainerNode(this);
            this.AddChild(_interpretersContainer);
            RefreshInterpreters(alwaysCollapse: true);

            OnProjectPropertyChanged += PythonProjectNode_OnProjectPropertyChanged;

            // Defer reanalysis until after we have loaded the project
            ActiveInterpreterChanged -= OnActiveInterpreterChanged;
            try {
                UpdateActiveInterpreter();
            } finally {
                ActiveInterpreterChanged += OnActiveInterpreterChanged;
            }

            base.Reload();

            string id;
            if (IsActiveInterpreterGlobalDefault &&
                !string.IsNullOrEmpty(id = GetProjectProperty(MSBuildConstants.InterpreterIdProperty))) {
                // The interpreter in the project file has no reference, so
                // find and add it.
                var interpFact = InterpreterRegistry.FindInterpreter(id);
                if (interpFact != null && QueryEditProjectFile(false)) {
                    AddInterpreter(id);
                }
            }

            if (!this.IsAppxPackageableProject()) {
                _searchPaths.LoadPathsFromString(ProjectHome, GetProjectProperty(PythonConstants.SearchPathSetting, false));
            }

            Site.GetUIThread().InvokeTask(async () => {
                await Task.Delay(10);
                for (int retries = 10; retries > 0; --retries) {
                    try {
                        await ReanalyzeProject();
                        return;
                    } catch (Exception ex) {
                        // Cannot allow UI here or we will re-enter with async tasks
                        ex.ReportUnhandledException(Site, GetType(), allowUI: false);
                    }
                    await Task.Delay(50);
                }
            });

            try {
                Site.GetPythonToolsService().SurveyNews.CheckSurveyNews(false);
            } catch (Exception ex) {
                Debug.Fail($"Error checking news: {ex}");
            }
        }

        private void RefreshCurrentWorkingDirectory() {
            try {
                IsRefreshing = true;
                string projHome = ProjectHome;
                string workDir = GetWorkingDirectory();

                //Refresh CWD node
                bool needCWD = !PathUtils.IsSameDirectory(projHome, workDir);
                var cwdNode = FindImmediateChild<CurrentWorkingDirectoryNode>(_searchPathContainer);
                if (needCWD) {
                    if (cwdNode == null) {
                        //No cwd node yet
                        _searchPathContainer.AddChild(new CurrentWorkingDirectoryNode(this, workDir));
                    } else if (!PathUtils.IsSameDirectory(cwdNode.Url, workDir)) {
                        //CWD has changed, recreate the node
                        cwdNode.Remove(false);
                        _searchPathContainer.AddChild(new CurrentWorkingDirectoryNode(this, workDir));
                    }
                } else {
                    //No need to show CWD, remove if exists
                    if (cwdNode != null) {
                        cwdNode.Remove(false);
                    }
                }
            } finally {
                IsRefreshing = false;
            }
        }

        private void RefreshSearchPaths() {
            try {
                IsRefreshing = true;

                var searchPath = _searchPaths.GetAbsoluteSearchPaths();

                //Refresh regular search path nodes
                SetProjectProperty(PythonConstants.SearchPathSetting, _searchPaths.SavePathsToString(ProjectHome));

                //We need to update search path nodes according to the search path property.
                //It's quite expensive to remove all and build all nodes from scratch, 
                //so we are going to perform some smarter update.
                //We are looping over paths in the search path and if a corresponding node
                //exists, we only update its index (sort order), creating new node otherwise.
                //At the end all nodes that haven't been updated have to be removed - they are
                //not in the search path anymore.
                var searchPathNodes = new List<CommonSearchPathNode>();
                this.FindNodesOfType<CommonSearchPathNode>(searchPathNodes);
                bool[] updatedNodes = new bool[searchPathNodes.Count];
                int index;
                for (int i = 0; i < searchPath.Count; i++) {
                    string path = searchPath[i];
                    //ParseSearchPath() must resolve all paths
                    Debug.Assert(Path.IsPathRooted(path));
                    var node = FindSearchPathNodeByPath(searchPathNodes, path, out index);
                    if (node != null) {
                        //existing path, update index (sort order)
                        node.Index = i;
                        updatedNodes[index] = true;
                    } else {
                        //new path - create new node
                        _searchPathContainer.AddChild(new CommonSearchPathNode(this, path, i));
                    }
                }

                //Refresh nodes and remove non-updated ones
                for (int i = 0; i < searchPathNodes.Count; i++) {
                    if (!updatedNodes[i]) {
                        searchPathNodes[i].Remove();
                    }
                }
            } finally {
                IsRefreshing = false;
            }
        }

        private static bool RemoveFirst<T>(List<T> list, Func<T, bool> condition) {
            for (int i = 0; i < list.Count; ++i) {
                if (condition(list[i])) {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        private void RefreshInterpreters(bool alwaysCollapse = false) {
            if (IsClosed) {
                return;
            }

            var node = _interpretersContainer;
            if (node == null) {
                return;
            }

            var remaining = node.AllChildren.OfType<InterpretersNode>().ToList();
            var vsProjectContext = Site.GetComponentModel().GetService<VsProjectContextProvider>();

            if (!IsActiveInterpreterGlobalDefault) {
                foreach (var fact in InterpreterFactories) {
                    if (!RemoveFirst(remaining, n => !n._isGlobalDefault && n._factory == fact)) {
                        bool isProjectSpecific = vsProjectContext.IsProjectSpecific(fact.Configuration);
                        bool canRemove = !this.IsAppxPackageableProject(); // Do not allow change python enivronment for UWP
                        node.AddChild(new InterpretersNode(
                            this,
                            fact,
                            isInterpreterReference: !isProjectSpecific,
                            canDelete:
                                isProjectSpecific &&
                                Directory.Exists(fact.Configuration.PrefixPath),
                            isGlobalDefault:false,
                            canRemove:canRemove
                        ));
                    }
                }
            } else {
                var fact = ActiveInterpreter;
                if (fact.IsRunnable() && !RemoveFirst(remaining, n => n._isGlobalDefault && n._factory == fact)) {
                    node.AddChild(new InterpretersNode(this, fact, true, false, true));
                }
            }

            foreach (var id in InvalidInterpreterIds) {
                if (!RemoveFirst(remaining, n => n._absentId == id)) {
                    node.AddChild(InterpretersNode.CreateAbsentInterpreterNode(this, id));
                }
            }

            foreach (var child in remaining) {
                node.RemoveChild(child);
            }

            if (alwaysCollapse || ParentHierarchy == null) {
                OnInvalidateItems(node);
            } else {
                bool wasExpanded = node.GetIsExpanded();
                var expandAfter = node.AllChildren.Where(n => n.GetIsExpanded()).ToArray();
                OnInvalidateItems(node);
                if (wasExpanded) {
                    node.ExpandItem(EXPANDFLAGS.EXPF_ExpandFolder);
                }
                foreach (var child in expandAfter) {
                    child.ExpandItem(EXPANDFLAGS.EXPF_ExpandFolder);
                }
            }
            BoldActiveEnvironment();
        }

        private void BoldActiveEnvironment() {
            var node = _interpretersContainer;
            if (node != null) {
                foreach (var child in node.AllChildren.OfType<InterpretersNode>()) {
                    BoldItem(child, child._factory == ActiveInterpreter);
                }
            }
        }

        /// <summary>
        /// Returns first immediate child node (non-recursive) of a given type.
        /// </summary>
        private static T FindImmediateChild<T>(HierarchyNode parent)
            where T : HierarchyNode {
            for (HierarchyNode n = parent.FirstChild; n != null; n = n.NextSibling) {
                if (n is T) {
                    return (T)n;
                }
            }
            return null;
        }

        /// <summary>
        /// Finds Search Path node by a given search path and returns it along with the node's index. 
        /// </summary>
        private CommonSearchPathNode FindSearchPathNodeByPath(IList<CommonSearchPathNode> nodes, string path, out int index) {
            index = 0;
            for (int j = 0; j < nodes.Count; j++) {
                if (PathUtils.IsSameDirectory(nodes[j].Url, path)) {
                    index = j;
                    return nodes[j];
                }
            }
            return null;
        }


        private void SearchPaths_Changed(object sender, EventArgs e) {
            // Update solution explorer
            Site.GetUIThread().InvokeAsync(() =>
                RefreshSearchPaths()
            );

            // Update analyzer
            UpdateAnalyzerSearchPaths();
        }

        private void UpdateAnalyzerSearchPaths(VsProjectAnalyzer analyzer = null) {
            analyzer = analyzer ?? _analyzer;
            if (analyzer != null) {
                analyzer.SetSearchPathsAsync(_searchPaths.GetAbsoluteSearchPaths())
                    .HandleAllExceptions(Site, GetType())
                    .DoNotWait();
            }
        }

        /// <summary>
        /// Returns a list of absolute search paths, optionally including those
        /// that are implied by other properties.
        /// </summary>
        public IList<string> GetSearchPaths(bool withImplied = true) {
            return withImplied ?
                _searchPaths.GetAbsoluteSearchPaths() :
                _searchPaths.GetAbsolutePersistedSearchPaths();
        }

        internal void OnInvalidateSearchPath(string absolutePath, object moniker) {
            if (string.IsNullOrEmpty(absolutePath)) {
                // Clear all paths associated with this moniker
                _searchPaths.RemoveByMoniker(moniker);
            } else if (!_searchPaths.AddOrReplace(moniker, absolutePath, false)) {
                // Didn't change a search path, so we need to trigger reanalysis
                // manually.
                UpdateAnalyzerSearchPaths();
            }
        }


        /// <summary>
        /// Executes Add Search Path menu command.
        /// </summary>        
        internal int AddSearchPath() {
            string dirName = Dialogs.BrowseForDirectory(IntPtr.Zero, ProjectHome, Strings.SelectFolderForSearchPath);

            if (dirName != null) {
                _searchPaths.Add(dirName, true);
            }

            return VSConstants.S_OK;
        }


        internal void RemoveSearchPath(string path) {
            _searchPaths.Remove(path);
        }

        private async void PythonProjectNode_OnProjectPropertyChanged(object sender, ProjectPropertyChangedArgs e) {
            switch (e.PropertyName) {
                case CommonConstants.StartupFile:
                    var genProp = GeneralPropertyPageControl;
                    if (genProp != null) {
                        genProp.StartupFile = e.NewValue;
                    }
                    break;
                case CommonConstants.WorkingDirectory:
                    RefreshCurrentWorkingDirectory();
                    genProp = GeneralPropertyPageControl;
                    if (genProp != null) {
                        genProp.WorkingDirectory = e.NewValue;
                    }
                    break;
            }

            var debugProp = DebugPropertyPage;
            if (debugProp != null) {
                ((PythonDebugPropertyPageControl)debugProp.Control).ReloadSetting(e.PropertyName);
            }
        }

        private PythonGeneralPropertyPageControl GeneralPropertyPageControl {
            get {
                if (PropertyPage != null && PropertyPage.Control != null) {
                    return (PythonGeneralPropertyPageControl)PropertyPage.Control;
                }

                return null;
            }
        }

        internal PythonDebugPropertyPage DebugPropertyPage {
            get {
                return _debugPropPage;
            }
            set {
                _debugPropPage = value;
            }
        }

        internal object DesignerContext {
            get {
                return _designerContext;
            }
            private set {
                Debug.Assert(_designerContext == null, "Should only set DesignerContext once");
                _designerContext = value;
            }
        }

        /*
        public PythonAnalyzer GetProjectAnalyzer() {
            return GetAnalyzer().Project;
        }
        */
        VsProjectAnalyzer IPythonProject.GetProjectAnalyzer() {
            return GetAnalyzer();
        }

        public event EventHandler ProjectAnalyzerChanged;
        public event EventHandler<AnalyzerChangingEventArgs> ProjectAnalyzerChanging;
        public event EventHandler InterpreterDbChanged;

        public override IProjectLauncher GetLauncher() {
            return PythonToolsPackage.GetLauncher(Site, this);
        }

        public void AddActionOnClose(object key, Action<object> action) {
            Debug.Assert(key != null);
            Debug.Assert(action != null);
            if (key == null || action == null) {
                return;
            }

            if (_actionsOnClose == null) {
                _actionsOnClose = new Dictionary<object, Action<object>>();
            }
            _actionsOnClose[key] = action;
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                var actions = _actionsOnClose;
                _actionsOnClose = null;
                if (actions != null) {
                    foreach (var keyValue in actions) {
                        if (keyValue.Value != null) {
                            keyValue.Value(keyValue.Key);
                        }
                    }
                }

                if (_analyzer != null) {
                    UnHookErrorsAndWarnings(_analyzer);
                    _analyzer.ClearAllTasks();

                    if (_analyzer.RemoveUser()) {
                        _analyzer.Dispose();
                    }
                    _analyzer = null;
                }

                InterpreterOptions.DefaultInterpreterChanged -= GlobalDefaultInterpreterChanged;
                InterpreterRegistry.InterpretersChanged -= OnInterpreterRegistryChanged;

                if (_interpretersContainer != null) {
                    _interpretersContainer.Dispose();
                    _interpretersContainer = null;
                }
                if (_searchPathContainer != null) {
                    _searchPathContainer.Dispose();
                    _searchPathContainer = null;
                }
                if (_customCommands != null) {
                    foreach (var c in _customCommands) {
                        c.Dispose();
                    }
                    _customCommands = null;
                }
            }

            base.Dispose(disposing);
        }

        public int SetInterpreterFactory(IPythonInterpreterFactory factory) {
            if (factory != ActiveInterpreter) {
                //Make sure we can edit the project file
                if (!ProjectMgr.QueryEditProjectFile(false)) {
                    return VSConstants.OLE_E_PROMPTSAVECANCELLED;
                }

                ActiveInterpreter = factory;
            }
            return VSConstants.S_OK;
        }

        public VsProjectAnalyzer GetAnalyzer() {
            if (IsClosed) {
                Debug.Fail("GetAnalyzer() called on closed project " + new StackTrace(true).ToString());
                var service = (PythonToolsService)PythonToolsPackage.GetGlobalService(typeof(PythonToolsService));
                if (service == null) {
                    throw new InvalidOperationException("Called GetAnalyzer() with no Python Tools service available");
                }
                return service.DefaultAnalyzer;
            } else if (_analyzer == null) {
                _analyzer = CreateAnalyzer();
            }
            return _analyzer;
        }

        private VsProjectAnalyzer CreateAnalyzer() {
            // check to see if we should share our analyzer with another project in the same solution.  This enables
            // refactoring, find all refs, and intellisense across projects.
            var vsSolution = (IVsSolution)GetService(typeof(SVsSolution));
            if (vsSolution != null) {
                var guid = new Guid(PythonConstants.ProjectFactoryGuid);
                IEnumHierarchies hierarchies;
                ErrorHandler.ThrowOnFailure((vsSolution.GetProjectEnum((uint)(__VSENUMPROJFLAGS.EPF_MATCHTYPE | __VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION), ref guid, out hierarchies)));
                IVsHierarchy[] hierarchy = new IVsHierarchy[1];
                uint fetched;
                var curFactory = GetInterpreterFactory();
                while (ErrorHandler.Succeeded(hierarchies.Next(1, hierarchy, out fetched)) && fetched == 1) {
                    var proj = hierarchy[0].GetProject();
                    Debug.Assert(proj != null);
                    if (proj != null) {
                        var pyProj = proj.GetPythonProject();
                        Debug.Assert(pyProj != null);

                        if (pyProj != this &&
                            pyProj._analyzer != null &&
                            pyProj._analyzer.InterpreterFactory == curFactory) {
                            // we have the same interpreter, we'll share analysis engines across projects.
                            pyProj._analyzer.AddUser();
                            HookErrorsAndWarnings(pyProj._analyzer);
                            return pyProj._analyzer;
                        }
                    }
                }
            }

            var model = Site.GetComponentModel();
            var interpreterService = model.GetService<IInterpreterRegistryService>();
            var factory = GetInterpreterFactory();
            var res = new VsProjectAnalyzer(
                Site,
                factory,
                false,
                BuildProject
            );
            res.AbnormalAnalysisExit += AnalysisProcessExited;

            HookErrorsAndWarnings(res);
            UpdateAnalyzerSearchPaths(res);
            return res;
        }

        private void AnalysisProcessExited(object sender, AbnormalAnalysisExitEventArgs e) {
            StringBuilder msg = new StringBuilder();
            msg.AppendFormat("Exit Code: {0}", e.ExitCode);
            msg.AppendLine();
            msg.AppendLine(" ------ STD ERR ------ ");
            msg.Append(e.StdErr);
            msg.AppendLine(" ------ END STD ERR ------ ");
            Site.GetPythonToolsService().Logger.LogEvent(
                PythonLogEvent.AnalysisExitedAbnormally,
                msg.ToString()
            );
        }

        private void HookErrorsAndWarnings(VsProjectAnalyzer res) {
            res.ShouldWarnOnLaunchChanged += OnShouldWarnOnLaunchChanged;
        }

        private void UnHookErrorsAndWarnings(VsProjectAnalyzer res) {
            res.ShouldWarnOnLaunchChanged -= OnShouldWarnOnLaunchChanged;
            _warnOnLaunchFiles.Clear();
        }

        private void OnShouldWarnOnLaunchChanged(object sender, EntryEventArgs e) {
            if (_diskNodes.ContainsKey(e.Entry.Path ?? "")) {
                if (((VsProjectAnalyzer)sender).ShouldWarnOnLaunch(e.Entry)) {
                    _warnOnLaunchFiles.Add(e.Entry);
                } else {
                    _warnOnLaunchFiles.Remove(e.Entry);
                }
            }
        }

        public bool ShouldWarnOnLaunch {
            get {
                return _warnOnLaunchFiles.Any();
            }
        }

        public IPythonInterpreterFactory GetInterpreterFactory() {
            var fact = ActiveInterpreter;
            if (fact == null) {
                // May occur if we are racing with Dispose(), so the factory we
                // return isn't important, but it has to be non-null to fulfil
                // the contract.
                return InterpreterOptions.DefaultInterpreter;
            }


            Site.GetPythonToolsService().EnsureCompletionDb(fact);

            return fact;
        }

        /// <summary>
        /// Returns the active interpreter factory or throws an appropriate
        /// exception. These exceptions have localized strings that may be
        /// shown to the user.
        /// </summary>
        /// <returns>The active interpreter factory.</returns>
        /// <exception cref="NoInterpretersException">
        /// No interpreters are available at all.
        /// </exception>
        /// <exception cref="MissingInterpreterException">
        /// The specified interpreter is not suitable for use.
        /// </exception>
        public IPythonInterpreterFactory GetInterpreterFactoryOrThrow() {
            var fact = ActiveInterpreter;
            if (fact == null) {
                throw new NoInterpretersException();
            }

            if (!fact.Configuration.IsAvailable()) {
                throw new MissingInterpreterException(
                    Strings.MissingEnvironment.FormatUI(fact.Configuration.Description, fact.Configuration.Version)
                );
            } else if (IsActiveInterpreterGlobalDefault && 
                !String.IsNullOrWhiteSpace(BuildProject.GetPropertyValue(MSBuildConstants.InterpreterIdProperty))) {
                throw new MissingInterpreterException(
                    Strings.MissingEnvironmentUnknownVersion.FormatUI(
                        BuildProject.GetPropertyValue(MSBuildConstants.InterpreterIdProperty)
                    )
                );
            }

            Site.GetPythonToolsService().EnsureCompletionDb(fact);

            return fact;
        }

        /// <summary>
        /// Returns the current launch configuration or throws an appropriate
        /// exception. These exceptions have localized strings that may be
        /// shown to the user.
        /// </summary>
        /// <returns>The active interpreter factory.</returns>
        /// <exception cref="NoInterpretersException">
        /// No interpreters are available at all.
        /// </exception>
        /// <exception cref="MissingInterpreterException">
        /// The specified interpreter is not suitable for use.
        /// </exception>
        /// <exception cref="DirectoryNotFoundException">
        /// The working directory specified by the project does not exist.
        /// </exception>
        public LaunchConfiguration GetLaunchConfigurationOrThrow() {
            var fact = GetInterpreterFactoryOrThrow();

            var intPath = GetProjectProperty(PythonConstants.InterpreterPathSetting, resetCache: false);
            if (string.IsNullOrEmpty(intPath)) {
                intPath = null;
            } else if (!Path.IsPathRooted(intPath)) {
                intPath = PathUtils.GetAbsoluteFilePath(ProjectHome, intPath);
            }

            var config = new LaunchConfiguration(fact.Configuration) {
                InterpreterPath = intPath,
                InterpreterArguments = GetProjectProperty(PythonConstants.InterpreterArgumentsSetting, resetCache: false),
                ScriptName = GetStartupFile(),
                ScriptArguments = GetProjectProperty(PythonConstants.CommandLineArgumentsSetting, resetCache: false),
                WorkingDirectory = GetWorkingDirectory(),
                SearchPaths = _searchPaths.GetAbsoluteSearchPaths().ToList()
            };

            var str = GetProjectProperty(PythonConstants.IsWindowsApplicationSetting);
            bool preferWindowed;
            config.PreferWindowedInterpreter = bool.TryParse(str, out preferWindowed) && preferWindowed;

            config.Environment = PathUtils.ParseEnvironment(GetProjectProperty(PythonConstants.EnvironmentSetting) ?? "");

            str = GetProjectProperty(PythonConstants.WebBrowserUrlSetting);
            if (!string.IsNullOrEmpty(str)) {
                config.LaunchOptions[PythonConstants.WebBrowserUrlSetting] = str;
            }
            str = GetProjectProperty(PythonConstants.WebBrowserPortSetting);
            if (!string.IsNullOrEmpty(str)) {
                config.LaunchOptions[PythonConstants.WebBrowserPortSetting] = str;
            }
            str = GetProjectProperty(PythonConstants.EnableNativeCodeDebugging);
            if (!string.IsNullOrEmpty(str)) {
                config.LaunchOptions[PythonConstants.EnableNativeCodeDebugging] = str;
            }

            if (!File.Exists(config.GetInterpreterPath())) {
                throw new MissingInterpreterException(
                    Strings.DebugLaunchInterpreterMissing_Path.FormatUI(config.GetInterpreterPath())
                );
            }

            if (!Directory.Exists(config.WorkingDirectory)) {
                throw new DirectoryNotFoundException(
                    Strings.DebugLaunchWorkingDirectoryMissing.FormatUI(config.WorkingDirectory)
                );
            }

            // Ensure working directory is a search path.
            config.SearchPaths.Insert(0, config.WorkingDirectory);

            if (!Site.GetPythonToolsService().GeneralOptions.ClearGlobalPythonPath) {
                config.SearchPaths.AddRange(Environment.GetEnvironmentVariable(config.Interpreter.PathEnvironmentVariable)
                    .Split(Path.PathSeparator)
                    // Just ensure the string is not empty - if people are passing
                    // through invalid paths this option is meant to allow it
                    .Where(p => !string.IsNullOrEmpty(p))
                );
            }

            return config;
        }

        /// <summary>
        /// Called when the active interpreter is changed.  A new interpreter
        /// will be created immediately unless another project in the solution
        /// can provide a matching analyzer.
        /// </summary>
        private void OnActiveInterpreterChanged(object sender, EventArgs e) {
            if (IsClosed) {
                return;
            }

            InterpreterDbChanged?.Invoke(this, EventArgs.Empty);
            Site.GetUIThread().InvokeTask(async () => {
                await ReanalyzeProject().HandleAllExceptions(Site, GetType());
            }).DoNotWait();
        }

        private async Task ReanalyzeProject() {
            if (IsClosing || IsClosed) {
                // This deferred event is no longer important.
                return;
            }

            var statusBar = Site.GetService(typeof(SVsStatusbar)) as IVsStatusbar;
            if (statusBar != null) {
                statusBar.SetText(Strings.AnalyzingProject);
                object index = (short)0;
                statusBar.Animation(1, ref index);
                statusBar.FreezeOutput(1);
            }

            try {
                RefreshInterpreters();

                if (_analyzer != null) {
                    UnHookErrorsAndWarnings(_analyzer);
                }
                var analyzer = CreateAnalyzer();
                Debug.Assert(analyzer != null);

                ProjectAnalyzerChanging?.Invoke(this, new AnalyzerChangingEventArgs(_analyzer, analyzer));

                var oldAnalyzer = Interlocked.Exchange(ref _analyzer, analyzer);

                if (oldAnalyzer != null) {
                    if (analyzer != null) {
                        analyzer.SwitchAnalyzers(oldAnalyzer);
                    }
                    if (oldAnalyzer.RemoveUser()) {
                        oldAnalyzer.Dispose();
                    }
                }

                if (analyzer != null) {
                    var files = AllVisibleDescendants.OfType<PythonFileNode>().Select(f => f.Url).ToArray();
                    await analyzer.AnalyzeFileAsync(files);
                    await analyzer.SetSearchPathsAsync(_searchPaths.GetAbsoluteSearchPaths());
                }

                ProjectAnalyzerChanged?.Invoke(this, EventArgs.Empty);
            } catch (ObjectDisposedException) {
                // Raced with project disposal
            } finally {
                if (statusBar != null) {
                    statusBar.FreezeOutput(0);
                    object index = (short)0;
                    statusBar.Animation(0, ref index);
                    statusBar.Clear();
                }
            }
        }

        protected override string AssemblyReferenceTargetMoniker {
            get {
                return GetProjectProperty("TargetFrameworkMoniker", false); // ?? ".NETFramework, version=4.5";
            }
        }

        protected override string AddReferenceExtensions {
            get {
                return null;
            }
        }

        internal int OpenCommandPrompt(string path, InterpreterConfiguration interpreterConfig = null, string subtitle = null) {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
            psi.UseShellExecute = false;
            psi.WorkingDirectory = path;

            var config = GetLaunchConfigurationOrThrow();
            if (interpreterConfig != null) {
                config = config.Clone(interpreterConfig);
            }

            psi.Arguments = string.Format("/K \"title {0} Command Prompt\"",
                string.IsNullOrEmpty(subtitle) ? Caption : subtitle
            );


            var paths = config.Interpreter.PrefixPath;
            if (!Directory.Exists(paths)) {
                paths = PathUtils.GetParent(config.GetInterpreterPath());
            }
            string scripts;
            if (Directory.Exists(paths) &&
                Directory.Exists(scripts = PathUtils.GetAbsoluteDirectoryPath(paths, "Scripts"))) {
                paths += Path.PathSeparator + scripts;
            }

            var env = PathUtils.MergeEnvironments(
                PathUtils.MergeEnvironments(
                    Environment.GetEnvironmentVariables().AsEnumerable<string, string>(),
                    config.GetEnvironmentVariables(),
                    "PATH"
                ),
                new KeyValuePair<string, string>[] {
                    new KeyValuePair<string, string>("PATH", paths),
                },
                "PATH", config.Interpreter.PathEnvironmentVariable
            );

            foreach (var kv in env) {
                psi.EnvironmentVariables[kv.Key] = kv.Value;
            }

            Process.Start(psi);
            return VSConstants.S_OK;

        }

        internal override int ExecCommandOnNode(Guid cmdGroup, uint cmd, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
            if (cmdGroup == VsMenus.guidStandardCommandSet2K) {
                switch ((VsCommands2K)cmd) {
                    case VsCommands2K.ECMD_PUBLISHSELECTION:
                    case VsCommands2K.ECMD_PUBLISHSLNCTX:
                        Publish(PublishProjectOptions.Default, true);
                        return VSConstants.S_OK;
                    case CommonConstants.OpenFolderInExplorerCmdId:
                        Process.Start(this.ProjectHome);
                        return VSConstants.S_OK;
                }
            }

            if (cmdGroup == ProjectMgr.SharedCommandGuid) {
                switch ((SharedCommands)cmd) {
                    case SharedCommands.OpenCommandPromptHere:
                        return OpenCommandPrompt(FullPathToChildren);
                }
            }

            return base.ExecCommandOnNode(cmdGroup, cmd, nCmdexecopt, pvaIn, pvaOut);
        }

        internal override int QueryStatusOnNode(Guid cmdGroup, uint cmd, IntPtr pCmdText, ref QueryStatusResult result) {
            if (cmdGroup == GuidList.guidPythonToolsCmdSet) {
                IPythonInterpreterFactory factory;
                switch ((int)cmd) {
                    case PythonConstants.OpenInteractiveForEnvironment:
                        factory = GetInterpreterFactory();
                        if (factory.IsRunnable()) {
                            result |= QueryStatusResult.SUPPORTED | QueryStatusResult.ENABLED;
                        } else {
                            result |= QueryStatusResult.INVISIBLE;
                        }
                        return VSConstants.S_OK;

                    case PythonConstants.InstallPythonPackage:
                    case PythonConstants.InstallRequirementsTxt:
                    case PythonConstants.GenerateRequirementsTxt:
                        factory = GetInterpreterFactory();
                        if (factory.IsRunnable() && factory.PackageManager != null) {
                            result |= QueryStatusResult.SUPPORTED | QueryStatusResult.ENABLED;
                        } else {
                            result |= QueryStatusResult.INVISIBLE;
                        }
                        return VSConstants.S_OK;

                    case PythonConstants.CustomProjectCommandsMenu:
                        if (_customCommands != null && _customCommands.Any()) {
                            result |= QueryStatusResult.SUPPORTED | QueryStatusResult.ENABLED;
                        } else {
                            result |= QueryStatusResult.INVISIBLE;
                        }

                        if (pCmdText != IntPtr.Zero && NativeMethods.OLECMDTEXT.GetFlags(pCmdText) == NativeMethods.OLECMDTEXT.OLECMDTEXTF.OLECMDTEXTF_NAME) {
                            NativeMethods.OLECMDTEXT.SetText(pCmdText, _customCommandsDisplayLabel);
                        }
                        return VSConstants.S_OK;
                }

            }
            return base.QueryStatusOnNode(cmdGroup, cmd, pCmdText, ref result);
        }

        private CustomCommand GetCustomCommand(uint cmdId) {
            if ((int)cmdId >= PythonConstants.FirstCustomCmdId && (int)cmdId <= PythonConstants.LastCustomCmdId) {

                int i = (int)cmdId - PythonConstants.FirstCustomCmdId;
                if (_customCommands == null || i >= _customCommands.Count) {
                    return null;
                }

                return _customCommands[i];
            } else {
                return _customCommands.FirstOrDefault(c => c.AlternateCmdId == cmdId);
            }
        }

        protected override QueryStatusResult QueryStatusSelectionOnNodes(IList<HierarchyNode> selectedNodes, Guid cmdGroup, uint cmd, IntPtr pCmdText) {
            if (cmdGroup == GuidList.guidPythonToolsCmdSet) {
                var command = GetCustomCommand(cmd);
                if (command != null) {
                    // Update display text for the menu commands.
                    if ((int)cmd >= PythonConstants.FirstCustomCmdId && (int)cmd <= PythonConstants.LastCustomCmdId) {
                        if (pCmdText != IntPtr.Zero && NativeMethods.OLECMDTEXT.GetFlags(pCmdText) == NativeMethods.OLECMDTEXT.OLECMDTEXTF.OLECMDTEXTF_NAME) {
                            NativeMethods.OLECMDTEXT.SetText(pCmdText, command.DisplayLabel);
                        }
                    }

                    var result = QueryStatusResult.SUPPORTED;
                    if (command.CanExecute(null)) {
                        result |= QueryStatusResult.ENABLED;
                    }
                    return result;
                }

                if ((int)cmd >= PythonConstants.FirstCustomCmdId && (int)cmd <= PythonConstants.LastCustomCmdId) {
                    // All unspecified custom commands are hidden
                    return QueryStatusResult.INVISIBLE | QueryStatusResult.NOTSUPPORTED;
                }

                QueryStatusResult status;
                switch ((int)cmd) {
                    case PythonConstants.InstallRequirementsTxt:
                        status = base.QueryStatusSelectionOnNodes(selectedNodes, cmdGroup, cmd, pCmdText) |
                            QueryStatusResult.SUPPORTED;
                        return status;
                    case PythonConstants.ActivateEnvironment:
                        status = base.QueryStatusSelectionOnNodes(selectedNodes, cmdGroup, cmd, pCmdText);
                        if (!status.HasFlag(QueryStatusResult.SUPPORTED)) {
                            // Command is supported if an environment is
                            // selected, so only force enable if nobody has
                            // claimed it.
                            status = QueryStatusResult.SUPPORTED | QueryStatusResult.ENABLED;
                        }
                        return status;
                }
            }

            return base.QueryStatusSelectionOnNodes(selectedNodes, cmdGroup, cmd, pCmdText);
        }

        private async Task ExecuteCustomCommandAsync(CustomCommand command) {
            try {
                await command.ExecuteAsync(null);
            } catch (MissingInterpreterException ex) {
                MessageBox.Show(ex.Message, Strings.ProductTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            } catch (NoInterpretersException ex) {
                PythonToolsPackage.OpenNoInterpretersHelpPage(Site, ex.HelpPage);
            } catch (Exception ex) {
                MessageBox.Show(
                    Strings.ErrorRunningCustomCommand.FormatUI(
                        command.DisplayLabelWithoutAccessKeys,
                        ex.Message
                    ),
                    Strings.ProductTitle
                );
            }
        }

        protected override int ExecCommandIndependentOfSelection(Guid cmdGroup, uint cmdId, uint cmdExecOpt, IntPtr vaIn, IntPtr vaOut, CommandOrigin commandOrigin, out bool handled) {
            if (cmdGroup == GuidList.guidPythonToolsCmdSet) {
                var command = GetCustomCommand(cmdId);
                handled = true;

                if (command != null) {
                    if (command.CanExecute(null)) {
                        if (!Utilities.SaveDirtyFiles()) {
                            return VSConstants.S_OK;
                        }

                        ExecuteCustomCommandAsync(command).DoNotWait();
                    }
                    return VSConstants.S_OK;
                }

                switch ((int)cmdId) {
                    case PythonConstants.AddEnvironment:
                        ShowAddInterpreter();
                        return VSConstants.S_OK;
                    case PythonConstants.AddExistingVirtualEnv:
                    case PythonConstants.AddVirtualEnv:
                        ShowAddVirtualEnvironmentWithErrorHandling((int)cmdId == PythonConstants.AddExistingVirtualEnv);
                        return VSConstants.S_OK;
                    case PythonConstants.ViewAllEnvironments:
                        Site.ShowInterpreterList();
                        return VSConstants.S_OK;
                    case PythonConstants.AddSearchPathCommandId:
                        return AddSearchPath();
                    case PythonConstants.AddSearchPathZipCommandId:
                        return AddSearchPathZip();
                    case PythonConstants.AddPythonPathToSearchPathCommandId:
                        return AddPythonPathToSearchPath();
                    default:
                        handled = false;
                        break;
                }
            }

            return base.ExecCommandIndependentOfSelection(cmdGroup, cmdId, cmdExecOpt, vaIn, vaOut, commandOrigin, out handled);
        }

        private void GetSelectedInterpreterOrDefault(
            IEnumerable<HierarchyNode> selectedNodes,
            Dictionary<string, string> args,
            out InterpretersNode node,
            out IPythonInterpreterFactory factory,
            bool useProjectByDefault = true
        ) {
            factory = null;

            // First try and get the factory from the parameter
            string description;
            if (args != null && args.TryGetValue("e", out description) && !string.IsNullOrEmpty(description)) {
                var service = Site.GetComponentModel().GetService<IInterpreterRegistryService>();
                InterpreterConfiguration config;

                config = InterpreterConfigurations.FirstOrDefault(
                    // Description is a localized string, hence CCIC
                    c => description.Equals(c.Description, StringComparison.CurrentCultureIgnoreCase)
                );
                if (config != null) {
                    factory = service.FindInterpreter(config.Id);
                }

                if (factory == null) {
                    config = service.Configurations.FirstOrDefault(
                        c => description.Equals(c.Description, StringComparison.CurrentCultureIgnoreCase)
                    );
                    if (config != null) {
                        factory = service.FindInterpreter(config.Id);
                    }
                }
            }

            if (factory == null) {
                var candidates = selectedNodes
                    .OfType<InterpretersNode>()
                    .Where(n => n._factory != null && n._factory.IsRunnable())
                    .Distinct()
                    .ToList();

                if (candidates.Count == 1) {
                    node = candidates[0];
                    factory = node._factory;
                    return;
                }

                if (useProjectByDefault) {
                    factory = GetInterpreterFactory();
                }
            }

            if (_interpretersContainer != null && factory != null) {
                var active = factory;
                node = _interpretersContainer.AllChildren
                    .OfType<InterpretersNode>()
                    .FirstOrDefault(n => n._factory == active);
            } else {
                node = null;
            }
        }

        protected internal override string QueryCommandArguments(Guid cmdGroup, uint cmdId, CommandOrigin commandOrigin) {
            if (cmdGroup == GuidList.guidPythonToolsCmdSet) {
                switch ((int)cmdId) {
                    case PythonConstants.ActivateEnvironment:
                        return "e,env,environment:";
                    case PythonConstants.InstallRequirementsTxt:
                        return "e,env,environment: a,admin y";
                    case PythonConstants.OpenInteractiveForEnvironment:
                        return "e,env,environment:";
                    case PythonConstants.InstallPythonPackage:
                        return "e,env,environment: p,package: a,admin";
                    case PythonConstants.GenerateRequirementsTxt:
                        return "e,env,environment:";
                }
            }
            return base.QueryCommandArguments(cmdGroup, cmdId, commandOrigin);
        }

        protected override int ExecCommandThatDependsOnSelectedNodes(Guid cmdGroup, uint cmdId, uint cmdExecOpt, IntPtr vaIn, IntPtr vaOut, CommandOrigin commandOrigin, IList<HierarchyNode> selectedNodes, out bool handled) {
            if (cmdGroup == GuidList.guidPythonToolsCmdSet) {
                handled = true;
                switch ((int)cmdId) {
                    case PythonConstants.ActivateEnvironment:
                        return ExecActivateEnvironment(ParseCommandArgs(vaIn, cmdGroup, cmdId), selectedNodes);
                    case PythonConstants.InstallRequirementsTxt:
                        return ExecInstallRequirementsTxt(ParseCommandArgs(vaIn, cmdGroup, cmdId), selectedNodes);
                    case PythonConstants.OpenInteractiveForEnvironment:
                        return ExecOpenInteractiveForEnvironment(ParseCommandArgs(vaIn, cmdGroup, cmdId), selectedNodes);
                    case PythonConstants.InstallPythonPackage:
                        return ExecInstallPythonPackage(ParseCommandArgs(vaIn, cmdGroup, cmdId), selectedNodes);
                    case PythonConstants.GenerateRequirementsTxt:
                        return ExecGenerateRequirementsTxt(ParseCommandArgs(vaIn, cmdGroup, cmdId), selectedNodes);
                    default:
                        handled = false;
                        break;
                }
            }

            return base.ExecCommandThatDependsOnSelectedNodes(cmdGroup, cmdId, cmdExecOpt, vaIn, vaOut, commandOrigin, selectedNodes, out handled);
        }

        protected override bool DisableCmdInCurrentMode(Guid cmdGroup, uint cmd) {
            if (cmdGroup == GuidList.guidPythonToolsCmdSet) {
                if (IsCurrentStateASuppressCommandsMode()) {
                    switch ((int)cmd) {
                        case CommonConstants.StartDebuggingCmdId:
                        case CommonConstants.StartWithoutDebuggingCmdId:
                            return true;
                        case PythonConstants.ActivateEnvironment:
                        case PythonConstants.AddEnvironment:
                        case PythonConstants.AddExistingVirtualEnv:
                        case PythonConstants.AddVirtualEnv:
                        case PythonConstants.InstallPythonPackage:
                        case PythonConstants.InstallRequirementsTxt:
                        case PythonConstants.GenerateRequirementsTxt:
                        case PythonConstants.AddSearchPathCommandId:
                        case PythonConstants.AddSearchPathZipCommandId:
                        case PythonConstants.AddPythonPathToSearchPathCommandId:
                            return true;
                        default:
                            if (cmd >= PythonConstants.FirstCustomCmdId && cmd <= PythonConstants.LastCustomCmdId) {
                                return true;
                            }
                            break;
                    }
                } else if (this.IsAppxPackageableProject()) {
                    // Disable adding environment for UWP projects
                    switch ((int)cmd) {
                        case PythonConstants.AddEnvironment:
                        case PythonConstants.AddExistingVirtualEnv:
                        case PythonConstants.AddVirtualEnv:
                            return true;
                    }
                }
            }

            return base.DisableCmdInCurrentMode(cmdGroup, cmd);
        }

        private int ExecActivateEnvironment(Dictionary<string, string> args, IList<HierarchyNode> selectedNodes) {
            InterpretersNode selectedInterpreter;
            IPythonInterpreterFactory selectedInterpreterFactory;
            GetSelectedInterpreterOrDefault(selectedNodes, args, out selectedInterpreter, out selectedInterpreterFactory,
                useProjectByDefault: false);
            if (selectedInterpreterFactory != null) {
                return SetInterpreterFactory(selectedInterpreterFactory);
            }
            return VSConstants.S_OK;
        }

        private int ExecOpenInteractiveForEnvironment(Dictionary<string, string> args, IList<HierarchyNode> selectedNodes) {
            InterpretersNode selectedInterpreter;
            IPythonInterpreterFactory selectedInterpreterFactory;
            GetSelectedInterpreterOrDefault(selectedNodes, args, out selectedInterpreter, out selectedInterpreterFactory,
                useProjectByDefault: false);
            try {
                ExecuteInReplCommand.EnsureReplWindow(Site, selectedInterpreterFactory?.Configuration, this).Show(true);
            } catch (InvalidOperationException ex) {
                MessageBox.Show(Strings.ErrorOpeningInteractiveWindow.FormatUI(ex), Strings.ProductTitle);
            }
            return VSConstants.S_OK;
        }


        #region IPythonProject Members

        string IPythonProject.ProjectName {
            get {
                return Caption;
            }
        }

        string IPythonProject.ProjectDirectory {
            get {
                return ProjectHome;
            }
        }

        string IPythonProject.GetProperty(string name) {
            return GetProjectProperty(name, true);
        }

        void IPythonProject.SetProperty(string name, string value) {
            SetProjectProperty(name, value);
        }

        string IPythonProject.GetStartupFile() {
            return base.GetStartupFile();
        }

        IPythonInterpreterFactory IPythonProject.GetInterpreterFactory() {
            return this.GetInterpreterFactory();
        }

        bool IPythonProject.Publish(PublishProjectOptions options) {
            return Publish(options, false);
        }

        public override bool Publish(PublishProjectOptions publishOptions, bool async) {
            var factory = GetInterpreterFactory();
            if (factory.Configuration.IsAvailable() &&
                Directory.Exists(factory.Configuration.PrefixPath) &&
                PathUtils.IsSubpathOf(ProjectHome, factory.Configuration.PrefixPath)
            ) {
                try {
                    publishOptions = TaskDialog.CallWithRetry(
                        _ => new PublishProjectOptions(
                            publishOptions.AdditionalFiles.Concat(
                                PathUtils.EnumerateFiles(factory.Configuration.PrefixPath)
                                    // Exclude the completion DB
                                    .Where(f => !f.Contains("\\.ptvs\\"))
                                    .Select(f => new PublishFile(f, PathUtils.GetRelativeFilePath(ProjectHome, f)))
                            ).ToArray(),
                            publishOptions.DestinationUrl
                        ),
                        Site,
                        Strings.FailedToCollectFilesForPublish,
                        Strings.FailedToCollectFilesForPublishMessage,
                        Strings.ErrorDetail,
                        Strings.Retry,
                        Strings.Cancel
                    );
                } catch (OperationCanceledException) {
                    return false;
                }
            }
            return base.Publish(publishOptions, async);
        }

        string IPythonProject.GetUnevaluatedProperty(string name) {
            return base.GetUnevaluatedProperty(name);
        }

        #endregion

        #region Search Path support

        internal int AddSearchPathZip() {
            var fileName = Site.BrowseForFileOpen(
                IntPtr.Zero,
                "Zip Archives (*.zip;*.egg)|*.zip;*.egg|All Files (*.*)|*.*",
                ProjectHome
            );
            if (!string.IsNullOrEmpty(fileName)) {
                _searchPaths.Add(fileName, true);
            }
            return VSConstants.S_OK;
        }

        internal bool IsPythonPathSet() {
            return !string.IsNullOrEmpty(
                Environment.GetEnvironmentVariable(GetInterpreterFactory().Configuration.PathEnvironmentVariable ?? "")
            );
        }

        internal int AddPythonPathToSearchPath() {
            var value = Environment.GetEnvironmentVariable(GetInterpreterFactory().Configuration.PathEnvironmentVariable ?? "");
            if (string.IsNullOrEmpty(value)) {
                return VSConstants.S_OK;
            }

            foreach (var bit in value.Split(';')) {
                if (!string.IsNullOrEmpty(bit)) {
                    _searchPaths.Add(bit, true);
                }
            }
            return VSConstants.S_OK;
        }

        #endregion

        #region Package Installation support

        private int ExecInstallPythonPackage(Dictionary<string, string> args, IList<HierarchyNode> selectedNodes) {
            InterpretersNode selectedInterpreter;
            IPythonInterpreterFactory selectedInterpreterFactory;
            GetSelectedInterpreterOrDefault(selectedNodes, args, out selectedInterpreter, out selectedInterpreterFactory);

            if (selectedInterpreterFactory?.PackageManager == null) {
                if (Utilities.IsInAutomationFunction(Site)) {
                    return VSConstants.E_INVALIDARG;
                }
                MessageBox.Show(Strings.PackageManagementNotSupported, Strings.ProductTitle, MessageBoxButtons.OK);
                return VSConstants.S_OK;
            }

            string name;
            if (args != null && args.TryGetValue("p", out name)) {
                // Don't prompt, just install
                bool elevated = args.ContainsKey("a");
                selectedInterpreterFactory.PackageManager.InstallAsync(
                    PackageSpec.FromArguments(name),
                    new VsPackageManagerUI(Site, elevated),
                    CancellationToken.None
                )
                    .SilenceException<OperationCanceledException>()
                    .HandleAllExceptions(Site)
                    .DoNotWait();
            } else {
                // Open the install UI
                InterpreterList.InterpreterListToolWindow.OpenAt(
                    Site,
                    selectedInterpreterFactory,
                    typeof(EnvironmentsList.PipExtensionProvider)
                );
            }
            return VSConstants.S_OK;
        }

        private int ExecInstallRequirementsTxt(Dictionary<string, string> args, IList<HierarchyNode> selectedNodes) {
            InterpretersNode selectedInterpreter;
            IPythonInterpreterFactory selectedInterpreterFactory;
            GetSelectedInterpreterOrDefault(selectedNodes, args, out selectedInterpreter, out selectedInterpreterFactory);
            if (selectedInterpreterFactory == null || selectedInterpreterFactory.PackageManager == null) {
                if (Utilities.IsInAutomationFunction(Site)) {
                    return VSConstants.E_INVALIDARG;
                }
                MessageBox.Show(Strings.PackageManagementNotSupported, Strings.ProductTitle, MessageBoxButtons.OK);
                return VSConstants.S_OK;
            }

            var txt = PathUtils.GetAbsoluteFilePath(ProjectHome, "requirements.txt");
            var name = "-r " + ProcessOutput.QuoteSingleArgument(txt);
            if (args != null && !args.ContainsKey("y")) {
                if (!ShouldInstallRequirementsTxt(
                    selectedInterpreterFactory.Configuration.Description,
                    txt,
                    Site.GetPythonToolsService().GeneralOptions.ElevatePip
                )) {
                    return VSConstants.S_OK;
                }
            }

            selectedInterpreterFactory.PackageManager.InstallAsync(
                PackageSpec.FromArguments(name),
                new VsPackageManagerUI(Site),
                CancellationToken.None
            ).SilenceException<OperationCanceledException>()
             .HandleAllExceptions(Site, GetType())
             .DoNotWait();

            return VSConstants.S_OK;
        }


        private bool ShouldInstallRequirementsTxt(
            string targetLabel,
            string txt,
            bool elevate
        ) {
            if (!File.Exists(txt)) {
                return false;
            }
            string content;
            try {
                content = File.ReadAllText(txt);
            } catch (Exception ex) {
                if (ex.IsCriticalException()) {
                    throw;
                }
                return false;
            }

            var td = new TaskDialog(Site) {
                Title = Strings.ProductTitle,
                MainInstruction = Strings.ShouldInstallRequirementsTxtHeader,
                Content = Strings.ShouldInstallRequirementsTxtContent,
                ExpandedByDefault = true,
                ExpandedControlText = Strings.ShouldInstallRequirementsTxtExpandedControl,
                CollapsedControlText = Strings.ShouldInstallRequirementsTxtCollapsedControl,
                ExpandedInformation = content,
                AllowCancellation = true
            };

            var install = new TaskDialogButton(Strings.ShouldInstallRequirementsTxtInstallInto.FormatUI(targetLabel)) {
                ElevationRequired = elevate
            };

            td.Buttons.Add(install);
            td.Buttons.Add(TaskDialogButton.Cancel);

            return td.ShowModal() == install;
        }

        private int ExecGenerateRequirementsTxt(Dictionary<string, string> args, IList<HierarchyNode> selectedNodes) {
            InterpretersNode selectedInterpreter;
            IPythonInterpreterFactory selectedInterpreterFactory;
            GetSelectedInterpreterOrDefault(selectedNodes, args, out selectedInterpreter, out selectedInterpreterFactory);
            if (selectedInterpreterFactory?.PackageManager == null) {
                if (Utilities.IsInAutomationFunction(Site)) {
                    return VSConstants.E_INVALIDARG;
                }
                MessageBox.Show(Strings.PackageManagementNotSupported, Strings.ProductTitle, MessageBoxButtons.OK);
                return VSConstants.S_OK;
            }

            GenerateRequirementsTxtAsync(selectedInterpreterFactory)
                .SilenceException<OperationCanceledException>()
                .HandleAllExceptions(Site, GetType())
                .DoNotWait();
            return VSConstants.S_OK;
        }

        private async Task GenerateRequirementsTxtAsync(IPythonInterpreterFactory factory) {
            var projectHome = ProjectHome;
            var txt = PathUtils.GetAbsoluteFilePath(projectHome, "requirements.txt");

            IList<PackageSpec> items = null;

            try {
                items = await factory.PackageManager.GetInstalledPackagesAsync(CancellationToken.None);
            } catch (Exception ex) when (!ex.IsCriticalException()) {
                ex.ReportUnhandledException(Site, GetType(), allowUI: Utilities.IsInAutomationFunction(Site));
                return;
            }

            string[] existing = null;
            bool addNew = false;
            if (File.Exists(txt)) {
                existing = TaskDialog.CallWithRetry(
                    _ => File.ReadAllLines(txt),
                    Site,
                    Strings.ProductTitle,
                    Strings.RequirementsTxtFailedToRead,
                    Strings.ErrorDetail,
                    Strings.Retry,
                    Strings.Cancel
                );

                var td = new TaskDialog(Site) {
                    Title = Strings.ProductTitle,
                    MainInstruction = Strings.RequirementsTxtExists,
                    Content = Strings.RequirementsTxtExistsQuestion,
                    AllowCancellation = true,
                    CollapsedControlText = Strings.RequirementsTxtContentCollapsed,
                    ExpandedControlText = Strings.RequirementsTxtContentExpanded,
                    ExpandedInformation = string.Join(Environment.NewLine, existing)
                };
                var replace = new TaskDialogButton(
                    Strings.RequirementsTxtReplace,
                    Strings.RequirementsTxtReplaceHelp
                );
                var refresh = new TaskDialogButton(
                    Strings.RequirementsTxtRefresh,
                    Strings.RequirementsTxtRefreshHelp
                );
                var update = new TaskDialogButton(
                    Strings.RequirementsTxtUpdate,
                    Strings.RequirementsTxtUpdateHelp
                );
                td.Buttons.Add(replace);
                td.Buttons.Add(refresh);
                td.Buttons.Add(update);
                td.Buttons.Add(TaskDialogButton.Cancel);
                var selection = td.ShowModal();
                if (selection == TaskDialogButton.Cancel) {
                    return;
                } else if (selection == replace) {
                    existing = null;
                } else if (selection == update) {
                    addNew = true;
                }
            }

            if (File.Exists(txt) && !QueryEditFiles(false, txt)) {
                return;
            }

            TaskDialog.CallWithRetry(
                _ => {
                    if (items.Any()) {
                        File.WriteAllLines(txt, MergeRequirements(existing, items, addNew));
                    } else if (existing == null) {
                        File.WriteAllText(txt, "");
                    }
                },
                Site,
                Strings.ProductTitle,
                Strings.RequirementsTxtFailedToWrite,
                Strings.ErrorDetail,
                Strings.Retry,
                Strings.Cancel
            );

            var existingNode = FindNodeByFullPath(txt);
            if (existingNode == null || existingNode.IsNonMemberItem) {
                if (!QueryEditProjectFile(false)) {
                    return;
                }
                existingNode = TaskDialog.CallWithRetry(
                    _ => {
                        ErrorHandler.ThrowOnFailure(AddItem(
                            ID,
                            VSADDITEMOPERATION.VSADDITEMOP_LINKTOFILE,
                            Path.GetFileName(txt),
                            1,
                            new[] { txt },
                            IntPtr.Zero,
                            new VSADDRESULT[1]
                        ));

                        return FindNodeByFullPath(txt);
                    },
                    Site,
                    Strings.ProductTitle,
                    Strings.RequirementsTxtFailedToAddToProject,
                    Strings.ErrorDetail,
                    Strings.Retry,
                    Strings.Cancel
                );
            }
        }

        internal static readonly Regex FindRequirementRegex = new Regex(@"
            (?<!\#.*)       # ensure we are not in a comment
            (?<=\s|\A)      # ensure we are preceded by a space/start of the line
            (?<spec>        # <spec> includes name, version and whitespace
                (?<name>[^\s\#<>=!\-][^\s\#<>=!]*)  # just the name, no whitespace
                (\s*(?<cmp><=|>=|<|>|!=|==)\s*
                    (?<ver>[^\s\#]+)
                )?          # cmp and ver are optional
            )", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace
        );

        internal static IEnumerable<string> MergeRequirements(
            IEnumerable<string> original,
            IEnumerable<PackageSpec> updates,
            bool addNew
        ) {
            if (original == null) {
                foreach (var req in updates.OrderBy(r => r.FullSpec)) {
                    yield return req.FullSpec;
                }
                yield break;
            }

            var existing = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var p in updates) {
                existing[p.Name] = p.FullSpec;
            }

            var seen = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var _line in original) {
                var line = _line;
                foreach (var m in FindRequirementRegex.Matches(line).Cast<Match>()) {
                    string newReq;
                    var name = m.Groups["name"].Value;
                    if (existing.TryGetValue(name, out newReq)) {
                        line = FindRequirementRegex.Replace(line, m2 =>
                            name.Equals(m2.Groups["name"].Value, StringComparison.InvariantCultureIgnoreCase) ?
                                newReq :
                                m2.Value
                        );
                        seen.Add(name);
                    }
                }
                yield return line;
            }

            if (addNew) {
                foreach (var req in existing
                    .Where(kv => !seen.Contains(kv.Key))
                    .Select(kv => kv.Value)
                    .OrderBy(v => v)
                ) {
                    yield return req;
                }
            }
        }

        #endregion

        #region Virtual Env support

        private void ShowAddInterpreter() {
            var service = InterpreterOptions;

            var result = PythonTools.Project.AddInterpreter.ShowDialog(this, service);
            if (result == null) {
                return;
            }

            var toRemove = new HashSet<string>(InterpreterIds);
            var toAdd = new HashSet<string>(result);
            toRemove.ExceptWith(toAdd);
            toAdd.ExceptWith(toRemove);

            if (toAdd.Any() || toRemove.Any()) {
                //Make sure we can edit the project file
                if (!QueryEditProjectFile(false)) {
                    throw Marshal.GetExceptionForHR(VSConstants.OLE_E_PROMPTSAVECANCELLED);
                }
                foreach (var id in toAdd) {
                    AddInterpreter(id);
                }
                foreach (var id in toRemove) {
                    RemoveInterpreterFactory(id);
                }
            }
        }

        private async void ShowAddVirtualEnvironmentWithErrorHandling(bool browseForExisting) {
            var service = Site.GetComponentModel().GetService<IInterpreterRegistryService>();
            var statusBar = (IVsStatusbar)GetService(typeof(SVsStatusbar));
            object index = (short)0;
            statusBar.Animation(1, ref index);
            try {
                await AddVirtualEnvironment.ShowDialog(this, service, browseForExisting);
            } catch (Exception ex) {
                if (ex.IsCriticalException()) {
                    throw;
                }

                statusBar.SetText(Strings.VirtualEnvAddFailed);

                Debug.WriteLine("Failed to add virtual environment.\r\n{0}", ex.InnerException ?? ex);

                try {
                    ActivityLog.LogError(Strings.ProductTitle, (ex.InnerException ?? ex).ToString());
                } catch (InvalidOperationException) {
                    // Activity log may be unavailable
                }
            } finally {
                statusBar.Animation(0, ref index);
            }
        }

        internal async Task<IPythonInterpreterFactory> CreateOrAddVirtualEnvironment(
            IInterpreterRegistryService service,
            bool create,
            string path,
            IPythonInterpreterFactory baseInterp,
            bool preferVEnv = false
        ) {
            if (create && preferVEnv) {
                await VirtualEnv.CreateWithVEnv(Site, baseInterp, path);
            } else if (create) {
                await VirtualEnv.CreateAndInstallDependencies(Site, baseInterp, path);
            }

            var rootPath = PathUtils.GetAbsoluteDirectoryPath(ProjectHome, path);
            foreach (var existingConfig in InterpreterConfigurations) {
                var rootPrefix = PathUtils.EnsureEndSeparator(existingConfig.PrefixPath);

                if (rootPrefix.Equals(rootPath, StringComparison.OrdinalIgnoreCase)) {
                    return InterpreterRegistry.FindInterpreter(existingConfig.Id);
                }
            }

            string id = GetNewEnvironmentName(path);

            var config = VirtualEnv.FindInterpreterConfiguration(id, path, service, baseInterp);
            if (config == null || !File.Exists(config.InterpreterPath)) {
                throw new InvalidOperationException(Strings.VirtualEnvAddFailed);
            }

            if (!QueryEditProjectFile(false)) {
                throw Marshal.GetExceptionForHR(VSConstants.OLE_E_PROMPTSAVECANCELLED);
            }

            try {
                AddInterpreterReference(config);
            } catch (ArgumentException ex) {
                TaskDialog.ForException(Site, ex, issueTrackerUrl: IssueTrackerUrl).ShowModal();
                return null;
            }
            return InterpreterRegistry.FindInterpreter(id);
        }

        private string GetNewEnvironmentName(string path) {
            string id = MSBuildProjectInterpreterFactoryProvider.GetInterpreterId(
                BuildProject.FullPath,
                Path.GetFileName(PathUtils.TrimEndSeparator(path))
            );

            var interpReg = Site.GetComponentModel().GetService<IInterpreterRegistryService>();

            int counter = 1;
            while (interpReg.FindConfiguration(id) != null) {
                id = MSBuildProjectInterpreterFactoryProvider.GetInterpreterId(
                    BuildProject.FullPath,
                    Path.GetFileName(PathUtils.TrimEndSeparator(path)) + counter++
                );
            }

            return id;
        }

        /// <summary>
        /// Removes a given interpreter from the project, optionally deleting
        /// its prefix path from disk.
        /// </summary>
        internal async void RemoveInterpreter(IPythonInterpreterFactory factory, bool removeFromStorage = false) {
            Utilities.ArgumentNotNull("factory", factory);

            //Make sure we can edit the project file
            if (!QueryEditProjectFile(false)) {
                throw Marshal.GetExceptionForHR(VSConstants.OLE_E_PROMPTSAVECANCELLED);
            }
            RemoveInterpreterFactory(factory);

            var path = factory.Configuration.PrefixPath;
            if (removeFromStorage && Directory.Exists(path)) {
                var t = Task.Run(() => {
                    for (int retries = 5; Directory.Exists(path) && retries > 0; --retries) {
                        try {
                            Directory.Delete(path, true);
                            return true;
                        } catch (DirectoryNotFoundException) {
                            return true;
                        } catch (IOException) {
                        } catch (UnauthorizedAccessException) {
                        }
                        // Allow some time for operations to fail and release
                        // locks before trying to delete the directory again.
                        Thread.Sleep(100);
                    }
                    return !Directory.Exists(path);
                }).HandleAllExceptions(Site, GetType());

                if (!await t) {
                    MessageBox.Show(Strings.EnvironmentDeleteError.FormatUI(path), Strings.ProductTitle);
                }
            }
        }

        #endregion

        public override Guid SharedCommandGuid {
            get {
                return GuidList.guidPythonToolsCmdSet;
            }
        }
        
        public PythonProject Project {
            get {
                return _pythonProject;
            }
        }

        protected internal override int ShowAllFiles() {
            int hr = base.ShowAllFiles();
            BoldActiveEnvironment();
            return hr;
        }

        void IAzureRoleProject.AddedAsRole(object azureProjectHierarchy, string roleType) {
            var hier = azureProjectHierarchy as IVsHierarchy;

            if (hier == null) {
                return;
            }

            Site.GetUIThread().Invoke(() => {
                UpdateServiceDefinition(hier, roleType, Caption, Site);
                SetProjectProperty(PythonConstants.SuppressCollectPythonCloudServiceFiles, "false");
            });
        }

        private static bool TryGetItemId(object obj, out uint id) {
            const uint nil = (uint)VSConstants.VSITEMID.Nil;
            id = obj as uint? ?? nil;
            if (id == nil) {
                var asInt = obj as int?;
                if (asInt.HasValue) {
                    id = unchecked((uint)asInt.Value);
                }
            }
            return id != nil;
        }

        /// <summary>
        /// Updates the ServiceDefinition.csdef file in
        /// <paramref name="project"/> to include the default startup and
        /// runtime tasks for Python projects.
        /// </summary>
        /// <param name="project">
        /// The Cloud Service project to update.
        /// </param>
        /// <param name="roleType">
        /// The type of role being added, either "Web" or "Worker".
        /// </param>
        /// <param name="projectName">
        /// The name of the role. This typically matches the Caption property.
        /// </param>
        /// <param name="site">
        /// VS service provider.
        /// </param>
        internal static void UpdateServiceDefinition(
            IVsHierarchy project,
            string roleType,
            string projectName,
            IServiceProvider site
        ) {
            Utilities.ArgumentNotNull("project", project);

            object obj;
            ErrorHandler.ThrowOnFailure(project.GetProperty(
                (uint)VSConstants.VSITEMID.Root,
                (int)__VSHPROPID.VSHPROPID_FirstChild,
                out obj
            ));

            uint id;
            while (TryGetItemId(obj, out id)) {
                Guid itemType;
                string mkDoc;

                if (ErrorHandler.Succeeded(project.GetGuidProperty(id, (int)__VSHPROPID.VSHPROPID_TypeGuid, out itemType)) &&
                    itemType == VSConstants.GUID_ItemType_PhysicalFile &&
                    ErrorHandler.Succeeded(project.GetProperty(id, (int)__VSHPROPID.VSHPROPID_Name, out obj)) &&
                    "ServiceDefinition.csdef".Equals(obj as string, StringComparison.InvariantCultureIgnoreCase) &&
                    ErrorHandler.Succeeded(project.GetCanonicalName(id, out mkDoc)) &&
                    !string.IsNullOrEmpty(mkDoc)
                ) {
                    // We have found the file
                    var rdt = site.GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;

                    IVsHierarchy docHier;
                    uint docId, docCookie;
                    IntPtr pDocData;

                    bool updateFileOnDisk = true;

                    if (ErrorHandler.Succeeded(rdt.FindAndLockDocument(
                        (uint)_VSRDTFLAGS.RDT_EditLock,
                        mkDoc,
                        out docHier,
                        out docId,
                        out pDocData,
                        out docCookie
                    ))) {
                        try {
                            if (pDocData != IntPtr.Zero) {
                                try {
                                    // File is open, so edit it through the document
                                    UpdateServiceDefinition(
                                        Marshal.GetObjectForIUnknown(pDocData) as IVsTextLines,
                                        roleType,
                                        projectName
                                    );

                                    ErrorHandler.ThrowOnFailure(rdt.SaveDocuments(
                                        (uint)__VSRDTSAVEOPTIONS.RDTSAVEOPT_ForceSave,
                                        docHier,
                                        docId,
                                        docCookie
                                    ));

                                    updateFileOnDisk = false;
                                } catch (ArgumentException) {
                                } catch (InvalidOperationException) {
                                } finally {
                                    Marshal.Release(pDocData);
                                }
                            }
                        } finally {
                            ErrorHandler.ThrowOnFailure(rdt.UnlockDocument(
                                (uint)_VSRDTFLAGS.RDT_Unlock_SaveIfDirty | (uint)_VSRDTFLAGS.RDT_RequestUnlock,
                                docCookie
                            ));
                        }
                    }

                    if (updateFileOnDisk) {
                        // File is not open, so edit it on disk
                        FileStream stream = null;
                        try {
                            UpdateServiceDefinition(mkDoc, roleType, projectName);
                        } finally {
                            if (stream != null) {
                                stream.Close();
                            }
                        }
                    }

                    break;
                }

                if (ErrorHandler.Failed(project.GetProperty(id, (int)__VSHPROPID.VSHPROPID_NextSibling, out obj))) {
                    break;
                }
            }
        }

        private class StringWriterWithEncoding : StringWriter {
            private readonly Encoding _encoding;

            public StringWriterWithEncoding(Encoding encoding) {
                _encoding = encoding;
            }

            public override Encoding Encoding {
                get { return _encoding; }
            }
        }

        private static void UpdateServiceDefinition(IVsTextLines lines, string roleType, string projectName) {
            if (lines == null) {
                throw new ArgumentException("lines");
            }

            int lastLine, lastIndex;
            string text;

            ErrorHandler.ThrowOnFailure(lines.GetLastLineIndex(out lastLine, out lastIndex));
            ErrorHandler.ThrowOnFailure(lines.GetLineText(0, 0, lastLine, lastIndex, out text));

            var doc = new XmlDocument();
            doc.LoadXml(text);

            UpdateServiceDefinition(doc, roleType, projectName);

            var encoding = Encoding.UTF8;

            var userData = lines as IVsUserData;
            if (userData != null) {
                var guid = VSConstants.VsTextBufferUserDataGuid.VsBufferEncodingVSTFF_guid;
                object data;
                int cp;
                if (ErrorHandler.Succeeded(userData.GetData(ref guid, out data)) &&
                    (cp = (data as int? ?? (int)(data as uint? ?? 0)) & (int)__VSTFF.VSTFF_CPMASK) != 0) {
                    try {
                        encoding = Encoding.GetEncoding(cp);
                    } catch (NotSupportedException) {
                    } catch (ArgumentException) {
                    }
                }
            }

            var sw = new StringWriterWithEncoding(encoding);
            doc.Save(XmlWriter.Create(
                sw,
                new XmlWriterSettings {
                    Indent = true,
                    IndentChars = " ",
                    NewLineHandling = NewLineHandling.Entitize,
                    Encoding = encoding
                }
            ));

            var sb = sw.GetStringBuilder();
            var len = sb.Length;
            var pStr = Marshal.StringToCoTaskMemUni(sb.ToString());

            try {
                ErrorHandler.ThrowOnFailure(lines.ReplaceLines(0, 0, lastLine, lastIndex, pStr, len, new TextSpan[1]));
            } finally {
                Marshal.FreeCoTaskMem(pStr);
            }
        }

        private class VsPythonProject : PythonProject {
            private readonly PythonProjectNode _node;
            public VsPythonProject(PythonProjectNode node) {
                _node = node;
            }

            public override string ProjectHome {
                get {
                    return _node.ProjectHome;
                }
            }

            public override event EventHandler ProjectAnalyzerChanged {
                add { _node.ProjectAnalyzerChanged += value; }
                remove { _node.ProjectAnalyzerChanged -= value; }
            }

            public override IPythonInterpreterFactory GetInterpreterFactory() {
                return _node.GetInterpreterFactory();
            }

            public override LaunchConfiguration GetLaunchConfigurationOrThrow() {
                return _node.GetLaunchConfigurationOrThrow();
            }

            public override ProjectAnalyzer Analyzer {
                get {
                    return _node.GetAnalyzer();
                }
            }

            public override string GetProperty(string name) {
                return _node.GetProjectProperty(name);
            }

            public override string GetUnevaluatedProperty(string name) {
                return _node.GetUnevaluatedProperty(name);
            }

            public override void SetProperty(string name, string value) {
                _node.SetProjectProperty(name, value);
            }
        }

        private static void UpdateServiceDefinition(string path, string roleType, string projectName) {
            var doc = new XmlDocument();
            doc.Load(path);

            UpdateServiceDefinition(doc, roleType, projectName);

            doc.Save(XmlWriter.Create(
                path,
                new XmlWriterSettings {
                    Indent = true,
                    IndentChars = " ",
                    NewLineHandling = NewLineHandling.Entitize,
                    Encoding = Encoding.UTF8
                }
            ));
        }

        /// <summary>
        /// Modifies the provided XML document to contain the service definition
        /// nodes needed for the specified project.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="roleType"/> is not one of "Web" or "Worker".
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// A required element is missing from the document.
        /// </exception>
        internal static void UpdateServiceDefinition(XmlDocument doc, string roleType, string projectName) {
            bool isWeb = roleType == "Web";
            bool isWorker = roleType == "Worker";
            if (isWeb == isWorker) {
                throw new ArgumentException("Unknown role type: " + (roleType ?? "(null)"), "roleType");
            }

            var nav = doc.CreateNavigator();

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("sd", "http://schemas.microsoft.com/ServiceHosting/2008/10/ServiceDefinition");

            var role = nav.SelectSingleNode(string.Format(
                "/sd:ServiceDefinition/sd:{0}Role[@name='{1}']", roleType, projectName
            ), ns);

            if (role == null) {
                throw new InvalidOperationException("Missing role entry");
            }

            var startup = role.SelectSingleNode("sd:Startup", ns);
            if (startup != null) {
                startup.DeleteSelf();
            }

            role.AppendChildElement(null, "Startup", null, null);
            startup = role.SelectSingleNode("sd:Startup", ns);
            if (startup == null) {
                throw new InvalidOperationException("Missing Startup entry");
            }

            startup.AppendChildElement(null, "Task", null, null);
            var task = startup.SelectSingleNode("sd:Task", ns);
            AddEnvironmentNode(task, ns);
            task.CreateAttribute(null, "executionContext", null, "elevated");
            task.CreateAttribute(null, "taskType", null, "simple");

            if (isWeb) {
                task.CreateAttribute(null, "commandLine", null, "ps.cmd ConfigureCloudService.ps1");
            } else if (isWorker) {
                task.CreateAttribute(null, "commandLine", null, "bin\\ps.cmd ConfigureCloudService.ps1");

                var runtime = role.SelectSingleNode("sd:Runtime", ns);
                if (runtime != null) {
                    runtime.DeleteSelf();
                }
                role.AppendChildElement(null, "Runtime", null, null);
                runtime = role.SelectSingleNode("sd:Runtime", ns);
                AddEnvironmentNode(runtime, ns);
                runtime.AppendChildElement(null, "EntryPoint", null, null);
                var ep = runtime.SelectSingleNode("sd:EntryPoint", ns);
                ep.AppendChildElement(null, "ProgramEntryPoint", null, null);
                var pep = ep.SelectSingleNode("sd:ProgramEntryPoint", ns);
                pep.CreateAttribute(null, "commandLine", null, "bin\\ps.cmd LaunchWorker.ps1 worker.py");
                pep.CreateAttribute(null, "setReadyOnProcessStart", null, "true");
            }
        }

        private static void AddEnvironmentNode(XPathNavigator nav, IXmlNamespaceResolver ns) {
            nav.AppendChildElement(null, "Environment", null, null);
            nav = nav.SelectSingleNode("sd:Environment", ns);
            nav.AppendChildElement(null, "Variable", null, null);
            var children = nav.SelectChildren(XPathNodeType.Element);
            if (children.MoveNext()) {
                var emulatedNode = children.Current;
                emulatedNode.CreateAttribute(null, "name", null, "EMULATED");
                emulatedNode.AppendChildElement(null, "RoleInstanceValue", null, null);
                emulatedNode = emulatedNode.SelectSingleNode("sd:RoleInstanceValue", ns);
                emulatedNode.CreateAttribute(null, "xpath", null, "/RoleEnvironment/Deployment/@emulated");
            }
        }
    }
}
