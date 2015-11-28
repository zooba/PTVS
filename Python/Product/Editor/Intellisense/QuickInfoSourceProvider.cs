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
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
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
    [Export(typeof(IQuickInfoSourceProvider))]
    [ContentType(ContentType.Name)]
    [Name("Python Quick Info Source Provider")]
    internal class QuickInfoSourceProvider : IQuickInfoSourceProvider {
        public IQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer) {
            return new QuickInfoSource(textBuffer);
        }
    }

    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType(ContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Analyzable)]
    internal class QuickInfoSourceHandlerProvider : EditorCommandHandler<QuickInfoSourceHandlerProvider.State> {
        [Import]
        internal IQuickInfoBroker _broker = null;

        public class State {
            private readonly ITextView _textView;
            private readonly IQuickInfoBroker _broker;

            private IQuickInfoSession _session;

            public State(ITextView textView, IQuickInfoBroker broker) {
                _textView = textView;
                _broker = broker;
            }

            private async Task<bool> TriggerSessionAsync() {
                //the caret must be in a non-projection location 
                SnapshotPoint? caretPoint = _textView.Caret.Position.Point.GetInsertionPoint(
                    textBuffer => (!textBuffer.ContentType.IsOfType("projection"))
                );
                if (!caretPoint.HasValue) {
                    return false;
                }
                var snapshot = caretPoint.Value.Snapshot;
                var trigger = snapshot.CreateTrackingPoint(caretPoint.Value.Position, PointTrackingMode.Positive);
                try {
                    await QuickInfoContent.CreateAsync(trigger, CancellationTokens.After500ms);
                } catch (OperationCanceledException) {
                    return false;
                }

                _session = _broker.CreateQuickInfoSession(_textView, trigger, true);

                if (_session == null) {
                    return false;
                }

                //subscribe to the Dismissed event on the session 
                _session.Dismissed += OnSessionDismissed;
                _session.Start();

                return true;
            }

            public async Task<bool> TriggerOrRecalculate() {
                bool triggered = true;
                if (!(_session?.IsDismissed ?? true)) {
                    _session.Dismiss();
                    triggered = false;
                }
                await TriggerSessionAsync();
                return triggered;
            }

            public void Dismiss() {
                _session?.Dismiss();
            }

            private void OnSessionDismissed(object sender, EventArgs e) {
                _session.Dismissed -= OnSessionDismissed;
                _session = null;
            }
        }

        protected override State CreateSource(IWpfTextView textView, IVsTextView textViewAdapter) {
            return new State(textView, _broker);
        }

        protected override int Exec(
            State source,
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

            if (cmdGroup != VSConstants.VSStd2K || cmdId != (uint)VSConstants.VSStd2KCmdID.QUICKINFO) {
                return forward();
            }

            source.TriggerOrRecalculate().DoNotWait();

            return forward();
        }
    }

}
