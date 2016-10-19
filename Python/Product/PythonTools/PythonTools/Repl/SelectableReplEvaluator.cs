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
using System.Threading.Tasks;
using Microsoft.PythonTools.Editor.Core;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Intellisense;
using Microsoft.VisualStudio.InteractiveWindow;
using Microsoft.VisualStudio.InteractiveWindow.Commands;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.PythonTools.Repl {
    [InteractiveWindowRole("Execution")]
    [InteractiveWindowRole("Reset")]
    [ContentType(PythonCoreConstants.ContentType)]
    [ContentType(PredefinedInteractiveCommandsContentTypes.InteractiveCommandContentTypeName)]
    sealed class SelectableReplEvaluator : 
        IInteractiveEvaluator,
        IPythonInteractiveEvaluator,
        IMultipleScopeEvaluator,
        IPythonInteractiveIntellisense,
        IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IReadOnlyList<IInteractiveEvaluatorProvider> _providers;

        private IInteractiveEvaluator _evaluator;
        private string _evaluatorId;
        private IInteractiveWindow _window;

        private readonly string _settingsCategory;

        public event EventHandler EvaluatorChanged;
        public event EventHandler AvailableEvaluatorsChanged;

        public event EventHandler<EventArgs> AvailableScopesChanged;
        public event EventHandler<EventArgs> MultipleScopeSupportChanged;

        public SelectableReplEvaluator(
            IServiceProvider serviceProvider,
            IEnumerable<IInteractiveEvaluatorProvider> providers,
            string initialReplId,
            string windowId
        ) {
            _serviceProvider = serviceProvider;

            _providers = providers.ToArray();
            foreach (var provider in _providers) {
                provider.EvaluatorsChanged += Provider_EvaluatorsChanged;
            }

            _settingsCategory = GetSettingsCategory(windowId);

            if (!string.IsNullOrEmpty(initialReplId)) {
                _evaluatorId = initialReplId;
            }
        }

        internal static string GetSettingsCategory(string windowId) {
            if (string.IsNullOrEmpty(windowId)) {
                return null;
            }
            return "InteractiveWindows\\" + windowId;
        }

        private void ClearPersistedEvaluator() {
            if (string.IsNullOrEmpty(_settingsCategory)) {
                return;
            }

            _serviceProvider.GetPythonToolsService().DeleteCategory(_settingsCategory);
        }

        private void PersistEvaluator() {
            if (string.IsNullOrEmpty(_settingsCategory)) {
                return;
            }

            var pyEval = _evaluator as PythonInteractiveEvaluator;
            if (pyEval == null) {
                // Assume we can restore the evaluator next time
                _serviceProvider.GetPythonToolsService().SaveString("Id", _settingsCategory, _evaluatorId);
                return;
            }
            if (pyEval.Configuration == null) {
                // Invalid configuration - don't serialize it
                ClearPersistedEvaluator();
                return;
            }
            if (!string.IsNullOrEmpty(pyEval.ProjectMoniker)) {
                // Directly related to a project - don't serialize it
                ClearPersistedEvaluator();
                return;
            }
            var id = pyEval.Configuration.Interpreter.Id;
            if (string.IsNullOrEmpty(id)) {
                // Invalid as it has no id - don't serialize it
                ClearPersistedEvaluator();
                return;
            }

            // Only serialize it if the interpreter promises to be available
            // next time.
            var registry = _serviceProvider.GetComponentModel().GetService<IInterpreterRegistryService>();
            var obj = registry.GetProperty(id, "PersistInteractive");
            if (obj is bool && (bool)obj || (obj as string).IsTrue()) {
                _serviceProvider.GetPythonToolsService().SaveString("Id", _settingsCategory, _evaluatorId);
            } else {
                ClearPersistedEvaluator();
            }
        }

        private void Provider_EvaluatorsChanged(object sender, EventArgs e) {
            AvailableEvaluatorsChanged?.Invoke(this, EventArgs.Empty);
        }

        public IInteractiveEvaluator Evaluator => _evaluator;
        public string CurrentEvaluator => _evaluatorId;


        public bool IsDisconnected => (_evaluator as IPythonInteractiveEvaluator)?.IsDisconnected ?? true;
        public bool IsExecuting => (_evaluator as IPythonInteractiveEvaluator)?.IsExecuting ?? false;
        public string DisplayName => (_evaluator as IPythonInteractiveEvaluator)?.DisplayName;

        public bool LiveCompletionsOnly => (_evaluator as IPythonInteractiveIntellisense)?.LiveCompletionsOnly ?? false;

        public VsProjectAnalyzer Analyzer => (_evaluator as IPythonInteractiveIntellisense)?.Analyzer;
        public string AnalysisFilename => (_evaluator as IPythonInteractiveIntellisense)?.AnalysisFilename;

        // Test methods
        internal string PrimaryPrompt => ((dynamic)_evaluator)?.PrimaryPrompt ?? ">>> ";
        internal string SecondaryPrompt => ((dynamic)_evaluator)?.SecondaryPrompt ?? "... ";

        public void SetEvaluator(string id) {
            if (_evaluatorId == id && _evaluator != null) {
                return;
            }

            var eval = string.IsNullOrEmpty(id) ?
                null :
                _providers.Select(p => p.GetEvaluator(id)).FirstOrDefault(e => e != null);

            var oldEval = _evaluator;
            _evaluator = null;
            if (oldEval != null) {
                DetachWindow(oldEval);
                DetachMultipleScopeHandling(oldEval);
            }

            _evaluator = eval;
            _evaluatorId = id;

            if (eval != null) {
                eval.CurrentWindow = CurrentWindow;
                if (eval.CurrentWindow != null) {
                    // Otherwise, we'll initialize when the window is set
                    DoInitializeAsync(eval).DoNotWait();
                }
            }
            UpdateCaption();
            PersistEvaluator();

            EvaluatorChanged?.Invoke(this, EventArgs.Empty);
            AttachMultipleScopeHandling(eval);
        }

        private async Task DoInitializeAsync(IInteractiveEvaluator eval) {
            await eval.InitializeAsync();

            var view = eval?.CurrentWindow?.TextView;
            var buffer = eval?.CurrentWindow?.CurrentLanguageBuffer;
            if (view != null && buffer != null) {
                var controller = IntellisenseControllerProvider.GetOrCreateController(_serviceProvider, _serviceProvider.GetComponentModel(), view);
                controller.DisconnectSubjectBuffer(buffer);
                controller.ConnectSubjectBuffer(buffer);
            }
        }

        private void DetachWindow(IInteractiveEvaluator oldEval) {
            var oldView = oldEval?.CurrentWindow?.TextView;
            if (oldView != null) {
                foreach (var buffer in oldView.BufferGraph.GetTextBuffers(EditorExtensions.IsPythonContent)) {
                    if (oldEval.CurrentWindow.CurrentLanguageBuffer == buffer) {
                        continue;
                    }
                    buffer.Properties[BufferParser.DoNotParse] = BufferParser.DoNotParse;
                }
            }
        }

        private void UpdateCaption() {
            var window = CurrentWindow;
            if (window == null) {
                return;
            }

            var viw = InteractiveWindowProvider.GetVsInteractiveWindow(window);
            if (viw == null) {
                return;
            }

            var twp = viw as ToolWindowPane;
            if (twp == null) {
                return;
            }

            var display = DisplayName;
            if (!string.IsNullOrEmpty(display)) {
                twp.Caption = Strings.ReplCaption.FormatUI(display);
            } else {
                twp.Caption = Strings.ReplCaptionNoEvaluator;
            }
        }

        public IEnumerable<KeyValuePair<string, string>> AvailableEvaluators {
            get {
                return _providers.SelectMany(e => e.GetEvaluators());
            }
        }

        public IInteractiveWindow CurrentWindow {
            get { return _window; }
            set {
                var oldWindow = InteractiveWindowProvider.GetVsInteractiveWindow(_window);
                if (oldWindow != null) {
                    var events = InteractiveWindowEvents.TryGet(oldWindow);
                    events.Closed -= InteractiveWindow_Closed;
                }

                _window = value;
                var newWindow = InteractiveWindowProvider.GetVsInteractiveWindow(value);

                var eval = _evaluator;
                if (eval != null && eval.CurrentWindow != value) {
                    eval.CurrentWindow = value;
                    if (value != null) {
                        DoInitializeAsync(eval).DoNotWait();
                    }
                }
                UpdateCaption();
            }
        }

        internal void ProvideInteractiveWindowEvents(InteractiveWindowEvents events) {
            events.Closed += InteractiveWindow_Closed;
        }

        private void InteractiveWindow_Closed(object sender, EventArgs e) {
            ClearPersistedEvaluator();
        }

        #region Multiple Scope Support

        private void AttachMultipleScopeHandling(IInteractiveEvaluator evaluator) {
            var mse = evaluator as IMultipleScopeEvaluator;
            if (mse == null) {
                return;
            }
            mse.AvailableScopesChanged += Evaluator_AvailableScopesChanged;
            mse.MultipleScopeSupportChanged += Evaluator_MultipleScopeSupportChanged;
            MultipleScopeSupportChanged?.Invoke(this, EventArgs.Empty);
            AvailableScopesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DetachMultipleScopeHandling(IInteractiveEvaluator evaluator) {
            var mse = evaluator as IMultipleScopeEvaluator;
            if (mse == null) {
                return;
            }
            mse.AvailableScopesChanged -= Evaluator_AvailableScopesChanged;
            mse.MultipleScopeSupportChanged -= Evaluator_MultipleScopeSupportChanged;
            MultipleScopeSupportChanged?.Invoke(this, EventArgs.Empty);
            AvailableScopesChanged?.Invoke(this, EventArgs.Empty);
        }

        public string CurrentScopeName => (_evaluator as IMultipleScopeEvaluator)?.CurrentScopeName;
        public string CurrentScopePath => (_evaluator as IMultipleScopeEvaluator)?.CurrentScopePath;
        public bool EnableMultipleScopes => (_evaluator as IMultipleScopeEvaluator)?.EnableMultipleScopes ?? false;

        private void Evaluator_MultipleScopeSupportChanged(object sender, EventArgs e) {
            MultipleScopeSupportChanged?.Invoke(this, e);
        }

        private void Evaluator_AvailableScopesChanged(object sender, EventArgs e) {
            AvailableScopesChanged?.Invoke(this, e);
        }

        public void SetScope(string scopeName) {
            (_evaluator as IMultipleScopeEvaluator)?.SetScope(scopeName);
        }

        public IEnumerable<string> GetAvailableScopes() {
            return (_evaluator as IMultipleScopeEvaluator)?.GetAvailableScopes() ?? Enumerable.Empty<string>();
        }

        #endregion

        public void AbortExecution() {
            _evaluator?.AbortExecution();
        }

        public bool CanExecuteCode(string text) {
            return _evaluator?.CanExecuteCode(text) ?? false;
        }

        public void Dispose() {
            _evaluator?.Dispose();
            _evaluator = null;
            _window = null;
        }

        public Task<ExecutionResult> ExecuteCodeAsync(string text) {
            return _evaluator?.ExecuteCodeAsync(text) ?? ExecutionResult.Failed;
        }

        public string FormatClipboard() {
            return _evaluator?.FormatClipboard();
        }

        public string GetPrompt() {
            return _evaluator?.GetPrompt() ?? ">>> ";
        }

        public Task<ExecutionResult> InitializeAsync() {
            if (_evaluator == null && !string.IsNullOrEmpty(_evaluatorId)) {
                SetEvaluator(_evaluatorId);
                return ExecutionResult.Succeeded;
            }

            return _evaluator?.InitializeAsync() ?? ExecutionResult.Succeeded;
        }

        public Task<ExecutionResult> ResetAsync(bool initialize = true) {
            return _evaluator?.ResetAsync(initialize) ?? ExecutionResult.Succeeded;
        }

        public Task<bool> ExecuteFileAsync(string filename, string extraArgs) {
            return (_evaluator as IPythonInteractiveEvaluator)?.ExecuteFileAsync(filename, extraArgs)
                ?? Task.FromResult(false);
        }

        public IEnumerable<KeyValuePair<string, string>> GetAvailableScopesAndPaths() {
            return (_evaluator as IPythonInteractiveIntellisense)?.GetAvailableScopesAndPaths()
                ?? Enumerable.Empty<KeyValuePair<string, string>>();
        }

        public CompletionResult[] GetMemberNames(string text) {
            return (_evaluator as IPythonInteractiveIntellisense)?.GetMemberNames(text)
                ?? new CompletionResult[0];
        }

        public OverloadDoc[] GetSignatureDocumentation(string text) {
            return (_evaluator as IPythonInteractiveIntellisense)?.GetSignatureDocumentation(text)
                ?? new OverloadDoc[0];
        }
    }
}
