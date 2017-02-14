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
using System.Linq;
using Microsoft.PythonTools.InteractiveWindow;
using Microsoft.PythonTools.InteractiveWindow.Shell;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.PythonTools.Repl {
    [Export(typeof(InteractiveWindowProvider))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    class InteractiveWindowProvider {
        private readonly Dictionary<int, IVsInteractiveWindow> _windows = new Dictionary<int, IVsInteractiveWindow>();
        private int _nextId = 1;

        private readonly List<IVsInteractiveWindow> _mruWindows = new List<IVsInteractiveWindow>();
        private readonly Dictionary<string, int> _temporaryWindows = new Dictionary<string, int>();

        private readonly IServiceProvider _serviceProvider;
        private readonly IInteractiveEvaluatorProvider[] _evaluators;
        private readonly IVsInteractiveWindowFactory _windowFactory;
        private readonly IContentType _pythonContentType;

        private static readonly object VsInteractiveWindowKey = new object();

        private const string SavedWindowsCategoryBase = "InteractiveWindows\\";

        [ImportingConstructor]
        public InteractiveWindowProvider(
            [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider,
            [Import] IVsInteractiveWindowFactory factory,
            [ImportMany] IInteractiveEvaluatorProvider[] evaluators,
            [Import] IContentTypeRegistryService contentTypeService
        ) {
            _serviceProvider = serviceProvider;
            _evaluators = evaluators;
            _windowFactory = factory;
            _pythonContentType = contentTypeService.GetContentType(PythonCoreConstants.ContentType);
        }

        public IEnumerable<IVsInteractiveWindow> AllOpenWindows {
            get {
                lock (_windows) {
                    return _windows.Values.ToArray();
                }
            }
        }

        private int GetNextId() {
            lock (_windows) {
                do {
                    var curId = _nextId++;
                    if (!_windows.ContainsKey(curId)) {
                        return curId;
                    }
                } while (_nextId < int.MaxValue);
            }
            throw new InvalidOperationException(Strings.ReplWindowOutOfIds);
        }

        private bool EnsureInterpretersAvailable() {
            var registry = _serviceProvider.GetComponentModel().GetService<IInterpreterRegistryService>();
            if (registry.Configurations.Any()) {
                return true;
            }

            PythonToolsPackage.OpenNoInterpretersHelpPage(_serviceProvider);
            return false;
        }

        public IVsInteractiveWindow Open(string replId) {
            EnsureInterpretersAvailable();

            lock (_windows) {
                foreach(var window in _windows.Values) {
                    var eval = window.InteractiveWindow?.Evaluator as SelectableReplEvaluator;
                    if (eval?.CurrentEvaluator == replId) {
                        window.Show(true);
                        return window;
                    }
                }
            }

            return null;
        }

        public IVsInteractiveWindow OpenOrCreate(string replId) {
            EnsureInterpretersAvailable();

            IVsInteractiveWindow wnd;
            lock (_windows) {
                foreach(var window in _windows.Values) {
                    var eval = window.InteractiveWindow?.Evaluator as SelectableReplEvaluator;
                    if (eval?.CurrentEvaluator == replId) {
                        window.Show(true);
                        return window;
                    }
                }
            }

            wnd = Create(replId);
            wnd.Show(true);
            return wnd;
        }

        public IVsInteractiveWindow Create(string replId, int curId = -1) {
            EnsureInterpretersAvailable();

            if (curId < 0) {
                curId = GetNextId();
            }

            var window = CreateInteractiveWindowInternal(
                new SelectableReplEvaluator(_serviceProvider, _evaluators, replId, curId.ToString()),
                _pythonContentType,
                true,
                curId,
                Strings.ReplCaptionNoEvaluator,
                typeof(Navigation.PythonLanguageInfo).GUID,
                "PythonInteractive"
            );

            lock (_windows) {
                _windows[curId] = window;
            }

            window.InteractiveWindow.TextView.Closed += (s, e) => {
                lock (_windows) {
                    Debug.Assert(ReferenceEquals(_windows[curId], window));
                    _windows.Remove(curId);
                }
            };

            return window;
        }

        public IVsInteractiveWindow OpenOrCreateTemporary(string replId, string title) {
            bool dummy;
            return OpenOrCreateTemporary(replId, title, out dummy);
        }

        public IVsInteractiveWindow OpenOrCreateTemporary(string replId, string title, out bool wasCreated) {
            EnsureInterpretersAvailable();

            IVsInteractiveWindow wnd;
            lock (_windows) {
                int curId;
                if (_temporaryWindows.TryGetValue(replId, out curId)) {
                    if (_windows.TryGetValue(curId, out wnd)) {
                        wnd.Show(true);
                        wasCreated = false;
                        return wnd;
                    }
                }
                _temporaryWindows.Remove(replId);
            }

            wnd = CreateTemporary(replId, title);
            wnd.Show(true);
            wasCreated = true;
            return wnd;
        }

        public IVsInteractiveWindow CreateTemporary(string replId, string title) {
            EnsureInterpretersAvailable();

            int curId = GetNextId();

            var window = CreateInteractiveWindowInternal(
                _evaluators.Select(p => p.GetEvaluator(replId)).FirstOrDefault(e => e != null),
                _pythonContentType,
                false,
                curId,
                title,
                typeof(Navigation.PythonLanguageInfo).GUID,
                replId
            );

            lock (_windows) {
                _windows[curId] = window;
                _temporaryWindows[replId] = curId;
            }

            window.InteractiveWindow.TextView.Closed += (s, e) => {
                lock (_windows) {
                    Debug.Assert(ReferenceEquals(_windows[curId], window));
                    _windows.Remove(curId);
                    _temporaryWindows.Remove(replId);
                }
            };

            return window;
        }

        private IVsInteractiveWindow CreateInteractiveWindowInternal(
            IInteractiveEvaluator evaluator,
            IContentType contentType,
            bool alwaysCreate,
            int id,
            string title,
            Guid languageServiceGuid,
            string replId
        ) {
            var creationFlags =
                __VSCREATETOOLWIN.CTW_fMultiInstance |
                __VSCREATETOOLWIN.CTW_fActivateWithProject;

            if (alwaysCreate) {
                creationFlags |= __VSCREATETOOLWIN.CTW_fForceCreate;
            }

            var replWindow = _windowFactory.Create(GuidList.guidPythonInteractiveWindowGuid, id, title, evaluator, creationFlags);
            replWindow.InteractiveWindow.Properties[VsInteractiveWindowKey] = replWindow;
            var toolWindow = replWindow as ToolWindowPane;
            if (toolWindow != null) {
                toolWindow.BitmapImageMoniker = KnownMonikers.PYInteractiveWindow;
            }
            replWindow.SetLanguage(GuidList.guidPythonLanguageServiceGuid, contentType);
            replWindow.InteractiveWindow.InitializeAsync();

            var selectEval = evaluator as SelectableReplEvaluator;
            if (selectEval != null) {
                selectEval.ProvideInteractiveWindowEvents(InteractiveWindowEvents.GetOrCreate(replWindow));
            }

            return replWindow;
        }

        internal static IVsInteractiveWindow GetVsInteractiveWindow(IInteractiveWindow window) {
            IVsInteractiveWindow wnd = null;
            return (window?.Properties.TryGetProperty(VsInteractiveWindowKey, out wnd) ?? false) ? wnd : null;
        }

        internal static void Close(object obj) {
            var frame = ((obj as ToolWindowPane)?.Frame as IVsWindowFrame);
            frame?.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
        }

        internal static void CloseIfEvaluatorMatches(object obj, string evalId) {
            var eval = (obj as IVsInteractiveWindow)?.InteractiveWindow.Evaluator as SelectableReplEvaluator;
            if (eval?.CurrentEvaluator == evalId) {
                Close(obj);
            }
        }
    }
}
