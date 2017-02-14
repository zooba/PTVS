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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.InteractiveWindow;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Ipc.Json;
using Microsoft.PythonTools.Parsing;
using Microsoft.PythonTools.Parsing.Ast;
using Microsoft.PythonTools.Projects;
using Microsoft.PythonTools.Repl;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.OptionsExtensionMethods;
using Microsoft.VisualStudioTools;
using MSBuild = Microsoft.Build.Evaluation;

namespace Microsoft.PythonTools.Intellisense {
    using AP = AnalysisProtocol;

    public sealed class VsProjectAnalyzer : ProjectAnalyzer, IDisposable {
        internal readonly Process _analysisProcess;
        private Connection _conn;
        // For entries that were loaded from a .zip file, IProjectEntry.Properties[_zipFileName] contains the full path to that archive.
        private static readonly object _zipFileName = new { Name = "ZipFileName" };

        // For entries that were loaded from a .zip file, IProjectEntry.Properties[_pathInZipFile] contains the path of the item inside the archive.
        private static readonly object _pathInZipFile = new { Name = "PathInZipFile" };

        internal readonly SearchPathManager _searchPaths = new SearchPathManager();

        private readonly bool _implicitProject;
        private readonly StringBuilder _stdErr = new StringBuilder();

        private readonly IPythonInterpreterFactory _interpreterFactory;

        private readonly ConcurrentDictionary<string, AnalysisEntry> _projectFiles;
        private readonly ConcurrentDictionary<int, AnalysisEntry> _projectFilesById;

        internal readonly HashSet<AnalysisEntry> _hasParseErrors = new HashSet<AnalysisEntry>();
        internal readonly object _hasParseErrorsLock = new object();

        private const string ParserTaskMoniker = "Parser";
        internal const string UnresolvedImportMoniker = "UnresolvedImport";
        internal bool _analysisComplete;

        private ErrorTaskProvider _errorProvider;
        private CommentTaskProvider _commentTaskProvider;

        private int _userCount;

        private readonly UnresolvedImportSquiggleProvider _unresolvedSquiggles;
        private readonly PythonToolsService _pyService;
        internal readonly IServiceProvider _serviceProvider;
        private readonly CancellationTokenSource _processExitedCancelSource = new CancellationTokenSource();
        private readonly HashSet<ProjectReference> _references = new HashSet<ProjectReference>();
        private bool _disposing;

        internal Task ReloadTask;
        internal int _parsePending;

        internal async Task<VersionedResponse<AP.UnresolvedImportsResponse>> GetMissingImportsAsync(AnalysisEntry analysisEntry, ITextBuffer textBuffer) {
            var lastVersion = analysisEntry.GetAnalysisVersion(textBuffer);

            var resp = await SendRequestAsync(
                new AP.UnresolvedImportsRequest() {
                    fileId = analysisEntry.FileId,
                    bufferId = analysisEntry.GetBufferId(textBuffer)
                },
                null
            ).ConfigureAwait(false);

            if (resp != null) {
                return VersionedResponse(resp, textBuffer, lastVersion);
            }

            return null;
        }

        internal VsProjectAnalyzer(
            IServiceProvider serviceProvider,
            IPythonInterpreterFactory factory,
            bool implicitProject = true,
            MSBuild.Project projectFile = null
        ) {
            _errorProvider = (ErrorTaskProvider)serviceProvider.GetService(typeof(ErrorTaskProvider));
            _commentTaskProvider = (CommentTaskProvider)serviceProvider.GetService(typeof(CommentTaskProvider));
            if (_errorProvider != null) {
                _unresolvedSquiggles = new UnresolvedImportSquiggleProvider(serviceProvider, _errorProvider);
            }

            _implicitProject = implicitProject;
            _serviceProvider = serviceProvider;
            _interpreterFactory = factory;
            var withDb = _interpreterFactory as IPythonInterpreterFactoryWithDatabase;
            if (withDb != null) {
                withDb.NewDatabaseAvailable += Factory_NewDatabaseAvailable;
            }

            _projectFiles = new ConcurrentDictionary<string, AnalysisEntry>();
            _projectFilesById = new ConcurrentDictionary<int, AnalysisEntry>();

            _pyService = serviceProvider.GetPythonToolsService();

            if (_commentTaskProvider != null) {
                _commentTaskProvider.TokensChanged += CommentTaskTokensChanged;
            }

            _conn = StartConnection(
                projectFile?.FullPath ?? (implicitProject ? "Global Analysis" : "Misc. Non Project Analysis"),
                out _analysisProcess
            );
            _userCount = 1;

            Task.Run(() => _conn.ProcessMessages());

            // load the interpreter factories available inside of VS into the remote process
            var providers = new HashSet<string>(
                    serviceProvider.GetComponentModel().GetExtensions<IPythonInterpreterFactoryProvider>()
                        .Select(x => x.GetType().Assembly.Location),
                    StringComparer.OrdinalIgnoreCase
            );
            providers.Add(typeof(IInterpreterOptionsService).Assembly.Location);


            var initialize = new AP.InitializeRequest() {
                interpreterId = factory.Configuration.Id,
                mefExtensions = providers.ToArray()
            };

            if (projectFile != null) {
                initialize.projectFile = projectFile.FullPath;
                initialize.projectHome = CommonUtils.GetAbsoluteDirectoryPath(
                    Path.GetDirectoryName(projectFile.FullPath),
                    projectFile.GetPropertyValue(CommonConstants.ProjectHome)
                );
                initialize.derivedInterpreters = projectFile.GetItems(MSBuildConstants.InterpreterItem).Select(
                    interp => new AP.DerivedInterpreter() {
                        name = interp.EvaluatedInclude,
                        id = interp.GetMetadataValue(MSBuildConstants.IdKey),
                        description = interp.GetMetadataValue(MSBuildConstants.DescriptionKey),
                        version = interp.GetMetadataValue(MSBuildConstants.VersionKey),
                        baseInterpreter = interp.GetMetadataValue(MSBuildConstants.BaseInterpreterKey),
                        path = interp.GetMetadataValue(MSBuildConstants.InterpreterPathKey),
                        windowsPath = interp.GetMetadataValue(MSBuildConstants.WindowsPathKey),
                        arch = interp.GetMetadataValue(MSBuildConstants.ArchitectureKey),
                        libPath = interp.GetMetadataValue(MSBuildConstants.LibraryPathKey),
                        pathEnvVar = interp.GetMetadataValue(MSBuildConstants.PathEnvVarKey)
                    }
                ).ToArray();
            }

            SendRequestAsync(initialize).ContinueWith(
                task => {
                    var result = task.Result;
                    if (result == null) {
                        Debug.Fail("Analyzer initialization failed with no result");
                        _conn = null;
                    } else if (!String.IsNullOrWhiteSpace(result.error)) {
                        Debug.Fail("Analyzer initialization failed with " + result.error);
                        _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationFailed, "Initialization: " + result.error);
                        _conn = null;
                    } else {
                        SendEvent(
                            new AP.OptionsChangedEvent() {
                                indentation_inconsistency_severity = _pyService.GeneralOptions.IndentationInconsistencySeverity
                            }
                        );
                    }
                }
            );

            CommentTaskTokensChanged(null, EventArgs.Empty);
        }

        public event EventHandler AnalyzerNeedsRestart;

        private void Factory_NewDatabaseAvailable(object sender, EventArgs e) {
            AnalyzerNeedsRestart?.Invoke(this, EventArgs.Empty);
        }

        #region ProjectAnalyzer overrides

        public override void RegisterExtension(string path) {
            SendEvent(
                new AP.ExtensionAddedEvent() {
                    path = path
                }
            );
        }

        /// <summary>
        /// Send a command to an extension.  The extension should have been loaded using a 
        /// RegisterExtension call.  The extension is implemented using IAnalysisExtension.
        /// 
        /// The extension name is provided by decorating the exported value with 
        /// AnalysisExtensionNameAttribute.  The command ID and body are free form values
        /// defined by the extension.
        /// 
        /// Returns null if the extension command fails or the remote process exits unexpectedly.
        /// </summary>
        public override async Task<string> SendExtensionCommandAsync(string extensionName, string commandId, string body) {
            var res = await SendRequestAsync(new AP.ExtensionRequest() {
                extension = extensionName,
                commandId = commandId,
                body = body
            }).ConfigureAwait(false);

            if (res != null) {
                return res.response;
            }

            return null;
        }

        /// <summary>
        /// Raised when any file has a new analysis.
        /// </summary>
        public override event EventHandler<AnalysisCompleteEventArgs> AnalysisComplete;

        public override IEnumerable<string> Files {
            get {
                return _projectFiles.Keys;
            }
        }
        #endregion

        #region Public API

        public PythonLanguageVersion LanguageVersion {
            get {
                return _interpreterFactory.GetLanguageVersion();
            }
        }

        public void AddUser() {
            Interlocked.Increment(ref _userCount);
        }

        /// <summary>
        /// Reduces the number of known users by one and returns true if the
        /// analyzer should be disposed.
        /// </summary>
        public bool RemoveUser() {
            return Interlocked.Decrement(ref _userCount) == 0;
        }

        #region IDisposable Members

