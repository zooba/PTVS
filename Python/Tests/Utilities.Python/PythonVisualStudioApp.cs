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
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using EnvDTE;
using Microsoft.PythonTools;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Options;
using Microsoft.PythonTools.Parsing;
using Microsoft.PythonTools.Project;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudioTools;
using Microsoft.Win32;
using TestUtilities.Python;
using Process = System.Diagnostics.Process;

namespace TestUtilities.UI.Python {
    class PythonVisualStudioApp : VisualStudioApp {
        private bool _deletePerformanceSessions;
        private PythonPerfExplorer _perfTreeView;
        private PythonPerfToolBar _perfToolBar;
        
        public PythonVisualStudioApp(DTE dte = null)
            : base(dte) {

            var service = ServiceProvider.GetPythonToolsService();
            Assert.IsNotNull(service, "Failed to get PythonToolsService");
            
            // Disable AutoListIdentifiers for tests
            var ao = service.AdvancedOptions;
            Assert.IsNotNull(ao, "Failed to get AdvancedOptions");
            var oldALI = ao.AutoListIdentifiers;
            ao.AutoListIdentifiers = false;

            var orwoodProp = Dte.Properties["Environment", "ProjectsAndSolution"].Item("OnRunWhenOutOfDate");
            Assert.IsNotNull(orwoodProp, "Failed to get OnRunWhenOutOfDate property");
            var oldOrwood = orwoodProp.Value;
            orwoodProp.Value = 1;

            OnDispose(() => {
                ao.AutoListIdentifiers = oldALI;
                orwoodProp.Value = oldOrwood;
            });
        }

        protected override void Dispose(bool disposing) {
            if (!IsDisposed) {
                try {
                    InteractiveWindow.CloseAll(this);
                } catch (Exception ex) {
                    Console.WriteLine("Error while closing all interactive windows");
                    Console.WriteLine(ex);
                }

                if (_deletePerformanceSessions) {
                    try {
                        dynamic profiling = Dte.GetObject("PythonProfiling");

                        for (dynamic session = profiling.GetSession(1);
                            session != null;
                            session = profiling.GetSession(1)) {
                            profiling.RemoveSession(session, true);
                        }
                    } catch (Exception ex) {
                        Console.WriteLine("Error while cleaning up profiling sessions");
                        Console.WriteLine(ex);
                    }
                }
            }
            base.Dispose(disposing);
        }

        // Constants for passing to CreateProject
        private const string _templateLanguageName = "Python";
        public static string TemplateLanguageName {
            get {
                return _templateLanguageName;
            }
        }

        public const string PythonApplicationTemplate = "ConsoleAppProject.zip";
        public const string EmptyWebProjectTemplate = "EmptyWebProject.zip";
        public const string BottleWebProjectTemplate = "WebProjectBottle.zip";
        public const string FlaskWebProjectTemplate = "WebProjectFlask.zip";
        public const string DjangoWebProjectTemplate = "DjangoProject.zip";
        public const string WorkerRoleProjectTemplate = "WorkerRoleProject.zip";
        
        public const string EmptyFileTemplate = "EmptyPyFile.zip";
        public const string WebRoleSupportTemplate = "AzureCSWebRole.zip";
        public const string WorkerRoleSupportTemplate = "AzureCSWorkerRole.zip";

        /// <summary>
        /// Opens and activates the solution explorer window.
        /// </summary>
        public void OpenPythonPerformance() {
            try {
                _deletePerformanceSessions = true;
                Dte.ExecuteCommand("Python.PerformanceExplorer");
            } catch {
                // If the package is not loaded yet then the command may not
                // work. Force load the package by opening the Launch dialog.
                using (var dialog = new PythonPerfTarget(OpenDialogWithDteExecuteCommand("Python.LaunchProfiling"))) {
                }
                Dte.ExecuteCommand("Python.PerformanceExplorer");
            }
        }

