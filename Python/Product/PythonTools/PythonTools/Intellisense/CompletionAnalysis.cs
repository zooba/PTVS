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
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Repl;
using Microsoft.PythonTools.InteractiveWindow;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.PythonTools.Intellisense {
    /// <summary>
    /// Provides various completion services after the text around the current location has been
    /// processed. The completion services are specific to the current context
    /// </summary>
    public class CompletionAnalysis {
        private readonly ICompletionSession _session;
        private readonly ITextView _view;
        private readonly IServiceProvider _serviceProvider;
        private readonly ITrackingSpan _span;
        private readonly ITextBuffer _textBuffer;
        protected readonly CompletionOptions _options;
        internal const Int64 TooMuchTime = 50;
        protected static Stopwatch _stopwatch = MakeStopWatch();

        internal static CompletionAnalysis EmptyCompletionContext = new CompletionAnalysis(null, null, null, null, null, null);

        internal CompletionAnalysis(IServiceProvider serviceProvider, ICompletionSession session, ITextView view, ITrackingSpan span, ITextBuffer textBuffer, CompletionOptions options) {
            _session = session;
            _view = view;
            _span = span;
            _serviceProvider = serviceProvider;
            _textBuffer = textBuffer;
            _options = (options == null) ? new CompletionOptions() : options.Clone();
        }

        public ICompletionSession Session => _session;
        public ITextBuffer TextBuffer => _textBuffer;
        public ITrackingSpan Span => _span;
        public ITextView View => _view;

        public virtual CompletionSet GetCompletions(IGlyphService glyphService) {
            return null;
        }

        internal static bool IsKeyword(ClassificationSpan token, string keyword) {
            return token.ClassificationType.Classification == PredefinedClassificationTypeNames.Keyword && token.Span.GetText() == keyword;
        }

        internal static DynamicallyVisibleCompletion PythonCompletion(IGlyphService service, CompletionResult memberResult) {
            return new DynamicallyVisibleCompletion(memberResult.Name, 
                memberResult.Completion, 
                () => memberResult.Documentation, 
                () => service.GetGlyph(memberResult.MemberType.ToGlyphGroup(), StandardGlyphItem.GlyphItemPublic),
                Enum.GetName(typeof(PythonMemberType), memberResult.MemberType)
            );
        }

        internal static DynamicallyVisibleCompletion PythonCompletion(IGlyphService service, string name, string tooltip, StandardGlyphGroup group) {
            var icon = new IconDescription(group, StandardGlyphItem.GlyphItemPublic);

            var result = new DynamicallyVisibleCompletion(name, 
                name, 
                tooltip, 
                service.GetGlyph(group, StandardGlyphItem.GlyphItemPublic),
                Enum.GetName(typeof(StandardGlyphGroup), group));
            result.Properties.AddProperty(typeof(IconDescription), icon);
            return result;
        }

        internal static DynamicallyVisibleCompletion PythonCompletion(IGlyphService service, string name, string completion, string tooltip, StandardGlyphGroup group) {
            var icon = new IconDescription(group, StandardGlyphItem.GlyphItemPublic);

            var result = new DynamicallyVisibleCompletion(name, 
                completion, 
                tooltip, 
                service.GetGlyph(group, StandardGlyphItem.GlyphItemPublic),
                Enum.GetName(typeof(StandardGlyphGroup), group));
            result.Properties.AddProperty(typeof(IconDescription), icon);
            return result;
        }

        internal AnalysisEntry GetAnalysisEntry() {
            AnalysisEntry entry;
            if (_view.TryGetAnalysisEntry(TextBuffer, _serviceProvider, out entry) && entry != null) {
                //Debug.Assert(
                //    entry.Analysis != null,
                //    string.Format("Failed to get analysis for buffer {0} with file {1}", TextBuffer, entry.FilePath)
                //);
                return entry;
            }
            Debug.Fail("Failed to get project entry for buffer " + TextBuffer.ToString());
            return null;
        }

        private static Stopwatch MakeStopWatch() {
            var res = new Stopwatch();
            res.Start();
            return res;
        }

        protected IEnumerable<CompletionResult> GetModules(string[] package, bool modulesOnly = true) {
            var analysis = GetAnalysisEntry();
            if (analysis == null) {
                return Enumerable.Empty<CompletionResult>();
            }

            IPythonInteractiveIntellisense pyReplEval = null;
            IInteractiveEvaluator eval;
            if (TextBuffer.Properties.TryGetProperty(typeof(IInteractiveEvaluator), out eval)) {
                pyReplEval = eval as IPythonInteractiveIntellisense;
            }
            IEnumerable<KeyValuePair<string, string>> replScopes = null;
            if (pyReplEval != null) {
                replScopes = pyReplEval.GetAvailableScopesAndPaths();
            }

            if (package == null) {
                package = new string[0];
            }

            var modules = Enumerable.Empty<CompletionResult>();
            if (analysis != null && (pyReplEval == null || !pyReplEval.LiveCompletionsOnly)) {
                modules = modules.Concat(package.Length > 0 ? 
                    analysis.Analyzer.GetModuleMembersAsync(analysis, package, !modulesOnly).WaitOrDefault(1000) ?? modules:
                    (analysis.Analyzer.GetModulesResult(true).WaitOrDefault(1000) ?? modules).Distinct(CompletionComparer.MemberEquality)
                );
            }
            if (replScopes != null) {
                modules = GetModulesFromReplScope(replScopes, package)
                    .Concat(modules)
                    .Distinct(CompletionComparer.MemberEquality);
            }

            return modules;
        }

        private static IEnumerable<CompletionResult> GetModulesFromReplScope(
            IEnumerable<KeyValuePair<string, string>> scopes,
            string[] package
        ) {
            if (package == null || package.Length == 0) {
                foreach (var scope in scopes) {
                    if (scope.Key.IndexOf('.') < 0) {
                        yield return new CompletionResult(
                            scope.Key,
                            string.IsNullOrEmpty(scope.Value) ? PythonMemberType.Namespace : PythonMemberType.Module
                        );
                    }
                }
            } else {
                foreach (var scope in scopes) {
                    var parts = scope.Key.Split('.');
                    if (parts.Length - 1 == package.Length &&
                        parts.Take(parts.Length - 1).SequenceEqual(package, StringComparer.Ordinal)) {
                        yield return new CompletionResult(
                            parts[parts.Length - 1],
                            string.IsNullOrEmpty(scope.Value) ? PythonMemberType.Namespace : PythonMemberType.Module
                        );
                    }
                }
            }
        }

        public override string ToString() {
            if (Span == null) {
                return "CompletionContext.EmptyCompletionContext";
            };
            var snapSpan = Span.GetSpan(TextBuffer.CurrentSnapshot);
            return String.Format("CompletionContext({0}): {1} @{2}", GetType().Name, snapSpan.GetText(), snapSpan.Span);
        }
    }
}
