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
using Microsoft.PythonTools.Common.Infrastructure;
using Microsoft.VisualStudio.Imaging;

namespace Microsoft.PythonTools.Editor.Intellisense {
    class CompletionContent {
        private readonly CompletionSet _set;

        public CompletionSet Set => _set;

        private CompletionContent(CompletionSet set) {
            _set = set;
        }

        public static async Task<bool> CreateAsync(ITrackingPoint trigger, CancellationToken cancellationToken) {
            var textBuffer = trigger.TextBuffer;
            var snapshot = textBuffer.CurrentSnapshot;
            var span = await trigger.GetApplicableSpanAsync(snapshot, true, cancellationToken);

            var applicableToSpan = snapshot.CreateTrackingSpan(
                span.Start.Position,
                span.Length,
                SpanTrackingMode.EdgeInclusive
            );

            var analyzer = textBuffer.GetAnalyzer();
            if (analyzer == null) {
                return false;
            }

            IReadOnlyDictionary<string, string> names = null;
            try {
                names = await analyzer.GetImportableModulesAsync("", "", CancellationTokens.After500ms);
            } catch (OperationCanceledException) {
                return false;
            }
            if (names == null) {
                return false;
            }
            var completions = names.Select(n => new DynamicallyVisibleCompletion(
                n.Key, n.Key, n.Key, KnownMonikers.Module, "Module"
            ));

            var set = new FuzzyCompletionSet(
                "PythonImports",
                "Imports",
                applicableToSpan,
                completions,
                new CompletionOptions(),
                Comparer<Completion>.Create((x, y) => x.DisplayText.CompareTo(y.DisplayText))
            );

            trigger.TextBuffer.Properties[typeof(CompletionContent)] = new CompletionContent(set);
            return true;
        }
    }

    class CompletionSource : ICompletionSource {
        private readonly ITextBuffer _textBuffer;

        public CompletionSource(ITextBuffer textBuffer) {
            _textBuffer = textBuffer;
        }

        public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets) {
            CompletionContent content;
            if (_textBuffer.Properties.TryGetProperty(typeof(CompletionContent), out content)) {
                _textBuffer.Properties.RemoveProperty(typeof(CompletionContent));
            }
            if (content == null) {
                return;
            }

            completionSets.Add(content.Set);
        }

        public void Dispose() { }
    }
}
