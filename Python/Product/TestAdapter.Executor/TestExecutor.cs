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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.XPath;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Ipc.Json;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using TP = Microsoft.PythonTools.TestAdapter.TestProtocol;

namespace Microsoft.PythonTools.TestAdapter {
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
        Justification = "object owned by VS")]
    [ExtensionUri(PythonConstants.TestExecutorUriString)]
    class TestExecutor : ITestExecutor {
        private static readonly Guid PythonRemoteDebugPortSupplierUnsecuredId = new Guid("{FEB76325-D127-4E02-B59D-B16D93D46CF5}");
        private static readonly Guid PythonDebugEngineGuid = new Guid("EC1375B7-E2CE-43E8-BF75-DC638DE1F1F9");
        private static readonly Guid NativeDebugEngineGuid = new Guid("3B476D35-A401-11D2-AAD4-00C04F990171");

        private static readonly string TestLauncherPath = PythonToolsInstallPath.GetFile("visualstudio_py_testlauncher.py");
        internal static readonly Uri PythonCodeCoverageUri = new Uri("datacollector://Microsoft/PythonCodeCoverage/1.0");

        private readonly ManualResetEvent _cancelRequested = new ManualResetEvent(false);

        private readonly VisualStudioProxy _app;

        public TestExecutor() {
            _app = VisualStudioProxy.FromEnvironmentVariable(PythonConstants.PythonToolsProcessIdEnvironmentVariable);
        }

        public void Cancel() {
            _cancelRequested.Set();
        }

        public void RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle) {
            ValidateArg.NotNull(sources, "sources");
            ValidateArg.NotNull(runContext, "runContext");
            ValidateArg.NotNull(frameworkHandle, "frameworkHandle");

            _cancelRequested.Reset();

            var executorUri = new Uri(PythonConstants.TestExecutorUriString);
            var tests = new List<TestCase>();
            var doc = new XPathDocument(new StringReader(runContext.RunSettings.SettingsXml));
            foreach (var t in TestReader.ReadTests(doc, new HashSet<string>(sources, StringComparer.OrdinalIgnoreCase), m => {
                frameworkHandle?.SendMessage(TestMessageLevel.Warning, m);
            })) {
                tests.Add(new TestCase(t.FullyQualifiedName, executorUri, t.SourceFile) {
                    DisplayName = t.DisplayName,
                    LineNumber = t.LineNo,
                    CodeFilePath = t.SourceFile
                });
            }

            if (_cancelRequested.WaitOne(0)) {
                return;
            }

            RunTestCases(tests, runContext, frameworkHandle);
        }

        private Dictionary<string, PythonProjectSettings> GetSourceToSettings(IRunSettings settings) {
            var doc = new XPathDocument(new StringReader(settings.SettingsXml));
            XPathNodeIterator nodes = doc.CreateNavigator().Select("/RunSettings/Python/TestCases/Project");
            Dictionary<string, PythonProjectSettings> res = new Dictionary<string, PythonProjectSettings>();

            foreach (XPathNavigator project in nodes) {
                PythonProjectSettings projSettings = new PythonProjectSettings(
                    project.GetAttribute("home", ""),
                    project.GetAttribute("workingDir", ""),
                    project.GetAttribute("interpreter", ""),
                    project.GetAttribute("pathEnv", ""),
                    project.GetAttribute("nativeDebugging", "").IsTrue()
                );

                foreach (XPathNavigator environment in project.Select("Environment/Variable")) {
                    projSettings.Environment[environment.GetAttribute("name", "")] = environment.GetAttribute("value", "");
                }

                string djangoSettings = project.GetAttribute("djangoSettingsModule", "");
                if (!String.IsNullOrWhiteSpace(djangoSettings)) {
                    projSettings.Environment["DJANGO_SETTINGS_MODULE"] = djangoSettings;
                }

                foreach (XPathNavigator searchPath in project.Select("SearchPaths/Search")) {
                    projSettings.SearchPath.Add(searchPath.GetAttribute("value", ""));
                }

                foreach (XPathNavigator test in project.Select("Test")) {
                    string testFile = test.GetAttribute("file", "");
                    Debug.Assert(!string.IsNullOrWhiteSpace(testFile));
                    res[testFile] = projSettings;
                }
            }
            return res;
        }

        public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle) {
            ValidateArg.NotNull(tests, "tests");
            ValidateArg.NotNull(runContext, "runContext");
            ValidateArg.NotNull(frameworkHandle, "frameworkHandle");

            _cancelRequested.Reset();

            RunTestCases(tests, runContext, frameworkHandle);
        }

        private void RunTestCases(
            IEnumerable<TestCase> tests,
            IRunContext runContext,
            IFrameworkHandle frameworkHandle
        ) {
            bool codeCoverage = EnableCodeCoverage(runContext);
            string covPath = null;
            if (codeCoverage) {
                covPath = GetCoveragePath(tests);
            }
            // .py file path -> project settings
            var sourceToSettings = GetSourceToSettings(runContext.RunSettings);

            foreach (var testGroup in tests.GroupBy(x => sourceToSettings[x.CodeFilePath])) {
                if (_cancelRequested.WaitOne(0)) {
                    break;
                }

                using (var runner = new TestRunner(
                    frameworkHandle,
                    runContext,
                    testGroup,
                    covPath,
                    testGroup.Key,
                    _app,
                    _cancelRequested
                )) {
                    runner.Run();
                }
            }

            if (codeCoverage) {
                if (File.Exists(covPath + ".xml")) {
                    var set = new AttachmentSet(PythonCodeCoverageUri, "CodeCoverage");

                    set.Attachments.Add(
                        new UriDataAttachment(new Uri(covPath + ".xml"), "Coverage Data")
                    );
                    frameworkHandle.RecordAttachments(new[] { set });

                    File.Delete(covPath);
                } else {
                    frameworkHandle.SendMessage(TestMessageLevel.Warning, Strings.Test_NoCoverageProduced);
                }
            }
        }

        private static string GetCoveragePath(IEnumerable<TestCase> tests) {
            string bestFile = null, bestClass = null, bestMethod = null;

            // Try and generate a friendly name for the coverage report.  We use
            // the filename, class, and method.  We include each one if we're
            // running from a single filename/class/method.  When we have multiple
            // we drop the identifying names.  If we have multiple files we
            // go to the top level directory...  If all else fails we do "pycov".
            foreach (var test in tests) {
                string testFile, testClass, testMethod;
                TestReader.ParseFullyQualifiedTestName(
                    test.FullyQualifiedName,
                    out testFile,
                    out testClass,
                    out testMethod
                );

                bestFile = UpdateBestFile(bestFile, test.CodeFilePath);
                if (bestFile != test.CodeFilePath) {
                    // Different files, don't include class/methods even
                    // if they happen to be the same.
                    bestClass = bestMethod = "";
                }

                bestClass = UpdateBest(bestClass, testClass);
                bestMethod = UpdateBest(bestMethod, testMethod);
            }

            string filename = "";

            if (!String.IsNullOrWhiteSpace(bestFile)) {
                if (ModulePath.IsPythonSourceFile(bestFile)) {
                    filename = ModulePath.FromFullPath(bestFile).ModuleName;
                } else {
                    filename = Path.GetFileName(bestFile);
                }
            } else {
                filename = "pycov";
            }

            if (!String.IsNullOrWhiteSpace(bestClass)) {
                filename += "_" + bestClass;
            }

            if (!String.IsNullOrWhiteSpace(bestMethod)) {
                filename += "_" + bestMethod;
            }

            filename += "_" + DateTime.Now.ToString("s").Replace(':', '_');

            return Path.Combine(Path.GetTempPath(), filename);
        }

        private static string UpdateBest(string best, string test) {
            if (best == null || best == test) {
                best = test;
            } else if (!string.IsNullOrEmpty(best)) {
                best = "";
            }

            return best;
        }

        internal static string UpdateBestFile(string bestFile, string testFile) {
            if (bestFile == null || bestFile == testFile) {
                bestFile = testFile;
            } else if (!string.IsNullOrEmpty(bestFile)) {
                // Get common directory name, trim to the last \\ where we 
                // have things in common
                int lastSlash = 0;
                for (int i = 0; i < bestFile.Length && i < testFile.Length; i++) {
                    if (bestFile[i] != testFile[i]) {
                        bestFile = bestFile.Substring(0, lastSlash);
                        break;
                    } else if (bestFile[i] == '\\' || bestFile[i] == '/') {
                        lastSlash = i;
                    }
                }
            }

            return bestFile;
        }

        private static bool EnableCodeCoverage(IRunContext runContext) {
            var doc = new XPathDocument(new StringReader(runContext.RunSettings.SettingsXml));
            XPathNodeIterator nodes = doc.CreateNavigator().Select("/RunSettings/Python/EnableCoverage");
            bool enableCoverage;
            if (nodes.MoveNext()) {
                if (Boolean.TryParse(nodes.Current.Value, out enableCoverage)) {
                    return enableCoverage;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns true if this is a dry run. Dry runs require a
        /// &lt;DryRun value="true" /&gt; element under RunSettings/Python.
        /// </summary>
        private static bool IsDryRun(IRunSettings settings) {
            var doc = new XPathDocument(new StringReader(settings.SettingsXml));
            try {
                var node = doc.CreateNavigator().SelectSingleNode("/RunSettings/Python/DryRun[@value='true']");
                return node != null;
            } catch (Exception ex) {
                Debug.Fail(ex.ToUnhandledExceptionMessage(typeof(TestExecutor)));
                return false;
            }
        }

        /// <summary>
        /// Returns true if the console should be shown. This is the default
        /// unless a &lt;ShowConsole value="false" /&gt; element exists under
        /// RunSettings/Python.
        /// </summary>
        private static bool ShouldShowConsole(IRunSettings settings) {
            var doc = new XPathDocument(new StringReader(settings.SettingsXml));
            try {
                var node = doc.CreateNavigator().SelectSingleNode("/RunSettings/Python/ShowConsole[@value='false']");
                return node == null;
            } catch (Exception ex) {
                Debug.Fail(ex.ToUnhandledExceptionMessage(typeof(TestExecutor)));
                return true;
            }
        }


        sealed class TestRunner : IDisposable {
            private readonly IFrameworkHandle _frameworkHandle;
            private readonly IRunContext _context;
            private readonly TestCase[] _tests;
            private readonly string _codeCoverageFile;
            private readonly PythonProjectSettings _settings;
            private readonly PythonDebugMode _debugMode;
            private readonly VisualStudioProxy _app;
            private readonly string _searchPaths;
            private readonly Dictionary<string, string> _env;
            private readonly string _debugSecret;
            private readonly int _debugPort;
            private readonly ManualResetEvent _cancelRequested;
            private readonly AutoResetEvent _connected = new AutoResetEvent(false);
            private readonly AutoResetEvent _done = new AutoResetEvent(false);
            private Connection _connection;
            private readonly Socket _socket;
            private readonly StringBuilder _stdOut = new StringBuilder(), _stdErr = new StringBuilder();
            private TestCase _curTest;
            private readonly bool _dryRun, _showConsole;

            public TestRunner(
                IFrameworkHandle frameworkHandle,
                IRunContext runContext,
                IEnumerable<TestCase> tests,
                string codeCoverageFile,
                PythonProjectSettings settings,
                VisualStudioProxy app,
                ManualResetEvent cancelRequested) {

                _frameworkHandle = frameworkHandle;
                _context = runContext;
                _tests = tests.ToArray();
                _codeCoverageFile = codeCoverageFile;
                _settings = settings;
                _app = app;
                _cancelRequested = cancelRequested;
                _dryRun = IsDryRun(runContext.RunSettings);
                _showConsole = ShouldShowConsole(runContext.RunSettings);

                _env = new Dictionary<string, string>();

                _debugMode = PythonDebugMode.None;
                if (runContext.IsBeingDebugged && _app != null) {
                    _debugMode = settings.EnableNativeCodeDebugging ? PythonDebugMode.PythonAndNative : PythonDebugMode.PythonOnly;
                }

                _searchPaths = GetSearchPaths(tests, settings);

                if (_debugMode == PythonDebugMode.PythonOnly) {
                    var secretBuffer = new byte[24];
                    RandomNumberGenerator.Create().GetNonZeroBytes(secretBuffer);
                    _debugSecret = Convert.ToBase64String(secretBuffer)
                        .Replace('+', '-')
                        .Replace('/', '_')
                        .TrimEnd('=');

                    _debugPort = GetFreePort();
                }

                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
                _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                _socket.Listen(0);
                _socket.BeginAccept(AcceptConnection, _socket);
            }

            [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "_connection")]
            public void Dispose() {
                _connected.Dispose();
                _done.Dispose();
                _connection?.Dispose();
                _socket.Dispose();
            }

            private static Task RequestHandler(RequestArgs arg1, Func<Response, Task> arg2) {
                throw new NotImplementedException();
            }

            private void ConectionReceivedEvent(object sender, EventReceivedEventArgs e) {
                switch (e.Name) {
                    case TP.ResultEvent.Name:
                        var result = (TP.ResultEvent)e.Event;
                        TestOutcome outcome = TestOutcome.None;
                        switch (result.outcome) {
                            case "passed": outcome = TestOutcome.Passed; break;
                            case "failed": outcome = TestOutcome.Failed; break;
                            case "skipped": outcome = TestOutcome.Skipped; break;
                        }

                        var testResult = new TestResult(_curTest);
                        RecordEnd(
                            _frameworkHandle,
                            _curTest,
                            testResult,
                            _stdOut.ToString(),
                            _stdErr.ToString(),
                            outcome,
                            result
                        );

                        _stdOut.Clear();
                        _stdErr.Clear();
                        break;

                    case TP.StartEvent.Name:
                        var start = (TP.StartEvent)e.Event;
                        _curTest = null;
                        foreach (var test in GetTestCases()) {
                            if (test.Key == start.test) {
                                _curTest = test.Value;
                                break;
                            }
                        }

                        if (_curTest != null) {
                            _frameworkHandle.RecordStart(_curTest);
                        } else {
                            Warning(Strings.Test_UnexpectedResult.FormatUI(start.classname, start.method));
                        }
                        break;
                    case TP.StdErrEvent.Name:
                        var err = (TP.StdErrEvent)e.Event;
                        _stdErr.Append(err.content);
                        break;
                    case TP.StdOutEvent.Name:
                        var outp = (TP.StdOutEvent)e.Event;
                        _stdOut.Append(outp.content);
                        break;
                    case TP.DoneEvent.Name:
                        _done.Set();
                        break;
                }
            }

            private string GetSearchPaths(IEnumerable<TestCase> tests, PythonProjectSettings settings) {
                var paths = settings.SearchPath.ToList();

                HashSet<string> knownModulePaths = new HashSet<string>();
                foreach (var test in tests) {
                    string testFilePath = PathUtils.GetAbsoluteFilePath(settings.ProjectHome, test.CodeFilePath);
                    var modulePath = ModulePath.FromFullPath(testFilePath);
                    if (knownModulePaths.Add(modulePath.LibraryPath)) {
                        paths.Insert(0, modulePath.LibraryPath);
                    }
                }

                paths.Insert(0, settings.WorkingDirectory);
                if (_debugMode == PythonDebugMode.PythonOnly) {
                    paths.Insert(0, PtvsdSearchPath);
                }

                string searchPaths = string.Join(
                    ";",
                    paths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
                );
                return searchPaths;
            }

            private void AcceptConnection(IAsyncResult iar) {
                Socket socket;
                var socketSource = ((Socket)iar.AsyncState);
                try {
                    socket = socketSource.EndAccept(iar);
                } catch (SocketException ex) {
                    Debug.WriteLine("DebugConnectionListener socket failed");
                    Debug.WriteLine(ex);
                    return;
                } catch (ObjectDisposedException) {
                    Debug.WriteLine("DebugConnectionListener socket closed");
                    return;
                }

                var stream = new NetworkStream(socket, ownsSocket: true);
                _connection = new Connection(
                    new MemoryStream(),
                    stream,
                    RequestHandler,
                    TP.RegisteredTypes
                );
                _connection.EventReceived += ConectionReceivedEvent;
                _connection.StartProcessing();
                _connected.Set();
            }

            public void Run() {
                if (!File.Exists(_settings.InterpreterPath)) {
                    Error(Strings.Test_InterpreterDoesNotExist.FormatUI(_settings.InterpreterPath));
                    return;
                }
                try {
                    DetachFromSillyManagedProcess();

                    var pythonPath = InitializeEnvironment();

                    string testList = null;
                    // For a small set of tests, we'll pass them on the command
                    // line. Once we exceed a certain (arbitrary) number, create
                    // a test list on disk so that we do not overflow the 
                    // 32K argument limit.
                    if (_tests.Length > 5) {
                        testList = CreateTestList();
                    }
                    var arguments = GetArguments(testList);

                    ////////////////////////////////////////////////////////////
                    // Do the test run
                    using (var proc = ProcessOutput.Run(
                        _settings.InterpreterPath,
                        arguments,
                        _settings.WorkingDirectory,
                        _env,
                        _showConsole,
                        null
                    )) {
                        bool killed = false;

                        DebugInfo("cd " + _settings.WorkingDirectory);
                        DebugInfo("set " + pythonPath.Key + "=" + pythonPath.Value);
                        DebugInfo(proc.Arguments);

                        _connected.WaitOne();
                        if (proc.ExitCode.HasValue) {
                            // Process has already exited
                            proc.Wait();
                            Error(Strings.Test_FailedToStartExited);
                            if (proc.StandardErrorLines.Any()) {
                                foreach (var line in proc.StandardErrorLines) {
                                    Error(line);
                                }
                            }
                        }

                        if (_debugMode != PythonDebugMode.None) {
                            try {
                                if (_debugMode == PythonDebugMode.PythonOnly) {
                                    string qualifierUri = string.Format("tcp://{0}@localhost:{1}", _debugSecret, _debugPort);
                                    while (!_app.AttachToProcess(proc, PythonRemoteDebugPortSupplierUnsecuredId, qualifierUri)) {
                                        if (proc.Wait(TimeSpan.FromMilliseconds(500))) {
                                            break;
                                        }
                                    }
                                } else {
                                    var engines = new[] { PythonDebugEngineGuid, NativeDebugEngineGuid };
                                    while (!_app.AttachToProcess(proc, engines)) {
                                        if (proc.Wait(TimeSpan.FromMilliseconds(500))) {
                                            break;
                                        }
                                    }
                                }

                            } catch (COMException ex) {
                                Error(Strings.Test_ErrorConnecting);
                                DebugError(ex.ToString());
                                try {
                                    proc.Kill();
                                } catch (InvalidOperationException) {
                                    // Process has already exited
                                }
                                killed = true;
                            }
                        }


                        // https://pytools.codeplex.com/workitem/2290
                        // Check that proc.WaitHandle was not null to avoid crashing if
                        // a test fails to start running. We will report failure and
                        // send the error message from stdout/stderr.
                        var handles = new WaitHandle[] { _cancelRequested, proc.WaitHandle, _done };
                        if (handles[1] == null) {
                            killed = true;
                        }

                        if (!killed) {
                            switch (WaitHandle.WaitAny(handles)) {
                                case 0:
                                    // We've been cancelled
                                    try {
                                        proc.Kill();
                                    } catch (InvalidOperationException) {
                                        // Process has already exited
                                    }
                                    killed = true;
                                    break;
                                case 1:
                                    // The process has exited, give a chance for our comm channel
                                    // to be flushed...
                                    handles = new WaitHandle[] { _cancelRequested, _done };
                                    if (WaitHandle.WaitAny(handles, 10000) != 1) {
                                        Warning(Strings.Test_NoTestFinishedNotification);
                                    }
                                    break;
                                case 2:
                                    // We received the done event
                                    break;
                            }
                        }
                    }
                    if (File.Exists(testList)) {
                        try {
                            File.Delete(testList);
                        } catch (IOException) {
                        }
                    }
                } catch (Exception e) {
                    Error(e.ToString());
                }
            }

            [Conditional("DEBUG")]
            private void DebugInfo(string message) {
                _frameworkHandle.SendMessage(TestMessageLevel.Informational, message);
            }


            [Conditional("DEBUG")]
            private void DebugError(string message) {
                _frameworkHandle.SendMessage(TestMessageLevel.Error, message);
            }

            private void Info(string message) {
                _frameworkHandle.SendMessage(TestMessageLevel.Informational, message);
            }


            private void Error(string message) {
                _frameworkHandle.SendMessage(TestMessageLevel.Error, message);
            }

            private void Warning(string message) {
                _frameworkHandle.SendMessage(TestMessageLevel.Warning, message);
            }

            private void DetachFromSillyManagedProcess() {
                var dte = _app != null ? _app.GetDTE() : null;
                if (dte != null && _debugMode != PythonDebugMode.None) {
                    dte.Debugger.DetachAll();
                }
            }

            private KeyValuePair<string, string> InitializeEnvironment() {
                var pythonPathVar = _settings.PathEnv;
                var pythonPath = _searchPaths;
                if (!string.IsNullOrWhiteSpace(pythonPathVar)) {
                    _env[pythonPathVar] = pythonPath;
                }

                foreach (var envVar in _settings.Environment) {
                    _env[envVar.Key] = envVar.Value;
                }
                return new KeyValuePair<string, string>(pythonPathVar, pythonPath);
            }

            private IEnumerable<KeyValuePair<string, TestCase>> GetTestCases() {
                var moduleCache = new Dictionary<string, ModulePath>();

                foreach (var test in _tests) {
                    string testFile, testClass, testMethod;
                    TestReader.ParseFullyQualifiedTestName(
                        test.FullyQualifiedName,
                        out testFile,
                        out testClass,
                        out testMethod
                    );

                    ModulePath module;
                    if (!moduleCache.TryGetValue(testFile, out module)) {
                        string testFilePath = PathUtils.GetAbsoluteFilePath(_settings.ProjectHome, testFile);
                        moduleCache[testFile] = module = ModulePath.FromFullPath(testFilePath);
                    }

                    yield return new KeyValuePair<string, TestCase>("{0}.{1}.{2}".FormatInvariant(
                        module.ModuleName,
                        testClass,
                        testMethod
                    ), test);
                }
            }

            private string CreateTestList() {
                var testList = Path.GetTempFileName();

                using (var writer = new StreamWriter(testList, false, new UTF8Encoding(false))) {
                    foreach (var test in GetTestCases()) {
                        writer.WriteLine(test.Key);
                    }
                }

                return testList;
            }

            private string[] GetArguments(string testList = null) {
                var arguments = new List<string>();
                arguments.Add(TestLauncherPath);

                if (string.IsNullOrEmpty(testList)) {
                    foreach (var test in GetTestCases()) {
                        arguments.Add("-t");
                        arguments.Add(test.Key);
                    }
                } else {
                    arguments.Add("--test-list");
                    arguments.Add(testList);
                }

                if (_dryRun) {
                    arguments.Add("--dry-run");
                }

                if (_codeCoverageFile != null) {
                    arguments.Add("--coverage");
                    arguments.Add(_codeCoverageFile);
                }

                if (_debugMode == PythonDebugMode.PythonOnly) {
                    arguments.AddRange(new[] {
                        "-s", _debugSecret,
                        "-p", _debugPort.ToString()
                    });
                } else if (_debugMode == PythonDebugMode.PythonAndNative) {
                    arguments.Add("-x");
                }

                arguments.Add("-r");
                arguments.Add(((IPEndPoint)_socket.LocalEndPoint).Port.ToString());
                return arguments.ToArray();
            }
        }

        private static void RecordEnd(IFrameworkHandle frameworkHandle, TestCase test, TestResult result, string stdout, string stderr, TestOutcome outcome, TP.ResultEvent resultInfo) {
            result.EndTime = DateTimeOffset.Now;
            result.Duration = result.EndTime - result.StartTime;
            result.Outcome = outcome;
            
            // Replace \n with \r\n to be more friendly when copying output...
            stdout = stdout.Replace("\r\n", "\n").Replace("\n", "\r\n");
            stderr = stderr.Replace("\r\n", "\n").Replace("\n", "\r\n");

            result.Messages.Add(new TestResultMessage(TestResultMessage.StandardOutCategory, stdout));
            result.Messages.Add(new TestResultMessage(TestResultMessage.StandardErrorCategory, stderr));
            result.Messages.Add(new TestResultMessage(TestResultMessage.AdditionalInfoCategory, stderr));
            if (resultInfo.traceback != null) {
                result.ErrorStackTrace = resultInfo.traceback;
                result.Messages.Add(new TestResultMessage(TestResultMessage.DebugTraceCategory, resultInfo.traceback));
            }
            if (resultInfo.message != null) {
                result.ErrorMessage = resultInfo.message;
            }

            frameworkHandle.RecordResult(result);
            frameworkHandle.RecordEnd(test, outcome);
        }

        class TestReceiver : ITestCaseDiscoverySink {
            public List<TestCase> Tests { get; private set; }

            public TestReceiver() {
                Tests = new List<TestCase>();
            }

            public void SendTestCase(TestCase discoveredTest) {
                Tests.Add(discoveredTest);
            }
        }

        sealed class PythonProjectSettings : IEquatable<PythonProjectSettings> {
            public readonly string ProjectHome, WorkingDirectory, InterpreterPath, PathEnv;
            public readonly bool EnableNativeCodeDebugging;
            public readonly List<string> SearchPath;
            public readonly Dictionary<string, string> Environment;

            public PythonProjectSettings(string projectHome, string workingDir, string interpreter, string pathEnv, bool nativeDebugging) {
                ProjectHome = projectHome;
                WorkingDirectory = workingDir;
                InterpreterPath = interpreter;
                PathEnv = pathEnv;
                EnableNativeCodeDebugging = nativeDebugging;
                SearchPath = new List<string>();
                Environment = new Dictionary<string, string>();
            }

            public override bool Equals(object obj) {
                return Equals(obj as PythonProjectSettings);
            }

            public override int GetHashCode() {
                return ProjectHome.GetHashCode() ^ 
                    WorkingDirectory.GetHashCode() ^ 
                    InterpreterPath.GetHashCode();
            }

            public bool Equals(PythonProjectSettings other) {
                if (other == null) {
                    return false;
                }

                if (ProjectHome == other.ProjectHome &&
                    WorkingDirectory == other.WorkingDirectory &&
                    InterpreterPath == other.InterpreterPath &&
                    PathEnv == other.PathEnv &&
                    EnableNativeCodeDebugging == other.EnableNativeCodeDebugging) {
                    if (SearchPath.Count == other.SearchPath.Count && 
                        Environment.Count == other.Environment.Count) {
                        for (int i = 0; i < SearchPath.Count; i++) {
                            if (SearchPath[i] != other.SearchPath[i]) {
                                return false;
                            }
                        }

                        foreach (var keyValue in Environment) {
                            string value;
                            if (!other.Environment.TryGetValue(keyValue.Key, out value) ||
                                value != keyValue.Value) {
                                return false;
                            }
                        }

                        return true;
                    }
                }

                return false;
            }
        }

        enum PythonDebugMode {
            None,
            PythonOnly,
            PythonAndNative
        }

        private static string PtvsdSearchPath {
            get {
                return Path.GetDirectoryName(Path.GetDirectoryName(PythonToolsInstallPath.GetFile("ptvsd\\__init__.py")));
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
