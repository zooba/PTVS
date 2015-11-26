/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using IServiceProvider = System.IServiceProvider;

namespace Microsoft.PythonTools.Editor.Intellisense {
    //[Export(typeof(ICompletionSourceProvider))]
    //[ContentType(ContentType.Name)]
    //[Name("CompletionProvider")]
    internal class CompletionSourceProvider : ICompletionSourceProvider {
        [Import]
        internal ITextStructureNavigatorSelectorService _navigatorService = null;
        [Import]
        internal PythonLanguageServiceProvider _langServiceProvider = null;
        [Import]
        internal PythonFileContextProvider _fileContextProvider = null;

        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer) {
            return new CompletionSource(this, textBuffer);
        }
    }

    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType(ContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class CompletionSourceHandlerProvider : IVsTextViewCreationListener {
        [Import]
        internal IVsEditorAdaptersFactoryService _adapterService = null;
        [Import]
        internal ICompletionBroker _broker = null;
        [Import(typeof(SVsServiceProvider))]
        internal IServiceProvider _serviceProvider = null;

        public void VsTextViewCreated(IVsTextView textViewAdapter) {
            var textView = _adapterService.GetWpfTextView(textViewAdapter);
            if (textView == null) {
                return;
            }

            textView.Properties.GetOrCreateSingletonProperty(() => new CommandHandler(textView, textViewAdapter, _broker));
        }

        private class CommandHandler : IOleCommandTarget {
            private readonly ICompletionBroker _broker;
            private readonly IWpfTextView _textView;
            private readonly IVsTextView _textViewAdapter;
            private readonly IOleCommandTarget _nextCommandHandler;

            private ICompletionSession _session;

            public CommandHandler(IWpfTextView textView, IVsTextView textViewAdapter, ICompletionBroker broker) {
                _textView = textView;
                _textViewAdapter = textViewAdapter;
                _broker = broker;

                ErrorHandler.ThrowOnFailure(_textViewAdapter.AddCommandFilter(this, out _nextCommandHandler));
            }

            private bool TriggerCompletion() {
                //the caret must be in a non-projection location 
                SnapshotPoint? caretPoint =
                _textView.Caret.Position.Point.GetPoint(
                textBuffer => (!textBuffer.ContentType.IsOfType("projection")), PositionAffinity.Predecessor);
                if (!caretPoint.HasValue) {
                    return false;
                }

                _session = _broker.CreateCompletionSession(_textView,
                    caretPoint.Value.Snapshot.CreateTrackingPoint(caretPoint.Value.Position, PointTrackingMode.Positive),
                    true);

                //subscribe to the Dismissed event on the session 
                _session.Dismissed += OnSessionDismissed;
                _session.Start();

                return true;
            }

            private void OnSessionDismissed(object sender, EventArgs e) {
                _session.Dismissed -= OnSessionDismissed;
                _session = null;
            }

            public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
                // TODO: Check in in automation function

                //make a copy of this so we can look at it after forwarding some commands 
                uint commandID = nCmdID;
                char typedChar = char.MinValue;
                //make sure the input is a char before getting it 
                if (pguidCmdGroup == VSConstants.VSStd2K && nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR) {
                    typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
                }

                //check for a commit character 
                if (nCmdID == (uint)VSConstants.VSStd2KCmdID.RETURN
                    || nCmdID == (uint)VSConstants.VSStd2KCmdID.TAB
                    || (char.IsWhiteSpace(typedChar) || char.IsPunctuation(typedChar))) {
                    //check for a a selection 
                    if (_session != null && !_session.IsDismissed) {
                        //if the selection is fully selected, commit the current session 
                        if (_session.SelectedCompletionSet.SelectionStatus.IsSelected) {
                            _session.Commit();
                            //also, don't add the character to the buffer 
                            return VSConstants.S_OK;
                        } else {
                            //if there is no selection, dismiss the session
                            _session.Dismiss();
                        }
                    }
                }

                //pass along the command so the char is added to the buffer 
                int retVal = _nextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                bool handled = false;
                if (!typedChar.Equals(char.MinValue) && char.IsLetterOrDigit(typedChar)) {
                    if (_session == null || _session.IsDismissed) // If there is no active session, bring up completion
                    {
                        TriggerCompletion();
                        _session?.Filter();
                    } else     //the completion session is already active, so just filter
                      {
                        _session.Filter();
                    }
                    handled = true;
                } else if (commandID == (uint)VSConstants.VSStd2KCmdID.BACKSPACE   //redo the filter if there is a deletion
                      || commandID == (uint)VSConstants.VSStd2KCmdID.DELETE) {
                    if (_session != null && !_session.IsDismissed)
                        _session.Filter();
                    handled = true;
                }
                if (handled) return VSConstants.S_OK;
                return retVal;
            }

            public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText) {
                return _nextCommandHandler.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
            }
        }
    }
}
