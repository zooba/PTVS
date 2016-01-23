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
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;
using Microsoft.PythonTools.Debugger.DebugEngine;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.PythonTools.Project.Web {
    /// <summary>
    /// Web launcher.  This wraps the default launcher and provides it with a
    /// different IPythonProject which launches manage.py with the appropriate
    /// options.  Upon a successful launch we will then automatically load the
    /// appropriate page into the users web browser.
    /// </summary>
    class PythonWebLauncher : IProjectLauncher2 {
        private int? _testServerPort;

        public const string RunWebServerCommand = "PythonRunWebServerCommand";
        public const string DebugWebServerCommand = "PythonDebugWebServerCommand";

        public const string RunWebServerTargetProperty = "PythonRunWebServerCommand";
        public const string RunWebServerTargetTypeProperty = "PythonRunWebServerCommandType";
        public const string RunWebServerArgumentsProperty = "PythonRunWebServerCommandArguments";
        public const string RunWebServerEnvironmentProperty = "PythonRunWebServerCommandEnvironment";

        public const string DebugWebServerTargetProperty = "PythonDebugWebServerCommand";
        public const string DebugWebServerTargetTypeProperty = "PythonDebugWebServerCommandType";
        public const string DebugWebServerArgumentsProperty = "PythonDebugWebServerCommandArguments";
        public const string DebugWebServerEnvironmentProperty = "PythonDebugWebServerCommandEnvironment";

        private readonly IPythonProject _project;
        private readonly IAsyncCommand _runServerCommand;
        private readonly IAsyncCommand _debugServerCommand;
        private readonly PythonToolsService _pyService;
        private readonly IServiceProvider _serviceProvider;

        public PythonWebLauncher(IServiceProvider serviceProvider, PythonToolsService pyService, IPythonProject project) {
            _pyService = pyService;
            _project = project;
            _serviceProvider = serviceProvider;

            var project2 = project as IPythonProject2;
            if (project2 != null) {
                // The provider may return its own object, but the web launcher only
                // supports instances of CustomCommand.
                _runServerCommand = project2.FindCommand(RunWebServerCommand);
                _debugServerCommand = project2.FindCommand(DebugWebServerCommand);
            }

            var portNumber = _project.GetProperty(PythonConstants.WebBrowserPortSetting);
            int portNum;
            if (Int32.TryParse(portNumber, out portNum)) {
                _testServerPort = portNum;
            }
        }

        #region IPythonLauncher Members

        private CommandStartInfo GetStartInfo(bool debug, bool runUnknownCommands = true) {
            var cmd = debug ? _debugServerCommand : _runServerCommand;
            var customCmd = cmd as CustomCommand;
            if (customCmd == null && cmd != null) {
                // We have a command we don't understand, so we'll execute it
                // but won't start debugging. The (presumably) flavored project
                // that provided the command is responsible for handling the
                // attach.
                if (runUnknownCommands) {
                    cmd.Execute(null);
                }
                return null;
            }

            CommandStartInfo startInfo = null;
            var project2 = _project as IPythonProject2;
            if (customCmd != null && project2 != null) {
                // We have one of our own commands, so let's use the actual
                // start info.
                try {
                    startInfo = customCmd.GetStartInfo(project2);
                } catch (InvalidOperationException ex) {
                    var target = _project.GetProperty(debug ?
                        DebugWebServerTargetProperty :
                        RunWebServerTargetProperty
                    );
                    if (string.IsNullOrEmpty(target) && !File.Exists(_project.GetStartupFile())) {
                        // The exception was raised because no startup file
                        // is set.
                        throw new InvalidOperationException(Strings.NoStartupFileAvailable, ex);
                    } else {
                        throw;
                    }
                }
            }

            if (startInfo == null) {
                if (!File.Exists(_project.GetStartupFile())) {
                    throw new InvalidOperationException(Strings.NoStartupFileAvailable);
                }

                // No command, so set up a startInfo that looks like the default
                // launcher.
                startInfo = new CommandStartInfo {
                    Filename = _project.GetStartupFile(),
                    Arguments = _project.GetProperty(CommonConstants.CommandLineArguments) ?? string.Empty,
                    WorkingDirectory = _project.GetWorkingDirectory(),
                    EnvironmentVariables = null,
                    TargetType = "script",
                    ExecuteIn = "console"
                };
            }

            return startInfo;
        }

        public int LaunchProject(bool debug) {
            var startInfo = GetStartInfo(debug);
            var props = PythonProjectLaunchProperties.Create(_project, _serviceProvider, null);

            if (props.GetIsWindowsApplication() ?? false) {
                // Must run hidden if running with pythonw
                startInfo.ExecuteIn = "hidden";
            }

            var env = startInfo.EnvironmentVariables;
            if (env == null) {
                env = startInfo.EnvironmentVariables = new Dictionary<string, string>();
            }

            string value;
            if (!env.TryGetValue("SERVER_HOST", out value) || string.IsNullOrEmpty(value)) {
                env["SERVER_HOST"] = "localhost";
            }
            int dummyInt;
            if (!env.TryGetValue("SERVER_PORT", out value) ||
                string.IsNullOrEmpty(value) ||
                !int.TryParse(value, out dummyInt)) {
                env["SERVER_PORT"] = TestServerPortString;
            }

            if (debug) {
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.Launch, 1);

                using (var dsi = CreateDebugTargetInfo(startInfo, props)) {
                    dsi.Launch(_serviceProvider);
                }
            } else {
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.Launch, 0);

                var psi = CreateProcessStartInfo(startInfo, props);

                var process = Process.Start(psi);
                if (process != null) {
                    StartBrowser(GetFullUrl(), () => process.HasExited);
                }
            }

            return VSConstants.S_OK;
        }

        public int LaunchFile(string file, bool debug) {
            return LaunchFile(file, debug, null);
        }

        public int LaunchFile(string file, bool debug, IProjectLaunchProperties props) {
            var startInfo = GetStartInfo(debug, runUnknownCommands: false);
            
            return new DefaultPythonLauncher(_serviceProvider, _pyService, _project).LaunchFile(
                file,
                debug,
                props == null ? startInfo : PythonProjectLaunchProperties.Merge(props, startInfo)
            );
        }


        private string GetInterpreterPath(bool isWindows) {
            string result;
            result = (_project.GetProperty(PythonConstants.InterpreterPathSetting) ?? string.Empty).Trim();
            if (!String.IsNullOrEmpty(result)) {
                result = PathUtils.GetAbsoluteFilePath(_project.ProjectDirectory, result);

                if (!File.Exists(result)) {
                    throw new FileNotFoundException(Strings.DebugLaunchInterpreterMissing_Path.FormatUI(result));
                }

                return result;
            }

            IPythonInterpreterFactory factory;
            var ipp3 = _project as IPythonProject3;
            if (ipp3 != null) {
                factory = ipp3.GetInterpreterFactoryOrThrow();
            } else {
                factory = _project.GetInterpreterFactory();

                if (factory == null) {
                    throw new NoInterpretersException();
                }

                var interpreterService = _serviceProvider.GetComponentModel().GetService<IInterpreterOptionsService>();
                if (interpreterService == null || factory == interpreterService.NoInterpretersValue) {
                    throw new NoInterpretersException();
                }
            }

            return isWindows ?
                factory.Configuration.WindowsInterpreterPath :
                factory.Configuration.InterpreterPath;
        }

        private void StartBrowser(string url, Func<bool> shortCircuitPredicate) {
            Uri uri;
            if (!String.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out uri)) {
                OnPortOpenedHandler.CreateHandler(
                    uri.Port,
                    shortCircuitPredicate: shortCircuitPredicate,
                    action: () => {
                        var web = _serviceProvider.GetService(typeof(SVsWebBrowsingService)) as IVsWebBrowsingService;
                        if (web == null) {
                            PythonToolsPackage.OpenWebBrowser(url);
                            return;
                        }

                        ErrorHandler.ThrowOnFailure(
                            web.CreateExternalWebBrowser(
                                (uint)__VSCREATEWEBBROWSER.VSCWB_ForceNew,
                                VSPREVIEWRESOLUTION.PR_Default,
                                url
                            )
                        );
                    }
                );
            }
        }

        sealed class DebugTargetInfo : IDisposable {
            public VsDebugTargetInfo Info;

            public DebugTargetInfo() {
                Info = new VsDebugTargetInfo();
                Info.cbSize = (uint)Marshal.SizeOf(Info);
            }

            private static string UnquotePath(string p) {
                if (string.IsNullOrEmpty(p) || !p.StartsWith("\"") || !p.EndsWith("\"")) {
                    return p;
                }
                return p.Substring(1, p.Length - 2);
            }

            public string Validate() {
                if (!Directory.Exists(UnquotePath(Info.bstrCurDir))) {
                    return Strings.DebugLaunchWorkingDirectoryMissing.FormatUI(Info.bstrCurDir);
                }

                var exe = UnquotePath(Info.bstrExe);
                if (string.IsNullOrEmpty(exe)) {
                    return Strings.DebugLaunchInterpreterMissing;
                }
                if (!File.Exists(exe)) {
                    return Strings.DebugLaunchInterpreterMissing.FormatUI(exe);
                }

                return null;
            }

            public void Launch(IServiceProvider provider) {
                var error = Validate();
                if (!string.IsNullOrEmpty(error)) {
                    MessageBox.Show(error, Strings.ProductTitle);
                    return;
                }

                VsShellUtilities.LaunchDebugger(provider, Info);
            }

            public void Dispose() {
                if (Info.pClsidList != IntPtr.Zero) {
                    Marshal.FreeCoTaskMem(Info.pClsidList);
                    Info.pClsidList = IntPtr.Zero;
                }
            }
        }

        private IEnumerable<string> GetGlobalDebuggerOptions(bool allowPauseAtEnd, bool alwaysPauseAtEnd) {
            var options = _pyService.DebuggerOptions;

            if (alwaysPauseAtEnd || allowPauseAtEnd && options.WaitOnAbnormalExit) {
                yield return AD7Engine.WaitOnAbnormalExitSetting + "=True";
            }
            if (alwaysPauseAtEnd || allowPauseAtEnd && options.WaitOnNormalExit) {
                yield return AD7Engine.WaitOnNormalExitSetting + "=True";
            }
            if (options.TeeStandardOutput) {
                yield return AD7Engine.RedirectOutputSetting + "=True";
            }
            if (options.BreakOnSystemExitZero) {
                yield return AD7Engine.BreakSystemExitZero + "=True";
            }
            if (options.DebugStdLib) {
                yield return AD7Engine.DebugStdLib + "=True";
            }
        }

        private IEnumerable<string> GetProjectDebuggerOptions() {
            var factory = _project.GetInterpreterFactory();

            yield return string.Format("{0}={1}", AD7Engine.VersionSetting, factory.Configuration.Version);
            yield return string.Format("{0}={1}",
                AD7Engine.InterpreterOptions,
                _project.GetProperty(PythonConstants.InterpreterArgumentsSetting) ?? string.Empty
            );
            var url = GetFullUrl();
            if (!String.IsNullOrWhiteSpace(url)) {
                yield return string.Format("{0}={1}", AD7Engine.WebBrowserUrl, HttpUtility.UrlEncode(url));
            }
            
            // Check project type GUID and enable the Django-specific features
            // of the debugger if required.
            var projectGuids = _project.GetUnevaluatedProperty("ProjectTypeGuids") ?? "";
            // HACK: Literal GUID string to avoid introducing Django-specific public API
            // We don't want to expose a constant from PythonTools.dll.
            // TODO: Add generic breakpoint extension point
            // to avoid having to pass this property for Django and any future
            // extensions.
            if (projectGuids.IndexOf("5F0BE9CA-D677-4A4D-8806-6076C0FAAD37", StringComparison.OrdinalIgnoreCase) >= 0) {
                yield return AD7Engine.EnableDjangoDebugging + "=True";
            }
        }

        private unsafe DebugTargetInfo CreateDebugTargetInfo(
            CommandStartInfo startInfo,
            IPythonProjectLaunchProperties props
        ) {
            var dti = new DebugTargetInfo();

            var alwaysPause = startInfo.ExecuteInConsoleAndPause;
            // We only want to debug a web server in a console.
            startInfo.ExecuteIn = "console";
            startInfo.AdjustArgumentsForProcessStartInfo(props.GetInterpreterPath(), handleConsoleAndPause: false);

            try {
                dti.Info.dlo = DEBUG_LAUNCH_OPERATION.DLO_CreateProcess;
                dti.Info.bstrExe = startInfo.Filename;
                dti.Info.bstrCurDir = startInfo.WorkingDirectory;

                dti.Info.bstrRemoteMachine = null;
                dti.Info.fSendStdoutToOutputWindow = 0;

                if (!(props.GetIsNativeDebuggingEnabled() ?? false)) {
                    dti.Info.bstrOptions = string.Join(";",
                        GetGlobalDebuggerOptions(true, alwaysPause)
                            .Concat(GetProjectDebuggerOptions())
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Select(s => s.Replace(";", ";;"))
                    );
                }

                if (startInfo.EnvironmentVariables != null && startInfo.EnvironmentVariables.Any()) {
                    // Environment variables should be passed as a 
                    // null-terminated block of null-terminated strings. 
                    // Each string is in the following form:name=value\0
                    var buf = new StringBuilder();
                    foreach (var kv in startInfo.EnvironmentVariables) {
                        buf.AppendFormat("{0}={1}\0", kv.Key, kv.Value);
                    }
                    buf.Append("\0");
                    dti.Info.bstrEnv = buf.ToString();
                }

                dti.Info.bstrArg = startInfo.Arguments;

                if (props.GetIsNativeDebuggingEnabled() ?? false) {
                    dti.Info.dwClsidCount = 2;
                    dti.Info.pClsidList = Marshal.AllocCoTaskMem(sizeof(Guid) * 2);
                    var engineGuids = (Guid*)dti.Info.pClsidList;
                    engineGuids[0] = dti.Info.clsidCustom = DkmEngineId.NativeEng;
                    engineGuids[1] = AD7Engine.DebugEngineGuid;
                } else {
                    // Set the Python debugger
                    dti.Info.clsidCustom = new Guid(AD7Engine.DebugEngineId);
                    dti.Info.grfLaunch = (uint)__VSDBGLAUNCHFLAGS.DBGLAUNCH_StopDebuggingOnEnd;
                }

                // Null out dti so that it is not disposed before we return.
                var result = dti;
                dti = null;
                return result;
            } finally {
                if (dti != null) {
                    dti.Dispose();
                }
            }
        }

        private ProcessStartInfo CreateProcessStartInfo(
            CommandStartInfo startInfo,
            IPythonProjectLaunchProperties props
        ) {
            bool alwaysPause = startInfo.ExecuteInConsoleAndPause;
            // We only want to run the webserver in a console.
            startInfo.ExecuteIn = "console";
            startInfo.AdjustArgumentsForProcessStartInfo(props.GetInterpreterPath(), handleConsoleAndPause: false);

            var psi = new ProcessStartInfo {
                FileName = startInfo.Filename,
                Arguments = startInfo.Arguments,
                WorkingDirectory = startInfo.WorkingDirectory,
                UseShellExecute = false
            };

            var env = new Dictionary<string, string>(startInfo.EnvironmentVariables, StringComparer.OrdinalIgnoreCase);
            PythonProjectLaunchProperties.MergeEnvironmentBelow(env, new Dictionary<string, string> {
                { "SERVER_HOST", "localhost" },
                { "SERVER_PORT", TestServerPortString }
            }, true);

            foreach(var kv in env) {
                psi.EnvironmentVariables[kv.Key] = kv.Value;
            }

            // Pause if the user has requested it.
            string pauseCommand = null;
            if (alwaysPause ||
                _pyService.DebuggerOptions.WaitOnAbnormalExit &&
                _pyService.DebuggerOptions.WaitOnNormalExit) {
                pauseCommand = "pause";
            } else if (_pyService.DebuggerOptions.WaitOnAbnormalExit &&
                !_pyService.DebuggerOptions.WaitOnNormalExit) {
                pauseCommand = "if errorlevel 1 pause";
            } else if (_pyService.DebuggerOptions.WaitOnNormalExit &&
                !_pyService.DebuggerOptions.WaitOnAbnormalExit) {
                pauseCommand = "if not errorlevel 1 pause";
            }
            if (!string.IsNullOrEmpty(pauseCommand)) {
                psi.Arguments = string.Format("/c \"{0} {1}\" & {2}",
                    ProcessOutput.QuoteSingleArgument(psi.FileName),
                    psi.Arguments,
                    pauseCommand
                );
                psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            }

            return psi;
        }

        #endregion

        private string GetFullUrl() {
            var host = _project.GetProperty(PythonConstants.WebBrowserUrlSetting);

            try {
                return GetFullUrl(host, TestServerPort);
            } catch (UriFormatException) {
                var output = OutputWindowRedirector.GetGeneral(_serviceProvider);
                output.WriteErrorLine(Strings.ErrorInvalidLaunchUrl.FormatUI(host));
                output.ShowAndActivate();
                return string.Empty;
            }
        }

        internal static string GetFullUrl(string host, int port) {
            UriBuilder builder;
            Uri uri;
            if (Uri.TryCreate(host, UriKind.Absolute, out uri)) {
                builder = new UriBuilder(uri);
            } else {
                builder = new UriBuilder();
                builder.Scheme = Uri.UriSchemeHttp;
                builder.Host = "localhost";
                builder.Path = host;
            }

            builder.Port = port;

            return builder.ToString();
        }

        private string TestServerPortString {
            get {
                if (!_testServerPort.HasValue) {
                    _testServerPort = GetFreePort();
                }
                return _testServerPort.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        private int TestServerPort {
            get {
                if (!_testServerPort.HasValue) {
                    _testServerPort = GetFreePort();
                }
                return _testServerPort.Value;
            }
        }

        private static int GetFreePort() {
            return Enumerable.Range(new Random().Next(49152, 65536), 60000).Except(
                from connection in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
                select connection.LocalEndPoint.Port
            ).First();
        }
    }
}