        public void Dispose() {
            _disposing = true;

            var withDb = _interpreterFactory as IPythonInterpreterFactoryWithDatabase;
            if (withDb != null) {
                withDb.NewDatabaseAvailable -= Factory_NewDatabaseAvailable;
            }

            foreach (var entry in _projectFiles.Values) {
                _errorProvider?.Clear(entry, ParserTaskMoniker);
                _errorProvider?.Clear(entry, UnresolvedImportMoniker);
                _commentTaskProvider?.Clear(entry, ParserTaskMoniker);
            }

            Debug.WriteLine(String.Format("Disposing of parser {0}", _analysisProcess.Id));
            if (_commentTaskProvider != null) {
                _commentTaskProvider.TokensChanged -= CommentTaskTokensChanged;
            }

            foreach (var openFile in _projectFiles) {
                openFile.Value.Dispose();
            }

            SendRequestAsync(new AP.ExitRequest()).ContinueWith(t => {
                try {
                    if (!_analysisProcess.WaitForExit(500)) {
                        _analysisProcess.Kill();
                    }
                } catch (Win32Exception) {
                    // access denied
                } catch (InvalidOperationException) {
                    // race w/ process exit...
                }
                _analysisProcess.Dispose();
            });

            try {
                _processExitedCancelSource.Cancel();
                _processExitedCancelSource.Dispose();
            } catch (ObjectDisposedException) {
            }
            _conn?.Dispose();
        }

        #endregion

        public async Task SetSearchPathsAsync(IEnumerable<string> absolutePaths) {
            await SendRequestAsync(new AP.SetSearchPathRequest { dir = absolutePaths.ToArray() }).ConfigureAwait(false);
        }

        #endregion

        private Connection StartConnection(string comment, out Process proc) {
            var libAnalyzer = typeof(AP.FileChangedResponse).Assembly.Location;
            var psi = new ProcessStartInfo(libAnalyzer, "/interactive /comment \"" + comment + "\"");
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            var process = Process.Start(psi);

            var conn = new Connection(
                process.StandardInput.BaseStream,
                process.StandardOutput.BaseStream,
                null,
                AP.RegisteredTypes
            );

            process.Exited += OnAnalysisProcessExited;
            if (process.HasExited) {
                _stdErr.Append(process.StandardError.ReadToEnd());
                OnAnalysisProcessExited(process, EventArgs.Empty);
            } else {
                Task.Run(async () => {
                    try {
                        while (!process.HasExited) {
                            var line = await process.StandardError.ReadLineAsync();
                            if (line == null) {
                                break;
                            }
                            _stdErr.AppendLine(line);
                            Debug.WriteLine("Analysis Std Err: " + line);
                        }
                    } catch (InvalidOperationException) {
                        // can race with dispose of the process...
                    }
                });
            }
            conn.EventReceived += ConnectionEventReceived;
            proc = process;
            return conn;
        }

        private void OnAnalysisProcessExited(object sender, EventArgs e) {
            _processExitedCancelSource.Cancel();
            if (!_disposing) {
                _abnormalAnalysisExit?.Invoke(
                    this,
                    new AbnormalAnalysisExitEventArgs(
                        _stdErr.ToString(),
                        _analysisProcess.ExitCode
                    )
                );
            }
        }

        private event EventHandler<AbnormalAnalysisExitEventArgs> _abnormalAnalysisExit;
        internal event EventHandler<AbnormalAnalysisExitEventArgs> AbnormalAnalysisExit {
            add {
                if (_analysisProcess.HasExited && !_disposing) {
                    value?.Invoke(this, new AbnormalAnalysisExitEventArgs(_stdErr.ToString(), _analysisProcess.ExitCode));
                }
                _abnormalAnalysisExit += value;
            }
            remove {
                _abnormalAnalysisExit -= value;
            }
        }
        internal event EventHandler AnalysisStarted;

