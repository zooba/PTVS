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
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using IServiceProvider = System.IServiceProvider;

namespace Microsoft.PythonTools.Editor.Intellisense {
    abstract class EditorCommandHandler<T> : IVsTextViewCreationListener where T : class {
        [Import]
        internal IVsEditorAdaptersFactoryService _adapterService = null;
        [Import(typeof(SVsServiceProvider))]
        internal IServiceProvider _serviceProvider = null;

        void IVsTextViewCreationListener.VsTextViewCreated(IVsTextView textViewAdapter) {
            var textView = _adapterService.GetWpfTextView(textViewAdapter);
            if (textView == null) {
                return;
            }

            textView.Properties.GetOrCreateSingletonProperty(
                () => new CommandHandler(this, textView, textViewAdapter, CreateSource(textView, textViewAdapter))
            );
        }

        protected virtual T CreateSource(IWpfTextView textView, IVsTextView textViewAdapter) {
            return null;
        }

        protected virtual int QueryStatus(T source, Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText) {
            var flags = new OLECMDF[cCmds];
            var text = new NativeMethods.OLECMDTEXT(pCmdText);
            var name = text.IsName ? (text.Text ?? "") : null;
            var status = text.IsStatus ? (text.Text ?? "") : null;

            for (int i = 0; i < cCmds; ++i) {
                flags[i] = (OLECMDF)prgCmds[i].cmdf;
                bool supported = flags[i].HasFlag(OLECMDF.OLECMDF_SUPPORTED);
                bool visible = !flags[i].HasFlag(OLECMDF.OLECMDF_INVISIBLE);
                bool enable = flags[i].HasFlag(OLECMDF.OLECMDF_ENABLED);
                bool check = flags[i].HasFlag(OLECMDF.OLECMDF_LATCHED);
                QueryStatus(
                    source,
                    pguidCmdGroup,
                    prgCmds[i].cmdID,
                    ref name,
                    ref status,
                    ref supported,
                    ref visible,
                    ref enable,
                    ref check
                );
                if (!supported) {
                    return NativeMethods.OLECMDERR_E_NOTSUPPORTED;
                }

                flags[i] = (
                    (supported ? OLECMDF.OLECMDF_SUPPORTED : 0) |
                    (visible ? 0 : OLECMDF.OLECMDF_INVISIBLE) |
                    (enable ? OLECMDF.OLECMDF_ENABLED : 0) |
                    (check ? OLECMDF.OLECMDF_LATCHED : 0)
                );
            }

            for (int i = 0; i < cCmds; ++i) {
                prgCmds[i].cmdf = (uint)flags[i];
            }

            if (text.IsName) {
                text.Text = name;
            }
            if (text.IsStatus) {
                text.Text = status;
            }

            return VSConstants.S_OK;
        }

        protected virtual void QueryStatus(
            T source,
            Guid cmdGroup,
            uint cmdID,
            ref string name,
            ref string status,
            ref bool supported,
            ref bool visible,
            ref bool enable,
            ref bool check
        ) {
            supported = false;
        }

        protected virtual int Exec(
            T source,
            Guid pguidCmdGroup,
            uint nCmdID,
            uint nCmdexecopt,
            IntPtr pvaIn,
            IntPtr pvaOut,
            Func<int> forward
        ) {
            object argIn = pvaIn == IntPtr.Zero ? null : Marshal.GetObjectForNativeVariant(pvaIn);
            object argOut = null;

            var opt = (OLECMDEXECOPT)nCmdexecopt;
            int hr = Exec(
                source,
                pguidCmdGroup,
                nCmdID,
                argIn,
                ref argOut,
                (opt == OLECMDEXECOPT.OLECMDEXECOPT_DODEFAULT || opt == OLECMDEXECOPT.OLECMDEXECOPT_PROMPTUSER),
                (opt == OLECMDEXECOPT.OLECMDEXECOPT_SHOWHELP),
                forward
            );
            if (hr == NativeMethods.OLECMDERR_E_UNKNOWNGROUP || hr == NativeMethods.OLECMDERR_E_NOTSUPPORTED) {
                return forward();
            }

            if (argOut != null) {
                if (pvaOut == IntPtr.Zero) {
                    Debug.Fail("No output parameter accepted");
                } else {
                    Marshal.GetNativeVariantForObject(argOut, pvaOut);
                }
            }
            return hr;
        }

        protected virtual int Exec(
            T source,
            Guid cmdGroup,
            uint cmdId,
            object argIn,
            ref object argOut,
            bool allowUserInteraction,
            bool showHelpOnly,
            Func<int> forward
        ) {
            return NativeMethods.OLECMDERR_E_NOTSUPPORTED;
        }

        private class CommandHandler : IOleCommandTarget {
            private readonly EditorCommandHandler<T> _owner;
            private readonly IWpfTextView _textView;
            private readonly IVsTextView _textViewAdapter;
            private readonly IOleCommandTarget _nextCommandHandler;
            private T _state;

            public CommandHandler(
                EditorCommandHandler<T> owner,
                IWpfTextView textView,
                IVsTextView textViewAdapter,
                T state
            ) {
                _owner = owner;
                _textView = textView;
                _textViewAdapter = textViewAdapter;
                _state = state;

                ErrorHandler.ThrowOnFailure(_textViewAdapter.AddCommandFilter(this, out _nextCommandHandler));
            }

            public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
                var cmdGroup = pguidCmdGroup;
                return _owner.Exec(
                    _state,
                    pguidCmdGroup,
                    nCmdID,
                    nCmdexecopt,
                    pvaIn,
                    pvaOut,
                    () => {
                        var grp = cmdGroup;
                        return _nextCommandHandler.Exec(ref grp, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                    }
                );
            }

            public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText) {
                var hr = _owner.QueryStatus(_state, pguidCmdGroup, cCmds, prgCmds, pCmdText);
                if (!ErrorHandler.Succeeded(hr)) {
                    hr = _nextCommandHandler.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
                }
                return hr;
            }
        }
    }
}
