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
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Common.Infrastructure;
using Microsoft.PythonTools.Infrastructure;
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
    [Export(typeof(ICompletionSourceProvider))]
    [ContentType(ContentType.Name)]
    [Name("CompletionProvider")]
    internal class CompletionSourceProvider : ICompletionSourceProvider {
        [Import]
        internal ITextStructureNavigatorSelectorService _navigatorService = null;
        [Import]
        internal PythonLanguageServiceProvider _langServiceProvider = null;
        [Import]
        internal PythonFileContextProvider _fileContextProvider = null;

        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer) {
            return new CompletionSource(textBuffer);
        }
    }

    internal class CompletionSourceHandler {
        private readonly ITextView _textView;
        private readonly ICompletionBroker _broker;

        private ICompletionSession _session;

        public CompletionSourceHandler(ITextView textView, ICompletionBroker broker) {
            _textView = textView;
            _broker = broker;
        }

        private async Task<bool> TriggerCompletionAsync() {
            //the caret must be in a non-projection location 
            SnapshotPoint? caretPoint = _textView.Caret.Position.Point.GetPoint(
                textBuffer => (!textBuffer.ContentType.IsOfType("projection")),
                PositionAffinity.Predecessor
            );
            if (!caretPoint.HasValue) {
                return false;
            }
            var snapshot = caretPoint.Value.Snapshot;
            var trigger = snapshot.CreateTrackingPoint(caretPoint.Value.Position, PointTrackingMode.Positive);
            try {
                if (!await CompletionContent.CreateAsync(trigger, CancellationTokens.After500ms)) {
                    return false;
                }
            } catch (OperationCanceledException) {
                return false;
            }

            _session = _broker.CreateCompletionSession(_textView, trigger, true);

            if (_session == null) {
                return false;
            }

            //subscribe to the Dismissed event on the session 
            _session.Dismissed += OnSessionDismissed;
            _session.Start();

            return true;
        }

        public bool ShouldTrigger(char ch) {
            return char.IsLetter(ch);
        }

        public bool ShouldCommit(char ch) {
            return char.IsWhiteSpace(ch) || char.IsPunctuation(ch);
        }

        public async Task<bool> TriggerOrCompleteAsync() {
            await TriggerOrFilterAsync();
            if (_session != null &&
                _session.CompletionSets.Sum(cs => cs.Completions?.Count ?? 0) == 1 &&
                (_session.SelectedCompletionSet?.SelectionStatus?.IsSelected ?? false)
            ) {
                _session.Commit();
                return true;
            }
            return false;
        }

        public async Task<bool> TriggerOrFilterAsync() {
            bool triggered = false;
            if (_session?.IsDismissed ?? true) {
                await TriggerCompletionAsync();
                triggered = true;
            }
            if (_session?.IsStarted ?? false) {
                _session.Filter();
            }
            return triggered;
        }

        public void Filter() {
            if (_session?.IsStarted ?? false) {
                _session.Filter();
            }
        }

        public bool CommitOrDismiss(out bool wasFullyTyped) {
            wasFullyTyped = false;
            if (_session?.IsDismissed ?? true) {
                return false;
            }

            //if the selection is fully selected, commit the current session 
            if (_session.SelectedCompletionSet?.SelectionStatus.IsSelected ?? false) {
                var text = _session.SelectedCompletionSet.SelectionStatus.Completion.InsertionText;
                try {
                    var caret = _session.TextView.Caret.Position.BufferPosition;
                    var typed = caret.Snapshot.GetText(caret.Position - text.Length, text.Length);
                    // Need a perfect match to deem this fully typed
                    wasFullyTyped = typed.Equals(text, StringComparison.Ordinal);
                } catch {
                    wasFullyTyped = false;
                }
                _session.Commit();
                //also, don't add the character to the buffer 
                return true;
            }

            //if there is no selection, dismiss the session
            _session.Dismiss();
            return false;
        }

        private void OnSessionDismissed(object sender, EventArgs e) {
            _session.Dismissed -= OnSessionDismissed;
            _session = null;
        }
    }


    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType(ContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class CompletionSourceHandlerProvider : EditorCommandHandler<CompletionSourceHandler> {
        [Import]
        internal ICompletionBroker _broker = null;

        protected override CompletionSourceHandler CreateSource(IWpfTextView textView, IVsTextView textViewAdapter) {
            return new CompletionSourceHandler(textView, _broker);
        }

        protected override void QueryStatus(
            CompletionSourceHandler source,
            Guid cmdGroup,
            uint cmdID,
            ref string name,
            ref string status,
            ref bool supported,
            ref bool visible,
            ref bool enable,
            ref bool check
        ) {
            if (cmdGroup == VSConstants.VSStd2K) {
                switch ((VSConstants.VSStd2KCmdID)cmdID) {
                    case VSConstants.VSStd2KCmdID.SHOWMEMBERLIST:
                    case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                        supported = true;
                        enable = true;
                        visible = true;
                        break;
                }
            }
        }

        protected override int Exec(
            CompletionSourceHandler source,
            Guid cmdGroup,
            uint cmdId,
            object argIn,
            ref object argOut,
            bool allowUserInteraction,
            bool showHelpOnly,
            Func<int> forward
        ) {
            if (!allowUserInteraction) {
                return forward();
            }

            //make a copy of this so we can look at it after forwarding some commands 
            uint commandID = cmdId;
            char typedChar = char.MinValue;
            //make sure the input is a char before getting it 
            if (cmdGroup == VSConstants.VSStd2K && cmdId == (uint)VSConstants.VSStd2KCmdID.TYPECHAR) {
                typedChar = (char)(ushort)argIn;
            }

            //check for a commit character 
            if (cmdGroup == VSConstants.VSStd2K) {
                if (cmdId == (uint)VSConstants.VSStd2KCmdID.COMPLETEWORD) {
                    source.TriggerOrCompleteAsync().DoNotWait();
                    return VSConstants.S_OK;
                }

                if (cmdId == (uint)VSConstants.VSStd2KCmdID.SHOWMEMBERLIST) {
                    source.TriggerOrFilterAsync().DoNotWait();
                    return VSConstants.S_OK;
                }

                if (cmdId == (uint)VSConstants.VSStd2KCmdID.RETURN ||
                    cmdId == (uint)VSConstants.VSStd2KCmdID.TAB ||
                    cmdId == (uint)VSConstants.VSStd2KCmdID.TYPECHAR && source.ShouldCommit(typedChar)
                ) {
                    bool wasFullyTyped;
                    if (source.CommitOrDismiss(out wasFullyTyped)) {
                        // committed, so don't insert the tab character
                        if (cmdId == (uint)VSConstants.VSStd2KCmdID.TAB) {
                            return VSConstants.S_OK;
                        }
                        // TODO: check whether the user wants to insert enter
                        if (cmdId == (uint)VSConstants.VSStd2KCmdID.RETURN && wasFullyTyped) {
                            return VSConstants.S_OK;
                        }
                    }
                }

                if (cmdId == (uint)VSConstants.VSStd2KCmdID.TYPECHAR) {
                    //pass along the command so the char is added to the buffer 
                    int hr = forward();
                    if (ErrorHandler.Succeeded(hr) && source.ShouldTrigger(typedChar)) {
                        source.TriggerOrFilterAsync().DoNotWait();
                    }
                    return hr;
                } else if (cmdId == (uint)VSConstants.VSStd2KCmdID.BACKSPACE ||
                    cmdId == (uint)VSConstants.VSStd2KCmdID.DELETE ||
                    cmdId == (uint)VSConstants.VSStd2KCmdID.UNDO ||
                    cmdId == (uint)VSConstants.VSStd2KCmdID.UNDONOMOVE ||
                    cmdId == (uint)VSConstants.VSStd2KCmdID.REDO ||
                    cmdId == (uint)VSConstants.VSStd2KCmdID.REDONOMOVE ||
                    cmdId == (uint)VSConstants.VSStd2KCmdID.CUT ||
                    cmdId == (uint)VSConstants.VSStd2KCmdID.PASTE ||
                    cmdId == (uint)VSConstants.VSStd2KCmdID.DELETEWORDLEFT ||
                    cmdId == (uint)VSConstants.VSStd2KCmdID.DELETEWORDRIGHT) {
                    //pass along the command so the edit occurs
                    int hr = forward();
                    if (ErrorHandler.Succeeded(hr)) {
                        source.Filter();
                    }
                    return hr;
                }
            }

            return forward();
        }
    }
}
