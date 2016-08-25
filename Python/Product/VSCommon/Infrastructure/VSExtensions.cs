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
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.PythonTools.Infrastructure {
    public static class VSExtensions {
        internal static EnvDTE.DTE GetDTE(this IServiceProvider provider) {
            return (EnvDTE.DTE)provider.GetService(typeof(EnvDTE.DTE));
        }

        internal static IComponentModel GetComponentModel(this IServiceProvider serviceProvider) {
            if (serviceProvider == null) {
                return null;
            }
            return (IComponentModel)serviceProvider.GetService(typeof(SComponentModel));
        }

        internal static IVsShell GetShell(this IServiceProvider provider) {
            return (IVsShell)provider.GetService(typeof(SVsShell));
        }

        internal static bool TryGetShellProperty<T>(this IServiceProvider provider, __VSSPROPID propId, out T value) {
            object obj;
            if (ErrorHandler.Failed(provider.GetShell().GetProperty((int)propId, out obj))) {
                value = default(T);
                return false;
            }
            try {
                value = (T)obj;
                return true;
            } catch (InvalidCastException) {
                Debug.Fail("Expected property of type {0} but got value of type {1}".FormatUI(typeof(T).FullName, obj.GetType().FullName));
                value = default(T);
                return false;
            }
        }

        internal static bool IsShellInitialized(this IServiceProvider provider) {
            bool isInitialized;
            return provider.TryGetShellProperty((__VSSPROPID)__VSSPROPID4.VSSPROPID_ShellInitialized, out isInitialized) &&
                isInitialized;
        }

        class ShellInitializedNotification : IVsShellPropertyEvents {
            private readonly IVsShell _shell;
            private readonly uint _cookie;
            private readonly TaskCompletionSource<object> _tcs;

            public ShellInitializedNotification(IVsShell shell) {
                _shell = shell;
                _tcs = new TaskCompletionSource<object>();
                ErrorHandler.ThrowOnFailure(_shell.AdviseShellPropertyChanges(this, out _cookie));

                // Check again in case we raised with initialization
                object value;
                if (ErrorHandler.Succeeded(_shell.GetProperty((int)__VSSPROPID4.VSSPROPID_ShellInitialized, out value)) &&
                    CheckProperty((int)__VSSPROPID4.VSSPROPID_ShellInitialized, value)) {
                    return;
                }

                if (ErrorHandler.Succeeded(_shell.GetProperty((int)__VSSPROPID6.VSSPROPID_ShutdownStarted, out value)) &&
                    CheckProperty((int)__VSSPROPID6.VSSPROPID_ShutdownStarted, value)) {
                    return;
                }
            }

            private bool CheckProperty(int propid, object var) {
                if (propid == (int)__VSSPROPID4.VSSPROPID_ShellInitialized && var is bool && (bool)var) {
                    _shell.UnadviseShellPropertyChanges(_cookie);
                    _tcs.TrySetResult(null);
                    return true;
                } else if (propid == (int)__VSSPROPID6.VSSPROPID_ShutdownStarted && var is bool && (bool)var) {
                    _shell.UnadviseShellPropertyChanges(_cookie);
                    _tcs.TrySetCanceled();
                    return true;
                }
                return false;
            }

            public Task Task => _tcs.Task;

            int IVsShellPropertyEvents.OnShellPropertyChange(int propid, object var) {
                CheckProperty(propid, var);
                return VSConstants.S_OK;
            }
        }

        internal static Task WaitForShellInitializedAsync(this IServiceProvider provider) {
            if (provider.IsShellInitialized()) {
                return Task.FromResult<object>(null);
            }
            return new ShellInitializedNotification(provider.GetShell()).Task;
        }

        [Conditional("DEBUG")]
        internal static void AssertShellIsInitialized(this IServiceProvider provider) {
            Debug.Assert(provider.IsShellInitialized(), "Shell is not yet initialized");
        }
    }
}