        /// <summary>
        /// Opens and activates the solution explorer window.
        /// </summary>
        public PythonPerfTarget LaunchPythonProfiling() {
            _deletePerformanceSessions = true;
            return new PythonPerfTarget(OpenDialogWithDteExecuteCommand("Python.LaunchProfiling"));
        }

        /// <summary>
        /// Provides access to the Python profiling tree view.
        /// </summary>
        public PythonPerfExplorer PythonPerformanceExplorerTreeView {
            get {
                if (_perfTreeView == null) {
                    var element = Element.FindFirst(TreeScope.Descendants,
                        new AndCondition(
                            new PropertyCondition(
                                AutomationElement.ClassNameProperty,
                                "SysTreeView32"
                            ),
                            new PropertyCondition(
                                AutomationElement.NameProperty,
                                "Python Performance"
                            )
                        )
                    );
                    _perfTreeView = new PythonPerfExplorer(element);
                }
                return _perfTreeView;
            }
        }

        /// <summary>
        /// Provides access to the Python profiling tool bar
        /// </summary>
        public PythonPerfToolBar PythonPerformanceExplorerToolBar {
            get {
                if (_perfToolBar == null) {
                    var element = Element.FindFirst(TreeScope.Descendants,
                        new AndCondition(
                            new PropertyCondition(
                                AutomationElement.ClassNameProperty,
                                "ToolBar"
                            ),
                            new PropertyCondition(
                                AutomationElement.NameProperty,
                                "Python Performance"
                            )
                        )
                    );
                    _perfToolBar = new PythonPerfToolBar(element);
                }
                return _perfToolBar;
            }
        }

        public InteractiveWindow GetInteractiveWindow(string title) {
            AutomationElement element = null;
            for (int i = 0; i < 5 && element == null; i++) {
                element = Element.FindFirst(TreeScope.Descendants,
                    new AndCondition(
                        new PropertyCondition(
                            AutomationElement.NameProperty,
                            title
                        ),
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty,
                            ControlType.Pane
                        )
                    )
                );
                if (element == null) {
                    System.Threading.Thread.Sleep(100);
                }
            }

            if (element == null) {
                DumpVS();
                return null;
            }

