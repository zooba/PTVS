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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor.OptionsExtensionMethods;

namespace Microsoft.PythonTools.Editor.Intellisense {
    class QuickInfoContent {
        private readonly object _content;
        private readonly ITrackingSpan _applicableToSpan;

        public object Content => _content;
        public ITrackingSpan ApplicableToSpan => _applicableToSpan;

        private QuickInfoContent(object content, ITrackingSpan applicableToSpan) {
            _content = content;
            _applicableToSpan = applicableToSpan;
        }

        public static async Task CreateAsync(ITrackingPoint trigger, CancellationToken cancellationToken) {
            var snapshot = trigger.TextBuffer.CurrentSnapshot;
            var span = await trigger.GetApplicableSpanAsync(snapshot, true, cancellationToken);

            if (span.Length == 0) {
                return;
            }

            var text = span.GetText();
            var applicableToSpan = snapshot.CreateTrackingSpan(
                span.Start.Position,
                span.Length,
                SpanTrackingMode.EdgeInclusive
            );

            trigger.TextBuffer.Properties[typeof(QuickInfoContent)] = new QuickInfoContent(text, applicableToSpan);
        }
    }

    class QuickInfoSource : IQuickInfoSource {
        private readonly ITextBuffer _textBuffer;

        public QuickInfoSource(ITextBuffer textBuffer) {
            _textBuffer = textBuffer;
        }

        public void Dispose() { }

        public void AugmentQuickInfoSession(
            IQuickInfoSession session,
            IList<object> quickInfoContent,
            out ITrackingSpan applicableToSpan
        ) {
            applicableToSpan = null;
            QuickInfoContent content;
            if (session.Properties.TryGetProperty(typeof(QuickInfoContent), out content)) {
                session.Properties.RemoveProperty(typeof(QuickInfoContent));
            }

            if (content == null) {
                if (!(session?.IsDismissed ?? true)) {
                    session.Dismiss();
                }
                return;
            }

            quickInfoContent.Add(content.Content);
            applicableToSpan = content.ApplicableToSpan;
        }
    }
}