        private void ConnectionEventReceived(object sender, EventReceivedEventArgs e) {
            Debug.WriteLine(String.Format("Event received: {0}", e.Event.name));

            AnalysisEntry entry;
            switch (e.Event.name) {
                case AP.AnalysisCompleteEvent.Name:
                    _analysisComplete = true;
                    break;
                case AP.FileAnalysisCompleteEvent.Name:
                    OnAnalysisComplete(e);
                    break;
                case AP.FileParsedEvent.Name:
                    Interlocked.Decrement(ref _parsePending);

                    var parsed = (AP.FileParsedEvent)e.Event;
                    if (_projectFilesById.TryGetValue(parsed.fileId, out entry)) {
                        UpdateErrorsAndWarnings(entry, parsed);
                    } else {
                        Debug.WriteLine("Unknown file id for fileParsed event: {0}", parsed.fileId);
                    }
                    break;
                case AP.ChildFileAnalyzed.Name:
                    var childFile = (AP.ChildFileAnalyzed)e.Event;

                    if (!_projectFilesById.TryGetValue(childFile.fileId, out entry)) {
                        entry = new AnalysisEntry(
                            this,
                            childFile.filename,
                            childFile.fileId,
                            childFile.isTemporaryFile,
                            childFile.suppressErrorList
                        );
                        _projectFilesById[childFile.fileId] = _projectFiles[childFile.filename] = entry;
                    }
                    break;
                case AP.AnalyzerWarningEvent.Name:
                    var warning = (AP.AnalyzerWarningEvent)e.Event;
                    _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisWarning, warning.message);
                    break;
                case AP.UnhandledExceptionEvent.Name:
                    Debug.Fail("Unhandled exception from analyzer");
                    var exception = (AP.UnhandledExceptionEvent)e.Event;
                    _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationFailed, exception.message);
                    break;
            }
        }

        private async void OnAnalysisComplete(EventReceivedEventArgs e) {
            var analysisComplete = (AP.FileAnalysisCompleteEvent)e.Event;
            AnalysisEntry entry;
            if (_projectFilesById.TryGetValue(analysisComplete.fileId, out entry)) {
                try {
                    var bufferParser = await entry.GetBufferParserAsync();
                    foreach (var version in analysisComplete.versions) {
                        bufferParser.Analyzed(version.bufferId, version.version);
                    }
                } catch (OperationCanceledException) {
                }

                entry.OnAnalysisComplete();
                AnalysisComplete?.Invoke(this, new AnalysisCompleteEventArgs(entry.Path));
            }
        }

        internal static string GetZipFileName(AnalysisEntry entry) {
            object result;
            entry.Properties.TryGetValue(_zipFileName, out result);
            return (string)result;
        }

        private static void SetZipFileName(AnalysisEntry entry, string value) {
            entry.Properties[_zipFileName] = value;
        }

        internal static string GetPathInZipFile(AnalysisEntry entry) {
            object result;
            entry.Properties.TryGetValue(_pathInZipFile, out result);
            return (string)result;
        }

        private static void SetPathInZipFile(AnalysisEntry entry, string value) {
            entry.Properties[_pathInZipFile] = value;
        }

        internal async Task<AP.ModuleInfo[]> GetEntriesThatImportModuleAsync(string moduleName, bool includeUnresolved) {
            var modules = await SendRequestAsync(
                new AP.ModuleImportsRequest() {
                    includeUnresolved = includeUnresolved,
                    moduleName = moduleName
                }
            ).ConfigureAwait(false);

            if (modules != null) {
                return modules.modules;
            }
            return Array.Empty<AP.ModuleInfo>();
        }

        private void OnModulesChanged(object sender, EventArgs e) {
            SendEvent(new AP.ModulesChangedEvent());
        }

        /// <summary>
        /// Creates a new ProjectEntry for the collection of buffers.
        /// </summary>
        internal async void ReAnalyzeTextBuffers(BufferParser oldParser) {
            ITextBuffer[] buffers = oldParser.Buffers;
            if (buffers.Length > 0) {
                if (!oldParser.AnalysisEntry.SuppressErrorList) {
                    _errorProvider?.ClearErrorSource(oldParser.AnalysisEntry, ParserTaskMoniker);
                    _errorProvider?.ClearErrorSource(oldParser.AnalysisEntry, UnresolvedImportMoniker);
                    _commentTaskProvider?.ClearErrorSource(oldParser.AnalysisEntry, ParserTaskMoniker);
                }

                foreach (var buffer in buffers) {
                    oldParser.UninitBuffer(buffer);
                }

                int oldAttachedViews;
                lock (oldParser) {
                    oldAttachedViews = oldParser.AttachedViews;
                }

                var monitoredResult = await MonitorTextBufferAsync(buffers[0]);
                if (monitoredResult?.AnalysisEntry != null) {
                    for (int i = 1; i < buffers.Length; i++) {
                        monitoredResult.AddBuffer(buffers[i]);
                    }
                    monitoredResult.AttachedViews = oldAttachedViews;
                }

                oldParser.AnalysisEntry.OnNewAnalysisEntry();
            }
        }

        internal void ConnectErrorList(AnalysisEntry entry, ITextBuffer textBuffer) {
            if (entry.SuppressErrorList) {
                return;
            }

            _errorProvider?.AddBufferForErrorSource(entry, ParserTaskMoniker, textBuffer);
            _commentTaskProvider?.AddBufferForErrorSource(entry, ParserTaskMoniker, textBuffer);
        }

        internal void DisconnectErrorList(AnalysisEntry entry, ITextBuffer textBuffer) {
            if (entry.SuppressErrorList) {
                return;
            }

            _errorProvider?.RemoveBufferForErrorSource(entry, ParserTaskMoniker, textBuffer);
            _commentTaskProvider?.RemoveBufferForErrorSource(entry, ParserTaskMoniker, textBuffer);
        }

        internal void SwitchAnalyzers(VsProjectAnalyzer oldAnalyzer) {
            BufferParser[] parsers = oldAnalyzer._projectFiles
                .Select(x => x.Value.TryGetBufferParser())
                .Where(x => x != null)
                .ToArray();

            foreach (var bufferParser in parsers) {
                ReAnalyzeTextBuffers(bufferParser);
            }
        }

        internal void OnAnalysisStarted() {
            AnalysisStarted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Starts monitoring a buffer for changes so we will re-parse the buffer to update the analysis
        /// as the text changes.
        /// </summary>
        internal async Task<BufferParser> MonitorTextBufferAsync(ITextBuffer textBuffer, bool isTemporaryFile = false, bool suppressErrorList = false) {
            var entry = await CreateProjectEntryAsync(
                textBuffer,
                isTemporaryFile,
                suppressErrorList
            ).ConfigureAwait(false);
            if (entry == null) {
                return null;
            }

            var bufferParser = await entry.GetOrCreateBufferParser(
                this,
                textBuffer,
                bp => {
                    Debug.Assert(entry.SuppressErrorList == textBuffer.Properties.ContainsProperty(typeof(IInteractiveEvaluator)),
                        "Should always suppress error lists on interactive buffers");

                    if (!entry.SuppressErrorList) {
                        ConnectErrorList(entry, textBuffer);
                        _errorProvider?.AddBufferForErrorSource(entry, UnresolvedImportMoniker, textBuffer);
                        _unresolvedSquiggles?.ListenForNextNewAnalysis(entry, textBuffer);
                    }
                },
                bp => bp.AddBuffer(textBuffer)
            );

            Debug.Assert(bufferParser.AnalysisEntry == entry);

            return bufferParser;
        }

        internal void BufferDetached(AnalysisEntry entry, ITextBuffer buffer) {
            if (entry == null) {
                return;
            }

            var bufferParser = entry.TryGetBufferParser();
            if (bufferParser == null) {
                return;
            }

            bufferParser.RemoveBuffer(buffer);
            int attachedViews;
            lock (bufferParser) {
                attachedViews = --bufferParser.AttachedViews;
            }
            if (attachedViews == 0) {
                bufferParser.StopMonitoring();

                if (!entry.SuppressErrorList) {
                    _errorProvider?.ClearErrorSource(entry, ParserTaskMoniker);
                    _errorProvider?.ClearErrorSource(entry, UnresolvedImportMoniker);
                    _commentTaskProvider?.ClearErrorSource(entry, ParserTaskMoniker);
                }

                if (entry.IsTemporaryFile) {
                    UnloadFileAsync(entry)
                        .HandleAllExceptions(_serviceProvider, GetType())
                        .DoNotWait();
                }
            }
        }

        private static object _filenameKey = new object();

        private static string GetFilePath(ITextBuffer textBuffer) {
            string path;
            var replEval = textBuffer.GetInteractiveWindow()?.GetPythonEvaluator();
            if (replEval != null) {
                path = replEval.AnalysisFilename;
            } else {
                path = textBuffer.GetFilePath();
                if (path == null) {
                    if (!textBuffer.Properties.TryGetProperty(_filenameKey, out path)) {
                        textBuffer.Properties[_filenameKey] = path = Guid.NewGuid().ToString() + ".py";
                    }
                }
            }
            return path;
        }

        private async Task<AnalysisEntry> CreateProjectEntryAsync(ITextBuffer textBuffer, bool isTemporaryFile, bool suppressErrorList) {
            if (_conn == null) {
                // We aren't able to analyze code, so don't create an entry.
                return null;
            }

            string path = GetFilePath(textBuffer);

            AnalysisEntry entry;
            if (!_projectFiles.TryGetValue(path, out entry)) {
                _analysisComplete = false;
                Interlocked.Increment(ref _parsePending);

                var res = await SendRequestAsync(
                    new AP.AddFileRequest() {
                        path = path,
                        isTemporaryFile = isTemporaryFile,
                        suppressErrorLists = suppressErrorList
                    }).ConfigureAwait(false);

                if (res != null && res.fileId != -1) {
                    OnAnalysisStarted();

                    var id = res.fileId;
                    if (!_projectFilesById.TryGetValue(id, out entry)) {
                        // we awaited between the check and the AddFileRequest, another add could
                        // have snuck in.  So we check again here...
                        entry = _projectFilesById[id] = _projectFiles[path] = new AnalysisEntry(this, path, id, isTemporaryFile, suppressErrorList);
                    }
                } else {
                    Interlocked.Decrement(ref _parsePending);
                }

            }

            if (entry != null) {
                entry.AnalysisCookie = new SnapshotCookie(textBuffer.CurrentSnapshot);
            }

            return entry;
        }


        internal async Task<AnalysisEntry> AnalyzeFileAsync(string path, string addingFromDirectory = null) {
            if (_conn == null) {
                // We aren't able to analyze code, so don't create an entry.
                return null;
            }

            AnalysisEntry res;
            if (!_projectFiles.TryGetValue(path, out res)) {
                Interlocked.Increment(ref _parsePending);

                var response = await SendRequestAsync(new AP.AddFileRequest() { path = path }).ConfigureAwait(false);
                if (response != null) {
                    // we awaited between the check and the AddFileRequest, another add could
                    // have snuck in.  So we check again here, and we'll leave the other cookie in 
                    // as it's likely a SnapshotCookie which we prefer over a FileCookie.
                    if (response.fileId != -1 && !_projectFilesById.TryGetValue(response.fileId, out res)) {
                        res = _projectFilesById[response.fileId] = _projectFiles[path] = new AnalysisEntry(this, path, response.fileId);
                        res.AnalysisCookie = new FileCookie(path);
                    }
                }
            }

            return res;
        }

        internal async Task<IReadOnlyList<AnalysisEntry>> AnalyzeFileAsync(string[] paths, string addingFromDirectory = null) {
            if (_conn == null) {
                // We aren't able to analyze code, so don't create an entry.
                return null;
            }

            var req = new AP.AddBulkFileRequest { path = new string[paths.Length], addingFromDir = addingFromDirectory };
            bool anyAdded = false;
            AnalysisEntry[] res = new AnalysisEntry[paths.Length];
            for (int i = 0; i < paths.Length; ++i) {
                AnalysisEntry existing;
                if (_projectFiles.TryGetValue(paths[i], out existing)) {
                    res[i] = existing;
                } else {
                    anyAdded = true;
                    req.path[i] = paths[i];
                }
            }

            if (anyAdded) {
                Interlocked.Increment(ref _parsePending);
                var response = await SendRequestAsync(req).ConfigureAwait(false);
                if (response != null) {
                    for (int i = 0; i < paths.Length; ++i) {
                        AnalysisEntry entry = null;
                        var path = paths[i];
                        var id = response.fileId[i];
                        if (id != -1 && !_projectFilesById.TryGetValue(id, out entry)) {
                            entry = _projectFilesById[id] = _projectFiles[path] = new AnalysisEntry(this, path, id);
                            entry.AnalysisCookie = new FileCookie(path);
                        }
                        res[i] = entry;
                    }
                }
            }

            return res;
        }


        internal AnalysisEntry GetAnalysisEntryFromPath(string path) {
            AnalysisEntry res;
            if (_projectFiles.TryGetValue(path, out res)) {
                return res;
            }
            return null;
        }

        internal IEnumerable<KeyValuePair<string, AnalysisEntry>> LoadedFiles {
            get {
                return _projectFiles;
            }
        }

        internal static async Task<string[]> GetValueDescriptionsAsync(AnalysisEntry file, string expr, SnapshotPoint point) {
            var analysis = GetApplicableExpression(file, point);

            if (analysis != null) {
                return await GetValueDescriptionsAsync(file, analysis.Text, analysis.Location).ConfigureAwait(false);
            }

            return Array.Empty<string>();
        }

        internal static async Task<string[]> GetValueDescriptionsAsync(AnalysisEntry file, string expr, SourceLocation location) {
            var req = new AP.ValueDescriptionRequest() {
                expr = expr,
                column = location.Column,
                index = location.Index,
                line = location.Line,
                fileId = file.FileId
            };

            var res = await file.Analyzer.SendRequestAsync(req).ConfigureAwait(false);
            if (res != null) {
                return res.descriptions;
            }

            return Array.Empty<string>();
        }

        internal string[] GetValueDescriptions(AnalysisEntry entry, string expr, SourceLocation translatedLocation) {
            return GetValueDescriptionsAsync(
                entry,
                expr,
                translatedLocation
            ).WaitOrDefault(1000) ?? Array.Empty<string>();
        }

        internal static async Task<ExpressionAnalysis> AnalyzeExpressionAsync(AnalysisEntry file, string expr, SourceLocation location) {
            var req = new AP.AnalyzeExpressionRequest() {
                expr = expr,
                column = location.Column,
                index = location.Index,
                line = location.Line,
                fileId = file.FileId
            };

            var definitions = await file.Analyzer.SendRequestAsync(req).ConfigureAwait(false);
            if (definitions != null) {
                return new ExpressionAnalysis(
                    expr,
                    null,
                    definitions.variables
                        .Where(x => x.file != null)
                        .Select(file.Analyzer.ToAnalysisVariable)
                        .ToArray(),
                    definitions.privatePrefix,
                    definitions.memberName
                );
            }
            return null;
        }

        internal static async Task<ExpressionAnalysis> AnalyzeExpressionAsync(IServiceProvider serviceProvider, ITextView view, SnapshotPoint point) {
            var analysis = GetApplicableExpression(
                view.GetAnalysisEntry(point.Snapshot.TextBuffer, serviceProvider),
                point
            );

            if (analysis != null) {
                var location = analysis.Location;
                var req = new AP.AnalyzeExpressionRequest() {
                    expr = analysis.Text,
                    column = location.Column,
                    index = location.Index,
                    line = location.Line,
                    fileId = analysis.Entry.FileId
                };

                var definitions = await analysis.Entry.Analyzer.SendRequestAsync(req);

                if (definitions != null) {
                    return new ExpressionAnalysis(
                        analysis.Text,
                        analysis.Span,
                        definitions.variables
                            .Where(x => x.file != null)
                            .Select(analysis.Entry.Analyzer.ToAnalysisVariable)
                            .ToArray(),
                        definitions.privatePrefix,
                        definitions.memberName
                    );
                }
            }

            return null;
        }

        /// <summary>
        /// Gets a CompletionList providing a list of possible members the user can dot through.
        /// </summary>
        internal static CompletionAnalysis GetCompletions(IServiceProvider serviceProvider, ICompletionSession session, ITextView view, ITextSnapshot snapshot, ITrackingSpan span, ITrackingPoint point, CompletionOptions options) {
            return TrySpecialCompletions(serviceProvider, session, view, snapshot, span, point, options) ??
                   GetNormalCompletionContext(serviceProvider, session, view, snapshot, span, point, options);
        }

        /// <summary>
        /// Gets a list of signatuers available for the expression at the provided location in the snapshot.
        /// </summary>
        internal static async Task<SignatureAnalysis> GetSignaturesAsync(IServiceProvider serviceProvider, ITextView view, ITextSnapshot snapshot, ITrackingSpan span) {
            var analysis = view.GetAnalysisEntry(snapshot.TextBuffer, serviceProvider);
            if (analysis == null) {
                return new SignatureAnalysis("", 0, new ISignature[0]);
            }

            var analyzer = analysis.Analyzer;
            var buffer = snapshot.TextBuffer;

            ReverseExpressionParser parser = new ReverseExpressionParser(snapshot, buffer, span);

            var loc = parser.Span.GetSpan(parser.Snapshot.Version);

            int paramIndex;
            SnapshotPoint? sigStart;
            string lastKeywordArg;
            bool isParameterName;
            var exprRange = parser.GetExpressionRange(1, out paramIndex, out sigStart, out lastKeywordArg, out isParameterName);
            if (exprRange == null || sigStart == null) {
                return new SignatureAnalysis("", 0, new ISignature[0]);
            }

            var text = new SnapshotSpan(exprRange.Value.Snapshot, new Span(exprRange.Value.Start, sigStart.Value.Position - exprRange.Value.Start)).GetText();
            var applicableSpan = parser.Snapshot.CreateTrackingSpan(exprRange.Value.Span, SpanTrackingMode.EdgeInclusive);

            if (analyzer.ShouldEvaluateForCompletion(text)) {
                var liveSigs = analyzer.TryGetLiveSignatures(snapshot, paramIndex, text, applicableSpan, lastKeywordArg);
                if (liveSigs != null) {
                    return liveSigs;
                }
            }

            var result = new List<ISignature>();
            // TODO: Need to deal with version here...
            var location = TranslateIndex(loc.Start, snapshot, analysis);
            AP.SignaturesResponse sigs;
            using (new DebugTimer("SignaturesRequest", CompletionAnalysis.TooMuchTime)) {
                sigs = await analyzer.SendRequestAsync(
                    new AP.SignaturesRequest() {
                        text = text,
                        location = location.Index,
                        column = location.Column,
                        fileId = analysis.FileId
                    }
                ).ConfigureAwait(false);
            }

            if (sigs != null) {
                foreach (var sig in sigs.sigs) {
                    result.Add(new PythonSignature(analysis.Analyzer, applicableSpan, sig, paramIndex, lastKeywordArg));
                }
            }

            return new SignatureAnalysis(
                text,
                paramIndex,
                result,
                lastKeywordArg
            );
        }

        internal static SourceLocation TranslateIndex(int index, ITextSnapshot fromSnapshot, AnalysisEntry toAnalysisSnapshot) {
            SnapshotCookie snapshotCookie;
            // TODO: buffers differ in the REPL window case, in the future we should handle this better
            if (toAnalysisSnapshot != null &&
                fromSnapshot != null &&
                (snapshotCookie = toAnalysisSnapshot.AnalysisCookie as SnapshotCookie) != null &&
                snapshotCookie.Snapshot != null &&
                snapshotCookie.Snapshot.TextBuffer == fromSnapshot.TextBuffer) {

                var fromPoint = new SnapshotPoint(fromSnapshot, index);
                var fromLine = fromPoint.GetContainingLine();
                var toPoint = fromPoint.TranslateTo(snapshotCookie.Snapshot, PointTrackingMode.Negative);
                var toLine = toPoint.GetContainingLine();

                Debug.Assert(fromLine != null, "Unable to get 'from' line from " + fromPoint.ToString());
                Debug.Assert(toLine != null, "Unable to get 'to' line from " + toPoint.ToString());

                return new SourceLocation(
                    toPoint.Position,
                    (toLine != null ? toLine.LineNumber : fromLine != null ? fromLine.LineNumber : 0) + 1,
                    index - (fromLine != null ? fromLine.Start.Position : 0) + 1
                );
            } else if (fromSnapshot != null) {
                var fromPoint = new SnapshotPoint(fromSnapshot, index);
                var fromLine = fromPoint.GetContainingLine();

                return new SourceLocation(
                    index,
                    fromLine.LineNumber + 1,
                    index - fromLine.Start.Position + 1
                );
            } else {
                return new SourceLocation(index, 1, 1);
            }
        }

        internal static async Task<MissingImportAnalysis> GetMissingImportsAsync(IServiceProvider serviceProvider, ITextView view, ITextSnapshot snapshot, ITrackingSpan span) {
            ReverseExpressionParser parser = new ReverseExpressionParser(snapshot, snapshot.TextBuffer, span);
            var loc = span.GetSpan(snapshot.Version);
            int dummy;
            SnapshotPoint? dummyPoint;
            string lastKeywordArg;
            bool isParameterName;
            var exprRange = parser.GetExpressionRange(0, out dummy, out dummyPoint, out lastKeywordArg, out isParameterName);
            if (exprRange == null || isParameterName) {
                return MissingImportAnalysis.Empty;
            }

            AnalysisEntry entry;
            if (!view.TryGetAnalysisEntry(snapshot.TextBuffer, serviceProvider, out entry)) {
                return MissingImportAnalysis.Empty;
            }

            var text = exprRange.Value.GetText();
            if (string.IsNullOrEmpty(text)) {
                return MissingImportAnalysis.Empty;
            }

            var analyzer = entry.Analyzer;
            var index = (parser.GetStatementRange() ?? span.GetSpan(snapshot)).Start.Position;

            var location = TranslateIndex(
                index,
                snapshot,
                entry
            );

            var isMissing = await analyzer.IsMissingImportAsync(entry, text, location);

            if (isMissing) {
                var applicableSpan = parser.Snapshot.CreateTrackingSpan(
                    exprRange.Value.Span,
                    SpanTrackingMode.EdgeExclusive
                );
                return new MissingImportAnalysis(text, analyzer, applicableSpan);
            }

            // if we have type information don't offer to add imports
            return MissingImportAnalysis.Empty;
        }

        internal static async Task AddImportAsync(AnalysisEntry analysisEntry, string fromModule, string name, ITextView view, ITextBuffer textBuffer) {
            var analysis = view.GetAnalysisEntry(textBuffer, analysisEntry.Analyzer._serviceProvider);
            if (analysis != null) {
                var lastVersion = analysis.GetAnalysisVersion(textBuffer);

                var changes = await analysisEntry.Analyzer.AddImportAsync(
                    analysisEntry,
                    textBuffer,
                    fromModule,
                    name,
                    view.Options.GetNewLineCharacter()
                );

                if (changes != null) {
                    ApplyChanges(
                        changes.changes,
                        lastVersion,
                        textBuffer,
                        changes.version
                    );
                }
            }
        }

        internal async Task<bool> IsMissingImportAsync(AnalysisEntry entry, string text, SourceLocation location) {
            var res = await SendRequestAsync(
                new AP.IsMissingImportRequest() {
                    fileId = entry.FileId,
                    text = text,
                    index = location.Index,
                    line = location.Line,
                    column = location.Column
                }
            ).ConfigureAwait(false);

            return res?.isMissing ?? false;
        }

        private static NameExpression GetFirstNameExpression(Statement stmt) {
            return GetFirstNameExpression(Statement.GetExpression(stmt));
        }

        private static NameExpression GetFirstNameExpression(Expression expr) {
            NameExpression nameExpr;
            CallExpression callExpr;
            MemberExpression membExpr;

            if ((nameExpr = expr as NameExpression) != null) {
                return nameExpr;
            }
            if ((callExpr = expr as CallExpression) != null) {
                return GetFirstNameExpression(callExpr.Target);
            }
            if ((membExpr = expr as MemberExpression) != null) {
                return GetFirstNameExpression(membExpr.Target);
            }

            return null;
        }

        private static bool IsDefinition(IAnalysisVariable variable) {
            return variable.Type == VariableType.Definition;
        }

        private static bool IsImplicitlyDefinedName(NameExpression nameExpr) {
            return nameExpr.Name == "__all__" ||
                nameExpr.Name == "__file__" ||
                nameExpr.Name == "__doc__" ||
                nameExpr.Name == "__name__";
        }

        internal bool IsAnalyzing {
            get {

                return _parsePending > 0 || !_analysisComplete;
            }
        }

        internal bool WaitForAnalysisStarted(TimeSpan timeout) {
            var mre = new ManualResetEventSlim();
            EventHandler evt = (s, e) => mre.Set();
            AnalysisStarted += evt;
            try {
                return mre.Wait(timeout);
            } finally {
                AnalysisStarted -= evt;
                mre.Dispose();
            }
        }

        internal void WaitForCompleteAnalysis(Func<int, bool> itemsLeftUpdated) {
            if (IsAnalyzing) {
                while (IsAnalyzing) {
                    var res = SendRequestAsync(new AP.AnalysisStatusRequest()).Result;

                    if (res == null) {
                        itemsLeftUpdated(0);
                        return;
                    }

                    if (!itemsLeftUpdated(res.itemsLeft)) {
                        break;
                    }
                }
            } else {
                itemsLeftUpdated(0);
            }
        }

        /// <summary>
        /// True if the project is an implicit project and it should model files on disk in addition
        /// to files which are explicitly added.
        /// </summary>
        internal bool ImplicitProject {
            get {
                return _implicitProject;
            }
        }

        internal IPythonInterpreterFactory InterpreterFactory {
            get {
                return _interpreterFactory;
            }
        }

        private async void UpdateErrorsAndWarnings(AnalysisEntry entry, AP.FileParsedEvent parsedEvent) {
            bool hasErrors = false;

            BufferParser bufferParser;
            try {
                bufferParser = await entry.GetBufferParserAsync();
            } catch (OperationCanceledException) {
                return;
            }

            // Update the warn-on-launch state for this entry

            foreach (var buffer in parsedEvent.buffers) {
                hasErrors |= buffer.errors.Any();

                Debug.WriteLine("Received updated parse {0} {1}", parsedEvent.fileId, buffer.version);

                LocationTracker translator = null;
                if (bufferParser.IsOldSnapshot(buffer.bufferId, buffer.version)) {
                    // ignore receiving responses out of order...
                    Debug.WriteLine("Ignoring out of order parse {0}", buffer.version);
                    return;
                }

                var textBuffer = bufferParser.GetBuffer(buffer.bufferId);
                translator = new LocationTracker(
                    entry.GetAnalysisVersion(textBuffer),
                    textBuffer,
                    buffer.version
                );

                // Update the parser warnings/errors.
                var factory = new TaskProviderItemFactory(translator);
                if (!entry.SuppressErrorList && _errorProvider != null) {
                    if (buffer.errors.Any() || buffer.warnings.Any()) {
                        var warningItems = buffer.warnings.Select(er => factory.FromErrorResult(
                            _serviceProvider,
                            er,
                            VSTASKPRIORITY.TP_NORMAL,
                            VSTASKCATEGORY.CAT_BUILDCOMPILE)
                        );
                        var errorItems = buffer.errors.Select(er => factory.FromErrorResult(
                            _serviceProvider,
                            er,
                            VSTASKPRIORITY.TP_HIGH,
                            VSTASKCATEGORY.CAT_BUILDCOMPILE)
                        );


                        _errorProvider.ReplaceItems(
                            entry,
                            ParserTaskMoniker,
                            errorItems.Concat(warningItems).ToList()
                        );
                    } else {
                        _errorProvider.Clear(entry, ParserTaskMoniker);
                    }
                }

                if (!entry.SuppressErrorList && _commentTaskProvider != null) {
                    if (buffer.tasks.Any()) {
                        var taskItems = buffer.tasks.Select(x => new TaskProviderItem(
                               _serviceProvider,
                               x.message,
                               TaskProviderItemFactory.GetSpan(x),
                               GetPriority(x.priority),
                               GetCategory(x.category),
                               x.squiggle,
                               translator
                           )
                       );

                        _commentTaskProvider.ReplaceItems(
                            entry,
                            ParserTaskMoniker,
                            taskItems.ToList()
                        );
                    } else {
                        _commentTaskProvider.Clear(entry, ParserTaskMoniker);
                    }
                }
            }

            if (!entry.SuppressErrorList) {
                bool changed = false;
                lock (_hasParseErrorsLock) {
                    changed = hasErrors ? _hasParseErrors.Add(entry) : _hasParseErrors.Remove(entry);
                }
                if (changed) {
                    OnShouldWarnOnLaunchChanged(entry);
                }
            }

            entry.OnParseComplete();
        }

        private static VSTASKCATEGORY GetCategory(AP.TaskCategory category) {
            switch (category) {
                case AP.TaskCategory.buildCompile: return VSTASKCATEGORY.CAT_BUILDCOMPILE;
                case AP.TaskCategory.comments: return VSTASKCATEGORY.CAT_COMMENTS;
                default: return VSTASKCATEGORY.CAT_MISC;
            }
        }

        private static VSTASKPRIORITY GetPriority(AP.TaskPriority priority) {
            switch (priority) {
                case AP.TaskPriority.high: return VSTASKPRIORITY.TP_HIGH;
                case AP.TaskPriority.low: return VSTASKPRIORITY.TP_LOW;
                default: return VSTASKPRIORITY.TP_NORMAL;
            }
        }

        private AP.TaskPriority GetPriority(VSTASKPRIORITY value) {
            switch (value) {
                case VSTASKPRIORITY.TP_HIGH: return AP.TaskPriority.high;
                case VSTASKPRIORITY.TP_LOW: return AP.TaskPriority.low;
                default: return AP.TaskPriority.normal;
            }
        }

        #region Implementation Details

        private SignatureAnalysis TryGetLiveSignatures(ITextSnapshot snapshot, int paramIndex, string text, ITrackingSpan applicableSpan, string lastKeywordArg) {
            var eval = snapshot.TextBuffer.GetInteractiveWindow()?.Evaluator as IPythonInteractiveIntellisense;
            if (eval != null) {
                if (text.EndsWith("(")) {
                    text = text.Substring(0, text.Length - 1);
                }
                var liveSigs = eval.GetSignatureDocumentation(text);

                if (liveSigs != null && liveSigs.Length > 0) {
                    return new SignatureAnalysis(text, paramIndex, GetLiveSignatures(text, liveSigs, paramIndex, applicableSpan, lastKeywordArg), lastKeywordArg);
                }
            }
            return null;
        }

        private ISignature[] GetLiveSignatures(string text, ICollection<OverloadDoc> liveSigs, int paramIndex, ITrackingSpan span, string lastKeywordArg) {
            ISignature[] res = new ISignature[liveSigs.Count];
            int i = 0;
            foreach (var sig in liveSigs) {
                res[i++] = new PythonSignature(
                    this,
                    span,
                    new AP.Signature() {
                        name = text,
                        doc = sig.Documentation,
                        parameters = sig.Parameters
                            .Select(
                                x => new AP.Parameter() {
                                    name = x.Name,
                                    doc = x.Documentation,
                                    type = x.Type,
                                    defaultValue = x.DefaultValue,
                                    optional = x.IsOptional
                                }
                            ).ToArray()
                    },
                    paramIndex,
                    lastKeywordArg
                );
            }
            return res;
        }

        internal PythonToolsService PyService {
            get {
                return _pyService;
            }
        }

        internal bool ShouldEvaluateForCompletion(string source) {
            switch (_pyService.InteractiveOptions.CompletionMode) {
                case ReplIntellisenseMode.AlwaysEvaluate: return true;
                case ReplIntellisenseMode.NeverEvaluate: return false;
                case ReplIntellisenseMode.DontEvaluateCalls:
                    var parser = Parser.CreateParser(new StringReader(source), _interpreterFactory.GetLanguageVersion());

                    var stmt = parser.ParseSingleStatement();
                    var exprWalker = new ExprWalker();

                    stmt.Walk(exprWalker);
                    return exprWalker.ShouldExecute;
                default: throw new InvalidOperationException();
            }
        }

        class ExprWalker : PythonWalker {
            public bool ShouldExecute = true;

            public override bool Walk(CallExpression node) {
                ShouldExecute = false;
                return base.Walk(node);
            }
        }

        private static CompletionAnalysis TrySpecialCompletions(IServiceProvider serviceProvider, ICompletionSession session, ITextView view, ITextSnapshot snapshot, ITrackingSpan span, ITrackingPoint point, CompletionOptions options) {
            var snapSpan = span.GetSpan(snapshot);
            var buffer = snapshot.TextBuffer;
            var classifier = buffer.GetPythonClassifier();
            if (classifier == null) {
                return null;
            }

            var parser = new ReverseExpressionParser(snapshot, buffer, span);
            var statementRange = parser.GetStatementRange();
            if (!statementRange.HasValue) {
                statementRange = snapSpan.Start.GetContainingLine().Extent;
            }
            if (snapSpan.Start < statementRange.Value.Start) {
                return null;
            }

            if (snapshot.IsReplBufferWithCommand()) {
                return CompletionAnalysis.EmptyCompletionContext;
            }

            var tokens = classifier.GetClassificationSpans(snapSpan);
            if (tokens.LastOrDefault()?.ClassificationType.IsOfType(PredefinedClassificationTypeNames.String) ?? false) {
                // String completion
                if (span.GetStartPoint(snapshot).GetContainingLine().LineNumber == span.GetEndPoint(snapshot).GetContainingLine().LineNumber) {
                    return new StringLiteralCompletionList(serviceProvider, session, view, span, buffer, options);
                }
            }

            tokens = classifier.GetClassificationSpans(new SnapshotSpan(statementRange.Value.Start, snapSpan.Start));
            if (tokens.Count > 0) {
                // Check for context-sensitive intellisense
                var lastClass = tokens[tokens.Count - 1];

                if (lastClass.ClassificationType == classifier.Provider.Comment) {
                    // No completions in comments
                    return CompletionAnalysis.EmptyCompletionContext;
                } else if (lastClass.ClassificationType == classifier.Provider.Operator &&
                    lastClass.Span.GetText() == "@") {

                    if (tokens.Count == 1) {
                        return new DecoratorCompletionAnalysis(serviceProvider, session, view, span, buffer, options);
                    }
                    // TODO: Handle completions automatically popping up
                    // after '@' when it is used as a binary operator.
                } else if (CompletionAnalysis.IsKeyword(lastClass, "def")) {
                    return new OverrideCompletionAnalysis(serviceProvider, session, view, span, buffer, options);
                }

                // Import completions
                var first = tokens[0];
                if (CompletionAnalysis.IsKeyword(first, "import")) {
                    return ImportCompletionAnalysis.Make(tokens, serviceProvider, session, view, span, buffer, options);
                } else if (CompletionAnalysis.IsKeyword(first, "from")) {
                    return FromImportCompletionAnalysis.Make(tokens, serviceProvider, session, view, span, buffer, options);
                } else if (CompletionAnalysis.IsKeyword(first, "raise") || CompletionAnalysis.IsKeyword(first, "except")) {
                    if (tokens.Count == 1 ||
                        lastClass.ClassificationType.IsOfType(PythonPredefinedClassificationTypeNames.Comma) ||
                        (lastClass.IsOpenGrouping() && tokens.Count < 3)) {
                        return new ExceptionCompletionAnalysis(serviceProvider, session, view, span, buffer, options);
                    }
                }
            }

            return null;
        }

        private static CompletionAnalysis GetNormalCompletionContext(IServiceProvider serviceProvider, ICompletionSession session, ITextView view, ITextSnapshot snapshot, ITrackingSpan applicableSpan, ITrackingPoint point, CompletionOptions options) {
            var span = applicableSpan.GetSpan(snapshot);

            if (IsSpaceCompletion(snapshot, point) && !IntellisenseController.ForceCompletions) {
                return CompletionAnalysis.EmptyCompletionContext;
            }

            var parser = new ReverseExpressionParser(snapshot, snapshot.TextBuffer, applicableSpan);
            if (parser.IsInGrouping()) {
                options = options.Clone();
                options.IncludeStatementKeywords = false;
            }

            AnalysisEntry entry;
            if (view.TryGetAnalysisEntry(snapshot.TextBuffer, serviceProvider, out entry)) {
                return new NormalCompletionAnalysis(
                    entry.Analyzer,
                    session,
                    view,
                    snapshot,
                    applicableSpan,
                    snapshot.TextBuffer,
                    options,
                    serviceProvider
                );
            }

            return CompletionAnalysis.EmptyCompletionContext;
        }

        private static bool IsSpaceCompletion(ITextSnapshot snapshot, ITrackingPoint loc) {
            var pos = loc.GetPosition(snapshot);
            if (pos > 0) {
                return snapshot.GetText(pos - 1, 1) == " ";
            }
            return false;
        }

        private static Stopwatch MakeStopWatch() {
            var res = new Stopwatch();
            res.Start();
            return res;
        }

        internal async Task UnloadFileAsync(AnalysisEntry entry) {
            _analysisComplete = false;
            AnalysisEntry removed;
            _projectFiles.TryRemove(entry.Path, out removed);
            _projectFilesById.TryRemove(entry.FileId, out removed);

            if (!entry.SuppressErrorList) {
                _errorProvider?.Clear(entry, ParserTaskMoniker);
                _errorProvider?.Clear(entry, UnresolvedImportMoniker);
                _commentTaskProvider?.Clear(entry, ParserTaskMoniker);
            }

            await SendRequestAsync(new AP.UnloadFileRequest() { fileId = entry.FileId }).ConfigureAwait(false);
        }

        internal void ClearAllTasks() {
            _errorProvider?.ClearAll();
            _commentTaskProvider?.ClearAll();

            lock (_hasParseErrorsLock) {
                _hasParseErrors.Clear();
            }
        }

        internal bool ShouldWarnOnLaunch(AnalysisEntry entry) {
            lock (_hasParseErrorsLock) {
                return _hasParseErrors.Contains(entry);
            }
        }

        private void OnShouldWarnOnLaunchChanged(AnalysisEntry entry) {
            var evt = ShouldWarnOnLaunchChanged;
            if (evt != null) {
                evt(this, new EntryEventArgs(entry));
            }
        }

        internal event EventHandler<EntryEventArgs> ShouldWarnOnLaunchChanged;

        #endregion

        internal async Task<T> SendRequestAsync<T>(
            Request<T> request,
            T defaultValue = default(T),
            TimeSpan? timeout = null
        ) where T : Response, new() {
            var conn = _conn;
            if (conn == null) {
                return default(T);
            }
            Debug.WriteLine(String.Format("{1} Sending request {0}", request, DateTime.Now));
            T res = defaultValue;
            CancellationToken cancel;
            CancellationTokenSource linkedSource = null, timeoutSource = null;

            try {
                cancel = _processExitedCancelSource.Token;
            } catch (ObjectDisposedException) {
                // Raced with disposal
                return res;
            }

            if (timeout.HasValue) {
                timeoutSource = new CancellationTokenSource(timeout.Value);
                linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeoutSource.Token);
                cancel = linkedSource.Token;
            }

            try {
                res = await conn.SendRequestAsync(request, cancel).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationCancelled);
            } catch (IOException) {
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationCancelled);
            } catch (FailedRequestException e) {
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationFailed, e.Message);
            } catch (ObjectDisposedException) {
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationCancelled);
            } finally {
                linkedSource?.Dispose();
                timeoutSource?.Dispose();
            }
            Debug.WriteLine(String.Format("{1} Done sending request {0}", request, DateTime.Now));
            return res;
        }

        internal async Task SendEventAsync(Event eventValue) {
            var conn = _conn;
            if (conn == null) {
                return;
            }
            Debug.WriteLine(String.Format("{1} Sending event {0}", eventValue.name, DateTime.Now));
            try {
                await conn.SendEventAsync(eventValue).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationCancelled);
            } catch (IOException) {
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationCancelled);
            } catch (FailedRequestException e) {
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationFailed, e.Message);
            }
            Debug.WriteLine(String.Format("{1} Done sending event {0}", eventValue.name, DateTime.Now));
        }

        internal async void SendEvent(Event eventValue) {
            try {
                await SendEventAsync(eventValue);
            } catch (Exception ex) when (!ex.IsCriticalException()) {
                // Nothing we can do now except crash. We'll log it instead
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationFailed, ex.ToString());
                Debug.Fail("Unexpected error sending event");
            }
        }

        internal async Task<IEnumerable<CompletionResult>> GetAllAvailableMembersAsync(AnalysisEntry entry, SourceLocation location, GetMemberOptions options) {
            var members = await SendRequestAsync(new AP.TopLevelCompletionsRequest() {
                fileId = entry.FileId,
                options = options,
                location = location.Index,
                column = location.Column
            }).ConfigureAwait(false);

            if (members != null) {
                return ConvertMembers(members.completions);
            }

            return Enumerable.Empty<CompletionResult>();
        }

        private static CompletionResult ToMemberResult(AP.Completion member) {
            return new CompletionResult(
                member.name,
                member.completion,
                member.doc,
                member.memberType,
                member.detailedValues
            );
        }

        internal async Task<IEnumerable<CompletionResult>> GetMembersAsync(AnalysisEntry entry, string text, SourceLocation location, GetMemberOptions options) {
            AP.CompletionsResponse members;
            using (new DebugTimer("GetMembersAsync")) {
                members = await SendRequestAsync(new AP.CompletionsRequest() {
                    fileId = entry.FileId,
                    text = text,
                    options = options,
                    location = location.Index,
                    column = location.Column
                }).ConfigureAwait(false);
            }

            if (members != null) {
                return ConvertMembers(members.completions);
            }

            return Enumerable.Empty<CompletionResult>();
        }

        private IEnumerable<CompletionResult> ConvertMembers(AP.Completion[] completions) {
            foreach (var member in completions) {
                yield return ToMemberResult(member);
            }
        }

        internal async Task<IEnumerable<CompletionResult>> GetModulesResult(bool topLevelOnly) {
            var members = await SendRequestAsync(new AP.GetModulesRequest() {
                topLevelOnly = topLevelOnly
            }).ConfigureAwait(false);

            if (members != null) {
                return ConvertMembers(members.completions);
            }

            return Enumerable.Empty<CompletionResult>();
        }

        internal async Task<IEnumerable<CompletionResult>> GetModuleMembersAsync(AnalysisEntry entry, string[] package, bool includeMembers) {
            var members = await SendRequestAsync(new AP.GetModuleMembersRequest() {
                fileId = entry.FileId,
                package = package,
                includeMembers = includeMembers
            }).ConfigureAwait(false);

            if (members != null) {
                return ConvertMembers(members.completions);
            }

            return Enumerable.Empty<CompletionResult>();
        }

        internal async Task<string[]> FindMethodsAsync(AnalysisEntry entry, ITextBuffer textBuffer, string className, int? paramCount) {
            var res = await SendRequestAsync(
                new AP.FindMethodsRequest() {
                    fileId = entry.FileId,
                    bufferId = entry.GetBufferId(textBuffer),
                    className = className,
                    paramCount = paramCount
                }
            ).ConfigureAwait(false);

            return res?.names ?? Array.Empty<string>();
        }

        internal async Task<InsertionPoint> GetInsertionPointAsync(AnalysisEntry entry, ITextBuffer textBuffer, string className) {
            var lastVersion = entry.GetAnalysisVersion(textBuffer);

            var res = await SendRequestAsync(
                    new AP.MethodInsertionLocationRequest() {
                        fileId = entry.FileId,
                        bufferId = entry.GetBufferId(textBuffer),
                        className = className
                    }
                ).ConfigureAwait(false);

            if (res != null) {
                var translator = new LocationTracker(lastVersion, textBuffer, res.version);

                return new InsertionPoint(
                    translator.TranslateForward(res.location),
                    res.indentation
                );
            }
            return null;
        }

        internal async Task<AP.MethodInfoResponse> GetMethodInfoAsync(AnalysisEntry entry, ITextBuffer textBuffer, string className, string methodName) {
            return await SendRequestAsync(
                new AP.MethodInfoRequest() {
                    fileId = entry.FileId,
                    bufferId = entry.GetBufferId(textBuffer),
                    className = className,
                    methodName = methodName
                }
            ).ConfigureAwait(false);
        }


        private VersionedResponse<T> VersionedResponse<T>(T data, ITextBuffer textBuffer, ITextVersion versionBeforeRequest) {
            return new VersionedResponse<T>(data, textBuffer, versionBeforeRequest);
        }

        internal async Task<VersionedResponse<AP.AnalysisClassificationsResponse>> GetAnalysisClassificationsAsync(AnalysisEntry projFile, ITextBuffer textBuffer, bool colorNames) {
            var lastVersion = projFile.GetAnalysisVersion(textBuffer);

            var res = await SendRequestAsync(
                    new AP.AnalysisClassificationsRequest() {
                        fileId = projFile.FileId,
                        bufferId = projFile.GetBufferId(textBuffer),
                        colorNames = colorNames
                    }
                ).ConfigureAwait(false);

            if (res != null) {
                return VersionedResponse(
                    res,
                    textBuffer,
                    lastVersion
                );
            }
            return null;
        }

        internal async Task<AP.LocationNameResponse> GetNameOfLocationAsync(AnalysisEntry entry, ITextBuffer textBuffer, int line, int column) {
            return await SendRequestAsync(
                new AP.LocationNameRequest() {
                    fileId = entry.FileId,
                    bufferId = entry.GetBufferId(textBuffer),
                    line = line,
                    column = column
                }
            ).ConfigureAwait(false);
        }

        internal async Task<string[]> GetProximityExpressionsAsync(AnalysisEntry entry, ITextBuffer textBuffer, int line, int column, int lineCount) {
            return (await SendRequestAsync(
                new AP.ProximityExpressionsRequest() {
                    fileId = entry.FileId,
                    bufferId = entry.GetBufferId(textBuffer),
                    line = line,
                    column = column,
                    lineCount = lineCount
                }
            ).ConfigureAwait(false)).names ?? Array.Empty<string>();
        }

        internal async Task FormatCodeAsync(SnapshotSpan span, ITextView view, CodeFormattingOptions options, bool selectResult) {
            var fileInfo = view.GetAnalysisEntry(span.Snapshot.TextBuffer, _serviceProvider);
            var buffer = span.Snapshot.TextBuffer;

            await fileInfo.EnsureCodeSyncedAsync(buffer);

            var bufferId = fileInfo.GetBufferId(buffer);
            var lastAnalyzed = fileInfo.GetAnalysisVersion(buffer);

            var res = await SendRequestAsync(
                new AP.FormatCodeRequest() {
                    fileId = fileInfo.FileId,
                    bufferId = bufferId,
                    startIndex = span.Start,
                    endIndex = span.End,
                    options = options,
                    newLine = view.Options.GetNewLineCharacter()
                }
            );

            if (res != null && res.version != -1) {
                ITrackingSpan selectionSpan = null;
                if (selectResult) {
                    var translator = new LocationTracker(lastAnalyzed, buffer, res.version);
                    int start = translator.TranslateForward(res.startIndex);
                    int end = translator.TranslateForward(res.endIndex);
                    Debug.Assert(
                        start < view.TextBuffer.CurrentSnapshot.Length,
                        String.Format("Bad span: {0} vs {1} (was {2} before translation, from {3} to {4})",
                            start,
                            view.TextBuffer.CurrentSnapshot.Length,
                            res.startIndex,
                            res.version,
                            view.TextBuffer.CurrentSnapshot.Version.VersionNumber
                        )
                    );
                    Debug.Assert(
                        end <= view.TextBuffer.CurrentSnapshot.Length,
                        String.Format(
                            "Bad span: {0} vs {1} (was {2} before translation, from {3} to {4})",
                            end,
                            view.TextBuffer.CurrentSnapshot.Length,
                            res.endIndex,
                            res.version,
                            view.TextBuffer.CurrentSnapshot.Version.VersionNumber
                        )
                    );

                    selectionSpan = view.TextBuffer.CurrentSnapshot.CreateTrackingSpan(
                        Span.FromBounds(start, end),
                        SpanTrackingMode.EdgeInclusive
                    );
                }

                ApplyChanges(res.changes, lastAnalyzed, buffer, res.version);

                if (selectResult && !view.IsClosed) {
                    view.Selection.Select(selectionSpan.GetSpan(view.TextBuffer.CurrentSnapshot), false);
                }
            }
        }

        internal async Task RemoveImportsAsync(ITextView view, ITextBuffer textBuffer, int index, bool allScopes) {
            var fileInfo = view.GetAnalysisEntry(textBuffer, _serviceProvider);
            await fileInfo.EnsureCodeSyncedAsync(textBuffer);
            var lastAnalyzed = fileInfo.GetAnalysisVersion(textBuffer);

            var res = await SendRequestAsync(
                new AP.RemoveImportsRequest() {
                    fileId = fileInfo.FileId,
                    bufferId = fileInfo.GetBufferId(textBuffer),
                    allScopes = allScopes,
                    index = index
                }
            );

            if (res != null) {
                ApplyChanges(
                    res.changes,
                    lastAnalyzed,
                    textBuffer,
                    res.version
                );
            }
        }

        internal async Task<IEnumerable<ExportedMemberInfo>> FindNameInAllModulesAsync(string name, CancellationToken cancel = default(CancellationToken)) {
            CancellationTokenRegistration registration1 = default(CancellationTokenRegistration), registration2 = default(CancellationTokenRegistration);
            if (cancel.CanBeCanceled) {
                CancellationTokenSource source = new CancellationTokenSource();
                registration1 = cancel.Register(() => source.Cancel());
                registration2 = _processExitedCancelSource.Token.Register(() => source.Cancel());
                cancel = source.Token;
            } else {
                cancel = _processExitedCancelSource.Token;
            }

            var conn = _conn;
            if (conn == null) {
                return new ExportedMemberInfo[0];
            }

            try {
                return (await conn.SendRequestAsync(new AP.AvailableImportsRequest() {
                    name = name
                }, cancel)).imports.Select(x => new ExportedMemberInfo(x.fromName, x.importName));
            } catch (OperationCanceledException) {
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationCancelled);
            } catch (FailedRequestException e) {
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationFailed, e.Message);
            } catch (ObjectDisposedException) {
                _pyService.Logger.LogEvent(Logging.PythonLogEvent.AnalysisOperationCancelled);
            } finally {
                registration1.Dispose();
                registration2.Dispose();
            }
            return Enumerable.Empty<ExportedMemberInfo>();
        }

        internal async Task<VersionedResponse<AP.ExtractMethodResponse>> ExtractMethodAsync(AnalysisEntry entry, ITextBuffer textBuffer, ITextView view, string name, string[] parameters, int? targetScope = null) {
            var bufferId = entry.GetBufferId(textBuffer);

            await entry.EnsureCodeSyncedAsync(textBuffer);
            var lastAnalyzed = entry.GetAnalysisVersion(textBuffer);
            var selection = view.GetPythonSelection().First();

            var res = await SendRequestAsync(new AP.ExtractMethodRequest() {
                fileId = entry.FileId,
                bufferId = bufferId,
                indentSize = view.Options.GetIndentSize(),
                convertTabsToSpaces = view.Options.IsConvertTabsToSpacesEnabled(),
                newLine = view.Options.GetNewLineCharacter(),
                startIndex = selection.Start.Position,
                endIndex = selection.End.Position,
                parameters = parameters,
                name = name,
                scope = targetScope,
                shouldExpandSelection = true
            });

            if (res != null) {
                return VersionedResponse(
                    res,
                    textBuffer,
                    lastAnalyzed
                );
            }
            return null;
        }

        internal async Task<AP.AddImportResponse> AddImportAsync(AnalysisEntry entry, ITextBuffer textBuffer, string fromModule, string name, string newLine) {
            var bufferId = entry.GetBufferId(textBuffer);

            return await SendRequestAsync(
                new AP.AddImportRequest() {
                    fromModule = fromModule,
                    name = name,
                    fileId = entry.FileId,
                    bufferId = bufferId,
                    newLine = newLine
                }
            ).ConfigureAwait(false);
        }

        private void CommentTaskTokensChanged(object sender, EventArgs e) {
            if (_commentTaskProvider == null) {
                return;
            }

            Dictionary<string, AP.TaskPriority> priorities = new Dictionary<string, AP.TaskPriority>();
            foreach (var keyValue in _commentTaskProvider.Tokens) {
                priorities[keyValue.Key] = GetPriority(keyValue.Value);
            }
            SendEvent(
                new AP.SetCommentTaskTokens() {
                    tokens = priorities
                }
            );
        }

        internal async Task<NavigationInfo> GetNavigationsAsync(ITextView view) {
            AnalysisEntry entry;
            var textBuffer = view.TextBuffer;
            if (view.TryGetAnalysisEntry(textBuffer, _serviceProvider, out entry)) {
                var lastVersion = entry.GetAnalysisVersion(textBuffer);

                var navigations = await SendRequestAsync(
                    new AP.NavigationRequest() {
                        fileId = entry.FileId,
                        bufferId = entry.GetBufferId(textBuffer)
                    }
                );

                if (navigations != null && navigations.version != -1 && navigations.version >= lastVersion.VersionNumber) {
                    List<NavigationInfo> bufferNavs = new List<NavigationInfo>();

                    LocationTracker translator = new LocationTracker(
                        lastVersion,
                        textBuffer,
                        navigations.version
                    );
                    bufferNavs.AddRange(ConvertNavigations(textBuffer.CurrentSnapshot, translator, navigations.navigations));

                    return new NavigationInfo(
                        null,
                        NavigationKind.None,
                        new SnapshotSpan(),
                        bufferNavs.ToArray()
                    );
                }
            }

            return new NavigationInfo(null, NavigationKind.None, new SnapshotSpan(), Array.Empty<NavigationInfo>());
        }

        private NavigationInfo[] ConvertNavigations(ITextSnapshot snapshot, LocationTracker translator, AP.Navigation[] navigations) {
            if (navigations == null) {
                return null;
            }

            List<NavigationInfo> res = new List<NavigationInfo>();
            foreach (var nav in navigations) {
                // translate the span from the version we last parsed to the current version
                var span = translator.TranslateForward(Span.FromBounds(nav.startIndex, nav.endIndex));

                res.Add(
                    new NavigationInfo(
                        nav.name,
                        GetNavigationKind(nav.type),
                        span,
                        ConvertNavigations(snapshot, translator, nav.children)
                    )
                );
            }
            return res.ToArray();
        }

        internal ProjectReference[] GetReferences() {
            lock (_references) {
                return _references.ToArray();
            }
        }

        internal async Task<AP.AddReferenceResponse> AddReferenceAsync(ProjectReference reference, CancellationToken token = default(CancellationToken)) {
            lock (_references) {
                _references.Add(reference);
            }
            return await SendRequestAsync(new AP.AddReferenceRequest() {
                reference = AP.ProjectReference.Convert(reference)
            }).ConfigureAwait(false);
        }

        internal async Task<AP.RemoveReferenceResponse> RemoveReferenceAsync(ProjectReference reference) {
            lock (_references) {
                _references.Remove(reference);
            }
            return await SendRequestAsync(
                new AP.RemoveReferenceRequest() {
                    reference = AP.ProjectReference.Convert(reference)
                }
            ).ConfigureAwait(false);
        }

        private NavigationKind GetNavigationKind(string type) {
            switch (type) {
                case "property": return NavigationKind.Property;
                case "function": return NavigationKind.Function;
                case "class": return NavigationKind.Class;
                case "classmethod": return NavigationKind.ClassMethod;
                case "staticmethod": return NavigationKind.StaticMethod;
                default: return NavigationKind.None;
            }
        }

        internal async Task<IEnumerable<OutliningTaggerProvider.TagSpan>> GetOutliningTagsAsync(ITextSnapshot snapshot) {
            var res = Enumerable.Empty<OutliningTaggerProvider.TagSpan>();
            var entry = snapshot.TextBuffer.GetAnalysisEntry(_serviceProvider);
            if (entry != null) {
                var lastVersion = entry.GetAnalysisVersion(snapshot.TextBuffer);

                var outliningTags = await SendRequestAsync(
                    new AP.OutlingRegionsRequest() {
                        fileId = entry.FileId,
                        bufferId = entry.GetBufferId(snapshot.TextBuffer)
                    }
                );

                if (outliningTags != null && outliningTags.version >= lastVersion.VersionNumber) {
                    Debug.WriteLine("Translating from {0} to {1}", outliningTags.version, snapshot.TextBuffer.CurrentSnapshot);
                    var translator = new LocationTracker(
                        lastVersion,
                        snapshot.TextBuffer,
                        outliningTags.version
                    );

                    res = ConvertOutliningTags(translator, outliningTags.tags);
                } else {
                    return null;
                }
            }

            return res;
        }

        private static IEnumerable<OutliningTaggerProvider.TagSpan> ConvertOutliningTags(LocationTracker translator, AP.OutliningTag[] tags) {
            var snapshot = translator.TextBuffer.CurrentSnapshot;
            foreach (var tag in tags) {
                // translate the span from the version we last parsed to the current version
                var span = translator.TranslateForward(Span.FromBounds(tag.startIndex, tag.endIndex));

                int headerIndex = tag.headerIndex;
                if (tag.headerIndex != -1) {
                    headerIndex = translator.TranslateForward(headerIndex);
                }

                yield return OutliningTaggerProvider.OutliningTagger.GetTagSpan(
                    snapshot,
                    span.Start,
                    span.End,
                    headerIndex
                );
            }
        }

        internal async Task<AP.OverridesCompletionResponse> GetOverrideCompletionsAsync(AnalysisEntry entry, ITextBuffer textBuffer, SourceLocation location, string indentation) {
            var res = await SendRequestAsync(
                new AP.OverridesCompletionRequest() {
                    fileId = entry.FileId,
                    bufferId = entry.GetBufferId(textBuffer),
                    column = location.Column,
                    index = location.Index,
                    line = location.Line,
                    indentation = indentation
                }
            ).ConfigureAwait(false);

            return res;
        }

        internal static void ApplyChanges(AP.ChangeInfo[] changes, ITextVersion lastVersion, ITextBuffer textBuffer, int fromVersion) {
            var translator = new LocationTracker(lastVersion, textBuffer, fromVersion);

            ApplyChanges(changes, textBuffer, translator);
        }

        internal static void ApplyChanges(AP.ChangeInfo[] changes, ITextBuffer textBuffer, LocationTracker translator) {
            if (translator == null) {
                throw new ArgumentNullException(nameof(translator));
            }

            using (var edit = textBuffer.CreateEdit()) {
                foreach (var change in changes.MaybeEnumerate()) {
                    edit.Replace(
                        translator.TranslateForward(new Span(change.start, change.length)),
                        change.newText
                    );
                }
                edit.Apply();
            }
        }

        internal static async Task<QuickInfo> GetQuickInfoAsync(
            IServiceProvider serviceProvider,
            ITextView view,
            SnapshotPoint point,
            TimeSpan? timeout = null
        ) {
            var analysis = GetApplicableExpression(
                view.GetAnalysisEntry(point.Snapshot.TextBuffer, serviceProvider),
                point
            );

            if (analysis != null) {
                var location = analysis.Location;
                var req = new AP.QuickInfoRequest() {
                    expr = analysis.Text,
                    column = location.Column,
                    index = location.Index,
                    line = location.Line,
                    fileId = analysis.Entry.FileId
                };

                var quickInfo = await analysis.Entry.Analyzer.SendRequestAsync(req, timeout: timeout).ConfigureAwait(false);

                if (quickInfo != null) {
                    return new QuickInfo(quickInfo.text, analysis.Span);
                }
            }

            return null;
        }

        internal AnalysisVariable ToAnalysisVariable(AP.AnalysisReference arg) {
            VariableType type = VariableType.None;
            switch (arg.kind) {
                case "definition": type = VariableType.Definition; break;
                case "reference": type = VariableType.Reference; break;
                case "value": type = VariableType.Value; break;
            }

            var location = new AnalysisLocation(
                arg.file,
                arg.line,
                arg.column
            );
            return new AnalysisVariable(type, location);
        }

        internal static async Task<string> ExpressionForDataTipAsync(
            IServiceProvider serviceProvider,
            ITextView view,
            SnapshotSpan span,
            TimeSpan? timeout = null
        ) {
            var analysis = GetApplicableExpression(
                view.GetAnalysisEntry(span.Start.Snapshot.TextBuffer, serviceProvider),
                span.Start
            );
            string result = null;

            if (analysis != null) {
                var location = analysis.Location;
                var req = new AP.ExpressionForDataTipRequest() {
                    expr = span.GetText(),
                    column = location.Column,
                    index = location.Index,
                    line = location.Line,
                    fileId = analysis.Entry.FileId,
                };

                var resp = await analysis.Entry.Analyzer.SendRequestAsync(req, timeout: timeout).ConfigureAwait(false);

                if (resp != null) {
                    result = resp.expression;
                }
            }

            return result;
        }

        class ApplicableExpression {
            public readonly string Text;
            public readonly AnalysisEntry Entry;
            public readonly ITrackingSpan Span;
            public readonly SourceLocation Location;

            public ApplicableExpression(AnalysisEntry entry, string text, ITrackingSpan span, SourceLocation location) {
                Entry = entry;
                Text = text;
                Span = span;
                Location = location;
            }
        }

        private static ApplicableExpression GetApplicableExpression(AnalysisEntry entry, SnapshotPoint point) {
            if (entry != null) {
                var snapshot = point.Snapshot;
                var buffer = snapshot.TextBuffer;
                var span = snapshot.CreateTrackingSpan(
                    point.Position == snapshot.Length ?
                        new Span(point.Position, 0) :
                        new Span(point.Position, 1),
                    SpanTrackingMode.EdgeInclusive
                );

                ReverseExpressionParser parser = new ReverseExpressionParser(snapshot, buffer, span);

                var exprRange = parser.GetExpressionRange(false);
                if (exprRange != null) {
                    string text = exprRange.Value.GetText();

                    var applicableTo = parser.Snapshot.CreateTrackingSpan(
                        exprRange.Value.Span,
                        SpanTrackingMode.EdgeExclusive
                    );

                    if (text.Length > 0) {
                        var loc = parser.Span.GetSpan(parser.Snapshot.Version);
                        var lineNo = parser.Snapshot.GetLineNumberFromPosition(loc.Start);

                        var location = TranslateIndex(loc.Start, parser.Snapshot, entry);
                        return new ApplicableExpression(
                            entry,
                            text,
                            applicableTo,
                            location
                        );
                    }
                }
            }

            return null;
        }
    }
}