            return new InteractiveWindow(
                title,
                element.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.AutomationIdProperty,
                        "WpfTextView"
                    )
                ),
                this
            );

        }

        internal Document WaitForDocument(string docName) {
            for (int i = 0; i < 100; i++) {
                try {
                    return Dte.Documents.Item(docName);
                } catch {
                    System.Threading.Thread.Sleep(100);
                }
            }
            throw new InvalidOperationException("Document not opened: " + docName);
        }

        /// <summary>
        /// Selects the given interpreter as the default.
        /// </summary>
        /// <remarks>
        /// This method should always be called as a using block.
        /// </remarks>
        public DefaultInterpreterSetter SelectDefaultInterpreter(PythonVersion python) {
            return new DefaultInterpreterSetter(
                InterpreterService.FindInterpreter(python.Id, python.Version.ToVersion()),
                ServiceProvider
            );
        }

        public DefaultInterpreterSetter SelectDefaultInterpreter(PythonVersion interp, string installPackages) {
            interp.AssertInstalled();
            if (interp.IsIronPython && !string.IsNullOrEmpty(installPackages)) {
                Assert.Inconclusive("Package installation not supported on IronPython");
            }

            var interpreterService = InterpreterService;
            var factory = interpreterService.FindInterpreter(interp.Id, interp.Configuration.Version);
            var defaultInterpreterSetter = new DefaultInterpreterSetter(factory);

            try {
                if (!string.IsNullOrEmpty(installPackages)) {
                    Pip.InstallPip(ServiceProvider, factory, false).Wait();
                    foreach (var package in installPackages.Split(' ', ',', ';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s))) {
                        Pip.Install(ServiceProvider, factory, package, false).Wait();
                    }
                }

                var result = defaultInterpreterSetter;
                defaultInterpreterSetter = null;
                return result;
            } finally {
                if (defaultInterpreterSetter != null) {
                    defaultInterpreterSetter.Dispose();
                }
            }
        }


        public IInterpreterOptionsService InterpreterService {
            get {
                var model = GetService<IComponentModel>(typeof(SComponentModel));
                var service = model.GetService<IInterpreterOptionsService>();
                Assert.IsNotNull(service, "Unable to get InterpreterOptionsService");
                return service;
            }
        }

        public TreeNode CreateVirtualEnvironment(EnvDTE.Project project, out string envName) {
            string dummy;
            return CreateVirtualEnvironment(project, out envName, out dummy);
        }

        public TreeNode CreateVirtualEnvironment(EnvDTE.Project project, out string envName, out string envPath) {
            var environmentsNode = OpenSolutionExplorer().FindChildOfProject(project, Strings.Environments);
            environmentsNode.Select();

            using (var pss = new ProcessScope("python")) {
                using (var createVenv = AutomationDialog.FromDte(this, "Python.AddVirtualEnvironment")) {
                    envPath = new TextBox(createVenv.FindByAutomationId("VirtualEnvPath")).GetValue();
                    var baseInterp = new ComboBox(createVenv.FindByAutomationId("BaseInterpreter")).GetSelectedItemName();

                    envName = string.Format("{0} ({1})", envPath, baseInterp);

                    Console.WriteLine("Expecting environment named: {0}", envName);

                    // Force a wait for the view to be updated.
                    var wnd = (DialogWindowVersioningWorkaround)HwndSource.FromHwnd(
                        new IntPtr(createVenv.Element.Current.NativeWindowHandle)
                    ).RootVisual;
                    wnd.Dispatcher.Invoke(() => {
                        var view = (AddVirtualEnvironmentView)wnd.DataContext;
                        return view.UpdateInterpreter(view.BaseInterpreter);
                    }).Wait();

                    createVenv.ClickButtonByAutomationId("Create");
                    createVenv.ClickButtonAndClose("Close", nameIsAutomationId: true);
                }

                var nowRunning = pss.WaitForNewProcess(TimeSpan.FromMinutes(1));
                if (nowRunning == null || !nowRunning.Any()) {
                    Assert.Fail("Failed to see python process start to create virtualenv");
                }
                foreach (var p in nowRunning) {
                    if (p.HasExited) {
                        continue;
                    }
                    try {
                        p.WaitForExit(30000);
                    } catch (Win32Exception ex) {
                        Console.WriteLine("Error waiting for process ID {0}\n{1}", p.Id, ex);
                    }
                }
            }

            try {
                return OpenSolutionExplorer().WaitForChildOfProject(project, Strings.Environments, envName);
            } finally {
                var text = GetOutputWindowText("General");
                if (!string.IsNullOrEmpty(text)) {
                    Console.WriteLine("** Output Window text");
                    Console.WriteLine(text);
                    Console.WriteLine("***");
                    Console.WriteLine();
                }
            }
        }

        public TreeNode AddExistingVirtualEnvironment(EnvDTE.Project project, string envPath, out string envName) {
            var environmentsNode = OpenSolutionExplorer().FindChildOfProject(project, Strings.Environments);
            environmentsNode.Select();

            using (var createVenv = AutomationDialog.FromDte(this, "Python.AddVirtualEnvironment")) {
                new TextBox(createVenv.FindByAutomationId("VirtualEnvPath")).SetValue(envPath);
                var baseInterp = new ComboBox(createVenv.FindByAutomationId("BaseInterpreter")).GetSelectedItemName();

                envName = string.Format("{0} ({1})", Path.GetFileName(envPath), baseInterp);

                Console.WriteLine("Expecting environment named: {0}", envName);

                createVenv.ClickButtonAndClose("Add", nameIsAutomationId: true);
            }

            return OpenSolutionExplorer().WaitForChildOfProject(project, Strings.Environments, envName);
        }

        public IPythonOptions Options {
            get {
                return (IPythonOptions)Dte.GetObject("VsPython");
            }
        }

    }
}
