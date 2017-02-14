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
using System.Diagnostics;
using System.Linq;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.InteractiveWindow;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Repl;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.PythonTools.Intellisense {
    internal class NormalCompletionAnalysis : CompletionAnalysis {
        private readonly ITextSnapshot _snapshot;
        private readonly VsProjectAnalyzer _analyzer;
        private readonly IServiceProvider _serviceProvider;

        internal NormalCompletionAnalysis(VsProjectAnalyzer analyzer, ICompletionSession session, ITextView view, ITextSnapshot snapshot, ITrackingSpan span, ITextBuffer textBuffer, CompletionOptions options, IServiceProvider serviceProvider)
            : base(analyzer._serviceProvider, session, view, span, textBuffer, options) {
            _snapshot = snapshot;
            _analyzer = analyzer;
            _serviceProvider = serviceProvider;
        }

        internal bool GetPrecedingExpression(out string text, out SnapshotSpan expressionExtent) {
            text = string.Empty;
            expressionExtent = default(SnapshotSpan);

            var startSpan = _snapshot.CreateTrackingSpan(Span.GetSpan(_snapshot).Start.Position, 0, SpanTrackingMode.EdgeInclusive);
            var parser = new ReverseExpressionParser(_snapshot, _snapshot.TextBuffer, startSpan);
            using (var e = parser.GetEnumerator()) {
                if (e.MoveNext() &&
                    e.Current != null &&
                    e.Current.ClassificationType.IsOfType(PredefinedClassificationTypeNames.Number)) {
                    return false;
                }
            }

            var sourceSpan = parser.GetExpressionRange();
            if (sourceSpan.HasValue && sourceSpan.Value.Length > 0) {
                text = sourceSpan.Value.GetText();
                if (text.EndsWith(".")) {
                    text = text.Substring(0, text.Length - 1);
                    if (text.Length == 0) {
                        // don't return all available members on empty dot.
                        return false;
                    }
                } else {
                    int cut = text.LastIndexOfAny(new[] { '.', ']', ')' });
                    if (cut != -1) {
                        text = text.Substring(0, cut);
                    } else {
                        text = String.Empty;
                    }
                }
            }


            expressionExtent = sourceSpan ?? new SnapshotSpan(Span.GetStartPoint(_snapshot), 0);

            return true;
        }

        public override CompletionSet GetCompletions(IGlyphService glyphService) {
            var start1 = _stopwatch.ElapsedMilliseconds;

            IEnumerable<CompletionResult> members = null;
            IEnumerable<CompletionResult> replMembers = null;

            var interactiveWindow = _snapshot.TextBuffer.GetInteractiveWindow();
            var pyReplEval = interactiveWindow?.Evaluator as IPythonInteractiveIntellisense;
            
            var analysis = GetAnalysisEntry();

            string text;
            SnapshotSpan statementRange;
            if (!GetPrecedingExpression(out text, out statementRange)) {
                return null;
            } else if (string.IsNullOrEmpty(text)) {
                if (analysis != null) {
                    lock (_analyzer) {
                        var location = VsProjectAnalyzer.TranslateIndex(
                            statementRange.Start.Position,
                            statementRange.Snapshot,
                            analysis
                        );
                        var parameters = Enumerable.Empty<CompletionResult>();
                        var sigs = VsProjectAnalyzer.GetSignaturesAsync(_serviceProvider, View, _snapshot, Span).WaitOrDefault(1000);
                        if (sigs != null && sigs.Signatures.Any()) {
                            parameters = sigs.Signatures
                                .SelectMany(s => s.Parameters)
                                .Select(p => p.Name)
                                .Distinct()
                                .Select(n => new CompletionResult(n, PythonMemberType.Field));
                        }
                        members = (analysis.Analyzer.GetAllAvailableMembersAsync(analysis, location, _options.MemberOptions).WaitOrDefault(1000) ?? new CompletionResult[0])
                            .Union(parameters, CompletionComparer.MemberEquality);
                    }
                }

                if (pyReplEval == null) {
                    var expansions = _serviceProvider.GetPythonToolsService().GetExpansionCompletions(100);
                    if (expansions != null) {
                        // Expansions should come first, so that they replace our keyword
                        // completions with the more detailed snippets.
                        if (members != null) {
                            members = expansions.Union(members, CompletionComparer.MemberEquality);
                        } else {
                            members = expansions;
                        }
                    }
                }

                if (pyReplEval != null) {
                    replMembers = pyReplEval.GetMemberNames(string.Empty);
                }
            } else {
                if (analysis != null && (pyReplEval == null || !pyReplEval.LiveCompletionsOnly)) {
                    lock (_analyzer) {
                        var location = VsProjectAnalyzer.TranslateIndex(
                            statementRange.Start.Position,
                            statementRange.Snapshot,
                            analysis
                        );

                        members = analysis.Analyzer.GetMembersAsync(analysis, text, location, _options.MemberOptions).WaitOrDefault(1000);
                    }
                }

                if (pyReplEval != null && _analyzer.ShouldEvaluateForCompletion(text)) {
                    Debug.Assert(pyReplEval.Analyzer == _analyzer);
                    replMembers = pyReplEval.GetMemberNames(text);
                }
            }

            if (replMembers != null) {
                if (members != null) {
                    members = members.Union(replMembers, CompletionComparer.MemberEquality);
                } else {
                    members = replMembers;
                }
            }

            var end = _stopwatch.ElapsedMilliseconds;

            if (/*Logging &&*/ (end - start1) > TooMuchTime) {
                if (members != null) {
                    var memberArray = members.ToArray();
                    members = memberArray;
                    Trace.WriteLine(String.Format("{0} lookup time {1} for {2} members", this, end - start1, members.Count()));
                } else {
                    Trace.WriteLine(String.Format("{0} lookup time {1} for zero members", this, end - start1));
                }
            }

            if (members == null) {
                // The expression is invalid so we shouldn't provide
                // a completion set at all.
                return null;
            }

            var start = _stopwatch.ElapsedMilliseconds;

            var result = new FuzzyCompletionSet(
                "Python",
                "Python",
                Span,
                members.Select(m => PythonCompletion(glyphService, m)),
                _options,
                CompletionComparer.UnderscoresLast,
                matchInsertionText: true
            );

            end = _stopwatch.ElapsedMilliseconds;

            if (/*Logging &&*/ (end - start1) > TooMuchTime) {
                Trace.WriteLine(String.Format("{0} completion set time {1} total time {2}", this, end - start, end - start1));
            }

            return result;
        }

    }
}
