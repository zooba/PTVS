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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Cdp;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.Project;
using SR = Microsoft.PythonTools.Project.SR;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.PythonTools.Repl {
    partial class PythonInteractiveEvaluator {
        protected virtual CommandProcessorThread Connect() {
            _serviceProvider.GetUIThread().MustBeCalledFromUIThreadOrThrow();

            if (string.IsNullOrWhiteSpace(InterpreterPath)) {
                WriteError(SR.GetString(SR.ReplEvaluatorInterpreterNotConfigured, DisplayName));
                return null;
            } else if (!File.Exists(InterpreterPath)) {
                WriteError(SR.GetString(SR.ReplEvaluatorInterpreterNotFound));
                return null;
            }

            var processInfo = new ProcessStartInfo(InterpreterPath);

            processInfo.CreateNoWindow = true;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardOutput = true;
            processInfo.RedirectStandardError = true;
            processInfo.RedirectStandardInput = true;

            if (!string.IsNullOrEmpty(WorkingDirectory)) {
                processInfo.WorkingDirectory = WorkingDirectory;
            } else {
                processInfo.WorkingDirectory = CommonUtils.GetParent(processInfo.FileName);
            }

            var existingEnv = processInfo.Environment;

            foreach (var kv in EnvironmentVariables) {
                var key = kv.Key.Trim(';');
                if (kv.Key.EndsWith(";")) {
                    string other;
                    if (existingEnv.TryGetValue(key, out other)) {
                        processInfo.Environment[key] = kv.Value + ";" + other;
                    } else {
                        processInfo.Environment[key] = kv.Value;
                    }
                } else if (kv.Key.StartsWith(";")) {
                    string other;
                    if (existingEnv.TryGetValue(key, out other)) {
                        processInfo.Environment[key] = other + ";" + kv.Value;
                    } else {
                        processInfo.Environment[key] = kv.Value;
                    }
                } else {
                    processInfo.Environment[key] = kv.Value;
                }
            }

            var args = new List<string>();
            if (!string.IsNullOrWhiteSpace(InterpreterArguments)) {
                args.Add(InterpreterArguments);
            }

            args.Add(ProcessOutput.QuoteSingleArgument(PythonToolsInstallPath.GetFile("visualstudio_py_repl2.py")));
            processInfo.Arguments = string.Join(" ", args);

            Process process;
            try {
                if (!File.Exists(processInfo.FileName)) {
                    throw new Win32Exception(Microsoft.VisualStudioTools.Project.NativeMethods.ERROR_FILE_NOT_FOUND);
                }
                process = Process.Start(processInfo);
                if (process.WaitForExit(100)) {
                    throw new Win32Exception(process.ExitCode);
                }
            } catch (Win32Exception e) {
                if (e.NativeErrorCode == Microsoft.VisualStudioTools.Project.NativeMethods.ERROR_FILE_NOT_FOUND) {
                    WriteError(SR.GetString(SR.ReplEvaluatorInterpreterNotFound));
                } else {
                    WriteError(SR.GetString(SR.ErrorStartingInteractiveProcess, e.ToString()));
                }
                return null;
            } catch (Exception e) when (!e.IsCriticalException()) {
                return null;
            }

            return new CommandProcessorThread(this, process);
        }



        protected class CommandProcessorThread : IDisposable {
            private readonly PythonInteractiveEvaluator _eval;
            private readonly Process _process;
            private readonly Stream _stream;
            private readonly Connection _connection;

            private Task _connecting;

            private readonly Dictionary<int, TaskCompletionSource<Response>> _completions;

            //private Action _deferredExecute;

            //private OverloadDoc[] _overloads;
            //private Dictionary<string, string> _fileToModuleName;
            //private Dictionary<string, bool> _allModules;
            private StringBuilder _preConnectionOutput;
            private string _currentScope = "__main__";
            //private MemberResults _memberResults;

            public CommandProcessorThread(PythonInteractiveEvaluator evaluator, Process process) {
                _process = process;
                _eval = evaluator;
                _preConnectionOutput = new StringBuilder();
                _completions = new Dictionary<int, TaskCompletionSource<Response>>();

                _stream = _process.StandardInput.BaseStream;
                _process.ErrorDataReceived += StdErrReceived;
                _process.Exited += ProcessExited;
                _process.EnableRaisingEvents = true;

                _process.BeginErrorReadLine();

                _connection = new Connection(
                    _process.StandardInput.BaseStream,
                    _process.StandardOutput.BaseStream
                );
                _connection.EventReceived += Connection_EventReceived;

                _connecting = ConnectAsync();
            }

            private async void Connection_EventReceived(object sender, EventReceivedEventArgs e) {
                OutputEvent oe;

                if ((oe = OutputEvent.TryCreate(e.Event)) != null) {
                    if (oe.Category == "stderr") {
                        _eval.WriteError(oe.Output, addNewline: false);
                    } else if (oe.Category == "stdout") {
                        _eval.WriteOutput(oe.Output, addNewline: false);
                    } else {
                        _eval.WriteError(oe.Category + ": " + oe.Output, addNewline: false);
                    }
                }
            }

            public void EnsureConnected(CancellationToken cancellationToken) {
                var c = _connecting;
                if (c == null) {
                    return;
                }
                try {
                    c.Wait(cancellationToken);
                } catch (AggregateException ae) {
                    throw ae.InnerException;
                }
            }

            public async Task EnsureConnectedAsync() {
                var c = _connecting;
                if (c == null) {
                    return;
                }
                await c;
            }

            private async Task ConnectAsync() {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5))) {
                    await _connection.SendRequestAsync(new InitializeRequest(), cts.Token);
                }
            }

            public bool IsConnected => _connecting?.Status == TaskStatus.RanToCompletion;

            public string CurrentScope => _currentScope;

            public bool IsProcessExpectedToExit { get; set; }

            private void StartOutputThread() {
            }

            private static string FixNewLines(string input) {
                return input.Replace("\r\n", "\n").Replace('\r', '\n');
            }

            private static string UnfixNewLines(string input) {
                return input.Replace("\r\n", "\n");
            }

            private void ProcessExited(object sender, EventArgs e) {
                _connecting = null;

                var pco = Interlocked.Exchange(ref _preConnectionOutput, null);
                if (pco != null) {
                    lock (pco) {
                        try {
                            _eval.WriteError(pco.ToString(), addNewline: false);
                        } catch (Exception ex) when (!ex.IsCriticalException()) {
                        }
                    }
                }

                if (!IsProcessExpectedToExit) {
                    try {
                        _eval.WriteError(SR.GetString(SR.ReplExited));
                    } catch (Exception ex) when (!ex.IsCriticalException()) {
                    }
                }
                IsProcessExpectedToExit = false;
            }

            private void StdErrReceived(object sender, DataReceivedEventArgs e) {
                if (e.Data == null) {
                    return;
                }
                if (!AppendPreConnectionOutput(e)) {
                    _eval.WriteError(FixNewLines(e.Data));
                }
            }

            private bool AppendPreConnectionOutput(DataReceivedEventArgs e) {
                var pco = Volatile.Read(ref _preConnectionOutput);
                if (pco != null) {
                    lock (pco) {
                        pco.Append(FixNewLines(e.Data) + Environment.NewLine);
                        return true;
                    }
                }
                return false;
            }

            //private void HandleReadLine() {
            //    // perform the input on a new thread so that we don't block
            //    // additional commands (such as output) from being processed by
            //    // us (this is called on the output thread)
            //    var window = _eval.CurrentWindow;
            //    ThreadPool.QueueUserWorkItem(x => {
            //        string input = window?.ReadStandardInput()?.ReadToEnd();
            //        input = input != null ? UnfixNewLines(input) : "\n";
            //        try {
            //            using (new StreamLock(this, throwIfDisconnected: true)) {
            //                _stream.Write(InputLineCommandBytes);
            //                SendString(input);
            //            }
            //        } catch (IOException) {
            //        }
            //    });
            //}

            //private void HandleDebuggerDetach() {
            //    _eval.OnDetach();
            //}

            //private void HandleDisplayPng() {
            //    Debug.Assert(Monitor.IsEntered(_streamLock));

            //    int len = _stream.ReadInt32();
            //    byte[] buffer = new byte[len];
            //    _stream.ReadToFill(buffer);
            //    DisplayImage(buffer);
            //}

            //private void HandleDisplayXaml() {
            //    Debug.Assert(Monitor.IsEntered(_streamLock));

            //    int len = _stream.ReadInt32();
            //    byte[] buffer = new byte[len];
            //    _stream.ReadToFill(buffer);

            //    _eval.InvokeAsync(() => {
            //        try {
            //            var fe = XamlReader.Load(new MemoryStream(buffer)) as FrameworkElement;
            //            if (fe != null) {
            //                _eval.WriteFrameworkElement(fe, fe.DesiredSize);
            //            }
            //        } catch (Exception ex) when (!ex.IsCriticalException()) {
            //            _eval.WriteError(ex.ToString());
            //            return;
            //        }
            //    }).DoNotWait();
            //}

            //private void HandlePromptChanged() {
            //    Debug.Assert(Monitor.IsEntered(_streamLock));

            //    var prompt1 = _stream.ReadString();
            //    var prompt2 = _stream.ReadString();
            //    Trace.TraceInformation("New prompts: \"{0}\" \"{1}\"", prompt1, prompt2);

            //    PrimaryPrompt = prompt1;
            //    SecondaryPrompt = prompt2;
            //}

            public event EventHandler AvailableScopesChanged {
                add { }
                remove { }
            }

            //private void HandleModulesChanged() {
            //    // modules changed
            //    using (new StreamUnlock(this)) {
            //        AvailableScopesChanged?.Invoke(this, EventArgs.Empty);
            //    }
            //}

            //private void HandleImageDisplay() {
            //    Debug.Assert(Monitor.IsEntered(_streamLock));

            //    string filename = _stream.ReadString();
            //    try {
            //        DisplayImage(File.ReadAllBytes(filename));
            //    } catch (IOException) {
            //        // can't read the file
            //        _eval.WriteError(SR.GetString(SR.ReplCannotReadFile, filename));
            //    }
            //}

            //private void DisplayImage(byte[] bytes) {
            //    _eval.InvokeAsync(() => {
            //        var imageSrc = new BitmapImage();
            //        try {
            //            imageSrc.BeginInit();
            //            imageSrc.StreamSource = new MemoryStream(bytes);
            //            imageSrc.EndInit();
            //        } catch (IOException) {
            //            return;
            //        }

            //        var img = new Image {
            //            Source = imageSrc,
            //            Stretch = Stretch.Uniform,
            //            StretchDirection = StretchDirection.Both
            //        };
            //        var control = new Border {
            //            Child = img,
            //            Background = Brushes.White
            //        };

            //        _eval.WriteFrameworkElement(control, new Size(imageSrc.PixelWidth, imageSrc.PixelHeight));
            //    });
            //}

            //private void HandleModuleList() {
            //    Debug.Assert(Monitor.IsEntered(_streamLock));

            //    int moduleCount = _stream.ReadInt32();
            //    var moduleNames = new Dictionary<string, string>();
            //    var allModules = new Dictionary<string, bool>();
            //    for (int i = 0; i < moduleCount; i++) {
            //        string name = _stream.ReadString();
            //        string filename = _stream.ReadString();
            //        if (!String.IsNullOrWhiteSpace(filename)) {
            //            moduleNames[filename] = name;
            //            allModules[name] = true;
            //        } else {
            //            allModules[name] = false;
            //        }
            //    }

            //    _fileToModuleName = moduleNames;
            //    _allModules = allModules;
            //    _completionResultEvent.Set();
            //}

            //private void HandleSigError() {
            //    _completionResultEvent.Set();
            //}

            //private void HandleSigResult() {
            //    Debug.Assert(Monitor.IsEntered(_streamLock));

            //    int overloadCount = _stream.ReadInt32();
            //    OverloadDoc[] docs = new OverloadDoc[overloadCount];
            //    for (int i = 0; i < overloadCount; i++) {
            //        string doc = _stream.ReadString();
            //        int paramCount = _stream.ReadInt32();

            //        ParameterResult[] parameters = new ParameterResult[paramCount];
            //        for (int curParam = 0; curParam < paramCount; curParam++) {
            //            string name = _stream.ReadString();
            //            int equals = name.IndexOf('=');
            //            if (equals < 0) {
            //                parameters[curParam] = new ParameterResult(name);
            //            } else {
            //                parameters[curParam] = new ParameterResult(
            //                    name.Remove(equals),
            //                    null,
            //                    null,
            //                    // Even though it has a default, don't mark the
            //                    // parameter as optional (for consistency with
            //                    // signature help from the database)
            //                    false,
            //                    null,
            //                    name.Substring(equals + 1)
            //                );
            //            }
            //        }

            //        docs[i] = new OverloadDoc(doc, parameters);
            //    }
            //    _overloads = docs;
            //    _completionResultEvent.Set();
            //}

            //private void HandleMemberResult() {
            //    Debug.Assert(Monitor.IsEntered(_streamLock));

            //    string typeName = _stream.ReadString();
            //    var instDict = ReadMemberDict();
            //    var typeDict = ReadMemberDict();
            //    _memberResults = new MemberResults(typeName, instDict, typeDict);

            //    _completionResultEvent.Set();
            //}

            //private void HandleMemberResultError() {
            //    _memberResults = null;
            //    _completionResultEvent.Set();
            //}

            //private void HandleOutput() {
            //    Debug.Assert(Monitor.IsEntered(_streamLock));

            //    string data = _stream.ReadString();
            //    if (data != null) {
            //        Trace.TraceInformation("Data = \"{0}\"", FixNewLines(data).Replace("\r\n", "\\r\\n"));
            //        using (new StreamUnlock(this)) {
            //            _eval.WriteOutput(FixNewLines(data), addNewline: false);
            //        }
            //    }
            //}

            //private void HandleError() {
            //    Debug.Assert(Monitor.IsEntered(_streamLock));

            //    string data = _stream.ReadString();
            //    Trace.TraceInformation("Data = \"{0}\"", FixNewLines(data).Replace("\r\n", "\\r\\n"));
            //    using (new StreamUnlock(this)) {
            //        _eval.WriteError(FixNewLines(data), addNewline: false);
            //    }
            //}

            //private void HandleExecutionError() {
            //    // ERRE command
            //    lock (_completionLock) {
            //        if (_completion != null) {
            //            _completion.SetResult(ExecutionResult.Failure);
            //            _completion = null;
            //        }
            //    }
            //}

            //private void HandleExecutionDone() {
            //    // DONE command
            //    lock (_completionLock) {
            //        if (_completion != null) {
            //            _completion.SetResult(ExecutionResult.Success);
            //            _completion = null;
            //        }
            //    }
            //}

            private async Task<Response> ExecuteAsync(LaunchRequest request, CancellationToken cancellationToken) {
                await EnsureConnectedAsync();

                if (_process != null) {
                    VisualStudioTools.Project.NativeMethods.AllowSetForegroundWindow(_process.Id);
                }

                var resp = await _connection.SendRequestAsync(request, cancellationToken);
                if (!resp.Success) {
                    _eval.WriteError(resp.Message);
                }
                return resp;
            }

            public async Task ExecuteTextAsync(string text, CancellationToken cancellationToken) {
                if (text.StartsWith("$")) {
                    _eval.WriteError(SR.GetString(SR.ReplUnknownCommand, text.Trim()));
                    return;
                }

                Trace.TraceInformation("Executing text: {0}", text);

                // normalize line endings to \n which is all older versions of CPython can handle.
                text = FixNewLines(text).TrimEnd(' ');

                var resp = await ExecuteAsync(new LaunchRequest {
                    Code = text
                }, cancellationToken);

                var er = EvaluateResponse.TryCreate(resp);
                if (er != null) {
                    foreach (var r in er.Reprs ?? new[] { er.Result }) {
                        if (!string.IsNullOrEmpty(r)) {
                            _eval.WriteOutput(r);
                        }
                    }
                }
            }

            public Task ExecuteScriptAsync(string filename, string extraArgs, CancellationToken cancellationToken) {
                return ExecuteAsync(new LaunchRequest {
                    ScriptPath = filename,
                    ExtraArguments = extraArgs
                }, cancellationToken);
            }

            public Task ExecuteModuleAsync(string moduleName, string extraArgs, CancellationToken cancellationToken) {
                return ExecuteAsync(new LaunchRequest {
                    ModuleName = moduleName,
                    ExtraArguments = extraArgs
                }, cancellationToken);
            }

            public Task ExecuteProcessAsync(string filename, string extraArgs, CancellationToken cancellationToken) {
                return ExecuteAsync(new LaunchRequest {
                    ProcessPath = filename,
                    ExtraArguments = extraArgs
                }, cancellationToken);
            }

            public void AbortCommand() {
                //    using (new StreamLock(this, throwIfDisconnected: true)) {
                //        _stream.Write(AbortCommandBytes);
                //    }
            }

            //public void SetThreadAndFrameCommand(long thread, int frame, FrameKind frameKind) {
            //    using (new StreamLock(this, throwIfDisconnected: true)) {
            //        _stream.Write(SetThreadAndFrameCommandBytes);
            //        _stream.WriteInt64(thread);
            //        _stream.WriteInt32(frame);
            //        _stream.WriteInt32((int)frameKind);
            //        _currentScope = "<CurrentFrame>";
            //    }
            //}

            public IReadOnlyList<OverloadDoc> GetSignatureDocumentation(string text) {
                Response response;

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(0.5))) {
                    try {
                        EnsureConnected(cts.Token);

                        response = _connection.SendRequest(new EvaluateRequest(text) {
                            IncludeDocs = true
                        }, cts.Token);
                    } catch (OperationCanceledException) {
                        return null;
                    }
                }

                if (!response.Success) {
                    _eval.WriteError(response.Message);
                    return null;
                }

                EvaluateResponse er;
                if ((er = EvaluateResponse.TryCreate(response)) == null) {
                    return null;
                }

                return new[] { new OverloadDoc(er.Docs?.LastOrDefault(), new ParameterResult[0]) };
            }

            public IReadOnlyList<MemberResult> GetMemberNames(string text) {
                Response response;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1))) {
                    try {
                        EnsureConnected(cts.Token);
                        response = _connection.SendRequest(new EvaluateRequest(text) {
                            IncludeMembers = true
                        }, cts.Token);
                    } catch (OperationCanceledException) {
                        return null;
                    }
                }

                if (!response.Success) {
                    _eval.WriteError(response.Message);
                    return null;
                }

                EvaluateResponse er;
                if ((er = EvaluateResponse.TryCreate(response)) == null) {
                    return null;
                }

                return er.Members?.LastOrDefault()?.Select(CreateMemberResult).ToArray();
            }

            private static MemberResult CreateMemberResult(string name) {
                int colon = name.IndexOf(':');
                if (colon < 0) {
                    return new MemberResult(name, PythonMemberType.Field);
                }
                var typeName = name.Substring(colon + 1).Trim();
                name = name.Remove(colon).Trim();

                switch (typeName) {
                    case "method-wrapper":
                    case "builtin_function_or_method":
                    case "method_descriptor":
                    case "wrapper_descriptor":
                    case "instancemethod":
                        return new MemberResult(name, PythonMemberType.Method);
                    case "getset_descriptor":
                        return new MemberResult(name, PythonMemberType.Property);
                    case "namespace#":
                        return new MemberResult(name, PythonMemberType.Namespace);
                    case "type":
                        return new MemberResult(name, PythonMemberType.Class);
                    case "function":
                        return new MemberResult(name, PythonMemberType.Function);
                    case "module":
                        return new MemberResult(name, PythonMemberType.Module);
                }

                return new MemberResult(name, PythonMemberType.Field);
            }

            //public async Task<string> GetScopeByFilenameAsync(string path) {
            //    await GetAvailableScopesAndKindAsync();

            //    string res;
            //    if (_fileToModuleName.TryGetValue(path, out res)) {
            //        return res;
            //    }
            //    return null;
            //}

            //public void SetScope(string scopeName) {
            //    try {
            //        using (new StreamLock(this, throwIfDisconnected: true)) {
            //            if (!string.IsNullOrWhiteSpace(scopeName)) {
            //                _stream.Write(SetModuleCommandBytes);
            //                SendString(scopeName);
            //                _currentScope = scopeName;

            //                _eval.WriteOutput(SR.GetString(SR.ReplModuleChanged, scopeName));
            //            } else {
            //                _eval.WriteOutput(_currentScope);
            //            }
            //        }
            //    } catch (DisconnectedException) {
            //        _eval.WriteError(SR.GetString(SR.ReplModuleCannotChange));
            //    } catch (IOException) {
            //    }
            //}

            //public Task<IEnumerable<string>> GetAvailableUserScopesAsync(int timeout = -1) {
            //    return Task.Run(() => {
            //        try {
            //            AutoResetEvent evt;
            //            using (new StreamLock(this, throwIfDisconnected: true)) {
            //                _stream.Write(GetModulesListCommandBytes);
            //                evt = _completionResultEvent;
            //            }
            //            evt.WaitOne(timeout);
            //            return _fileToModuleName?.Values.AsEnumerable();
            //        } catch (IOException) {
            //        }

            //        return null;
            //    });
            //}

            //public Task<IEnumerable<KeyValuePair<string, bool>>> GetAvailableScopesAndKindAsync(int timeout = -1) {
            //    return Task.Run(() => {
            //        try {
            //            AutoResetEvent evt;
            //            using (new StreamLock(this, throwIfDisconnected: true)) {
            //                _stream.Write(GetModulesListCommandBytes);
            //                evt = _completionResultEvent;
            //            }
            //            evt.WaitOne(timeout);
            //            return _allModules.AsEnumerable();
            //        } catch (IOException) {
            //        }

            //        return null;
            //    });
            //}

            public void Dispose() {
                if (_process != null && !_process.HasExited) {
                    try {
                        _process.Kill();
                    } catch (InvalidOperationException) {
                    } catch (Win32Exception) {
                        // race w/ killing the process
                    }
                }

                lock (_completions) {
                    foreach (var tcs in _completions.Values) {
                        tcs.TrySetCanceled();
                    }
                }
            }

            //private void SendString(string text) {
            //    Debug.Assert(text != null, "text should not be null");
            //    byte[] bytes = Encoding.UTF8.GetBytes(text);
            //    _stream.WriteInt32(bytes.Length);
            //    _stream.Write(bytes);
            //}

            //private Dictionary<string, string> ReadMemberDict() {
            //    int memCount = _stream.ReadInt32();
            //    var dict = new Dictionary<string, string>(memCount);
            //    for (int i = 0; i < memCount; i++) {
            //        string memName = _stream.ReadString();
            //        string typeName = _stream.ReadString();
            //        dict[memName] = typeName;
            //    }

            //    return dict;
            //}

            public bool IsExecuting => false; // _completion != null && !_completion.Task.IsCompleted;

            public string PrimaryPrompt { get; internal set; }

            public string SecondaryPrompt { get; internal set; }


            class MemberResults {
                public readonly string TypeName;
                public readonly Dictionary<string, string> InstanceMembers;
                public readonly Dictionary<string, string> TypeMembers;

                public MemberResults(
                    string typeName,
                    Dictionary<string, string> instMembers,
                    Dictionary<string, string> typeMembers
                ) {
                    TypeName = typeName;
                    InstanceMembers = instMembers;
                    TypeMembers = typeMembers;
                }
            }
        }
    }
}
