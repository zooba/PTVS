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
    class CompletionSource : ICompletionSource {
        private readonly ITextBuffer _textBuffer;
        private readonly CompletionSourceProvider _provider;

        public CompletionSource(CompletionSourceProvider provider, ITextBuffer textBuffer) {
            _textBuffer = textBuffer;
            _provider = provider;
        }


        private static SnapshotSpan GetApplicableSpan(IIntellisenseSession session, ITextSnapshot snapshot) {
            var triggerPoint = session.GetTriggerPoint(snapshot.TextBuffer);

            var point = triggerPoint.GetPoint(snapshot);
            var lineStart = point.GetContainingLine().Start;

            var text = snapshot.GetText(lineStart, point - lineStart);
            var pos = text.LastIndexOf(' ') + 1;

            return new SnapshotSpan(lineStart + pos, point);
        }

        public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets) {
            var textBuffer = _textBuffer;
            PythonLanguageService langService;
            if (!textBuffer.Properties.TryGetProperty(typeof(PythonLanguageService), out langService)) {
                return;
            }

            var snapshot = textBuffer.CurrentSnapshot;
            var span = GetApplicableSpan(session, snapshot);
            
            var names = langService.GetImportableModulesAsync("", "", CancellationToken.None).GetAwaiter().GetResult();
            var completions = names.Select(n => new DynamicallyVisibleCompletion(n.Key));

            completionSets.Add(new FuzzyCompletionSet(
                "PythonImports",
                "Imports",
                snapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeInclusive),
                completions,
                new CompletionOptions(),
                Comparer<Completion>.Create((x, y) => x.DisplayText.CompareTo(y.DisplayText))
            ));
        }

        public void Dispose() {
        }
    }
}
