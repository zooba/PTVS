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
using Microsoft.PythonTools.Analysis.Parsing;

namespace Microsoft.PythonTools.Editor.Intellisense {
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
            var snapshot = _textBuffer.CurrentSnapshot;
            var span = session.GetTriggerPoint(_textBuffer).GetApplicableSpan(snapshot, true, CancellationToken.None);
            applicableToSpan = snapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeInclusive);

            quickInfoContent.Add("Loading...");

            BeginAugmentQuickInfoSession(session, quickInfoContent, span).DoNotWait();
        }

        private async Task BeginAugmentQuickInfoSession(
            IQuickInfoSession session,
            IList<object> quickInfoContent,
            SnapshotSpan span
        ) {
            if (span.Length == 0) {
                session.Dismiss();
                return;
            }

            var text = span.GetText();
            var analyzer = _textBuffer.GetAnalyzer();
            var context = _textBuffer.GetPythonFileContext();
            var document = _textBuffer.GetDocument();
            if (analyzer == null || context == null || document == null) {
                text = string.Format(Strings.QuickInfo_UnknownType, text);
            } else {
                var startLine = span.Start.GetContainingLine();
                var loc = new SourceLocation(
                    span.Start.Position,
                    startLine.LineNumber + 1,
                    (span.Start.Position - startLine.Start.Position) + 1
                );
                var state = analyzer.GetAnalysisState(context, document.Moniker, true);
                var types = await analyzer.GetVariableTypesAsync(state, text, loc, CancellationTokens.After500ms);
                text = string.Format(Strings.QuickInfo_TypeNoDocs,
                    text,
                    string.Join(", ", types.Select(t => t.ToAnnotation(state)))
                );
            }

            quickInfoContent.Clear();
            quickInfoContent.Add(text);
        }
    }
}
