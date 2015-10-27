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
using System.Linq;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.InteractiveWindow;
using Microsoft.VisualStudio.InteractiveWindow.Shell;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Utilities;
using Microsoft.Win32;

namespace Microsoft.PythonTools.Repl {
    [Export(typeof(InteractiveWindowProvider))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    class InteractiveWindowProvider {
        private readonly Dictionary<int, InteractiveWindowInfo> _windows = new Dictionary<int, InteractiveWindowInfo>();
        private readonly IInteractiveEvaluatorProvider[] _evaluators;
        private readonly IVsInteractiveWindowFactory _windowFactory;

        [ImportingConstructor]
        public InteractiveWindowProvider([Import]IVsInteractiveWindowFactory factory, [ImportMany]IInteractiveEvaluatorProvider[] evaluators) {
            _evaluators = evaluators;
            _windowFactory = factory;
        }

        class InteractiveWindowInfo {
            public readonly string Id;
            public readonly IVsInteractiveWindow Window;

            public InteractiveWindowInfo(IVsInteractiveWindow replWindow, string replId) {
                Window = replWindow;
                Id = replId;
            }
        }

        public IVsInteractiveWindow FindReplWindow(string replId) {
            foreach (var idAndWindow in _windows) {
                var window = idAndWindow.Value;
                if (window.Id == replId) {
                    return window.Window;
                }
            }
            return null;
        }

        public IVsInteractiveWindow CreateInteractiveWindow(
            IContentType contentType,
            string/*!*/ title,
            Guid languageServiceGuid,
            string replId
        ) {
            int curId = 0;

            InteractiveWindowInfo window;
            do {
                curId++;
                window = FindReplWindowInternal(curId);
            } while (window != null);

            foreach (var provider in _evaluators) {
                var evaluator = provider.GetEvaluator(replId);
                if (evaluator != null) {
                    string[] roles = evaluator.GetType().GetCustomAttributes(typeof(InteractiveWindowRoleAttribute), true)
                        .OfType<InteractiveWindowRoleAttribute>()
                        .Select(r => r.Name)
                        .ToArray();
                    window = CreateInteractiveWindowInternal(evaluator, contentType, roles, curId, title, languageServiceGuid, replId);

                    return window.Window;
                }
            }

            throw new InvalidOperationException(String.Format("ReplId {0} was not provided by an IInteractiveWindowProvider", replId));
        }

        private InteractiveWindowInfo FindReplWindowInternal(int id) {
            InteractiveWindowInfo res;
            if (_windows.TryGetValue(id, out res)) {
                return res;
            }
            return null;
        }

        private const string ActiveReplsKey = "ActiveRepls";
        private const string ContentTypeKey = "ContentType";
        private const string RolesKey = "Roles";
        private const string TitleKey = "Title";
        private const string ReplIdKey = "ReplId";
        private const string LanguageServiceGuidKey = "LanguageServiceGuid";

        private static RegistryKey GetRegistryRoot() {
            return VSRegistry.RegistryRoot(__VsLocalRegistryType.RegType_UserSettings, writable: true).CreateSubKey(ActiveReplsKey);
        }

        private void SaveInteractiveInfo(int id, IInteractiveEvaluator evaluator, IContentType contentType, string[] roles, string title, Guid languageServiceGuid, string replId) {
            using (var root = GetRegistryRoot()) {
                if (root != null) {
                    using (var replInfo = root.CreateSubKey(id.ToString())) {
                        replInfo.SetValue(ContentTypeKey, contentType.TypeName);
                        replInfo.SetValue(TitleKey, title);
                        replInfo.SetValue(ReplIdKey, replId.ToString());
                        replInfo.SetValue(LanguageServiceGuidKey, languageServiceGuid.ToString());
                    }
                }
            }
        }

        internal bool CreateFromRegistry(IComponentModel model, int id) {
            string contentTypeName, title, replId, languageServiceId;

            using (var root = GetRegistryRoot()) {
                if (root == null) {
                    return false;
                }

                using (var replInfo = root.OpenSubKey(id.ToString())) {
                    if (replInfo == null) {
                        return false;
                    }

                    contentTypeName = replInfo.GetValue(ContentTypeKey) as string;
                    if (contentTypeName == null) {
                        return false;
                    }

                    title = replInfo.GetValue(TitleKey) as string;
                    if (title == null) {
                        return false;
                    }

                    replId = replInfo.GetValue(ReplIdKey) as string;
                    if (replId == null) {
                        return false;
                    }

                    languageServiceId = replInfo.GetValue(LanguageServiceGuidKey) as string;
                    if (languageServiceId == null) {
                        return false;
                    }
                }
            }

            Guid languageServiceGuid;
            if (!Guid.TryParse(languageServiceId, out languageServiceGuid)) {
                return false;
            }

            var contentTypes = model.GetService<IContentTypeRegistryService>();
            var contentType = contentTypes.GetContentType(contentTypeName);
            if (contentType == null) {
                return false;
            }

            string[] roles;
            var evaluator = GetInteractiveEvaluator(model, replId, out roles);
            if (evaluator == null) {
                return false;
            }

            CreateInteractiveWindow(evaluator, contentType, roles, id, title, languageServiceGuid, replId);
            return true;
        }

        public IEnumerable<IInteractiveWindow> GetReplWindows() {
            return _windows.Values.Select(x => x.Window.InteractiveWindow);
        }

        internal IEnumerable<ToolWindowPane> GetReplToolWindows() {
            return _windows.Values.Select(x => x.Window).OfType<ToolWindowPane>();
        }

        private static IInteractiveEvaluator GetInteractiveEvaluator(IComponentModel model, string replId, out string[] roles) {
            roles = new string[0];
            foreach (var provider in model.GetExtensions<IInteractiveEvaluatorProvider>()) {
                var evaluator = provider.GetEvaluator(replId);

                if (evaluator != null) {
                    roles = evaluator.GetType().GetCustomAttributes(typeof(InteractiveWindowRoleAttribute), true)
                        .OfType<InteractiveWindowRoleAttribute>()
                        .Select(r => r.Name)
                        .ToArray();
                    return evaluator;
                }
            }
            return null;
        }

        private InteractiveWindowInfo CreateInteractiveWindow(IInteractiveEvaluator/*!*/ evaluator, IContentType/*!*/ contentType, string[] roles, int id, string/*!*/ title, Guid languageServiceGuid, string replId) {
            return CreateInteractiveWindowInternal(evaluator, contentType, roles, id, title, languageServiceGuid, replId);
        }

        private InteractiveWindowInfo CreateInteractiveWindowInternal(IInteractiveEvaluator evaluator, IContentType contentType, string[] roles, int id, string title, Guid languageServiceGuid, string replId) {
            var service = (IVsUIShell)ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell));
            var model = (IComponentModel)ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel));

            SaveInteractiveInfo(id, evaluator, contentType, roles, title, languageServiceGuid, replId);

            // we don't pass __VSCREATETOOLWIN.CTW_fMultiInstance because multi instance panes are
            // destroyed when closed.  We are really multi instance but we don't want to be closed.  This
            // seems to work fine.
            __VSCREATETOOLWIN creationFlags = 0;
            if (!roles.Contains("DontPersist")) {
                creationFlags |= __VSCREATETOOLWIN.CTW_fForceCreate;
            }

            var replWindow = _windowFactory.Create(GuidList.guidPythonInteractiveWindowGuid, id, title, evaluator, creationFlags);
            replWindow.SetLanguage(GuidList.guidPythonLanguageServiceGuid, contentType);
            replWindow.InteractiveWindow.InitializeAsync();

            return _windows[id] = new InteractiveWindowInfo(replWindow, replId);
        }

    }
}
