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
using System.Text;
using Microsoft.PythonTools.Django.Analysis;
using Microsoft.PythonTools.Django.TemplateParsing;
using Microsoft.PythonTools.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace Microsoft.PythonTools.Django.Intellisense {
    internal abstract class DjangoCompletionSourceBase : ICompletionSource {
        protected readonly IGlyphService _glyphService;
        protected readonly VsProjectAnalyzer _analyzer;
        protected readonly ITextBuffer _buffer;

        protected DjangoCompletionSourceBase(IGlyphService glyphService, VsProjectAnalyzer analyzer, ITextBuffer textBuffer) {
            _glyphService = glyphService;
            _analyzer = analyzer;
            _buffer = textBuffer;
        }

        #region ICompletionSource Members

        public abstract void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets);

        /// <param name="kind">The type of template tag we are processing</param>
        /// <param name="templateText">The text of the template tag which we are offering a completion in</param>
        /// <param name="templateStart">The offset in the buffer where the template starts</param>
        /// <param name="triggerPoint">The point in the buffer where the completion was triggered</param>
        internal CompletionSet GetCompletionSet(CompletionOptions options, VsProjectAnalyzer analyzer, TemplateTokenKind kind, string templateText, int templateStart, SnapshotPoint triggerPoint, out ITrackingSpan applicableSpan) {
            int position = triggerPoint.Position - templateStart;
            IEnumerable<CompletionInfo> tags;
            IDjangoCompletionContext context;

            applicableSpan = GetWordSpan(templateText, templateStart, triggerPoint);

            switch (kind) {
                case TemplateTokenKind.Block:
                    var block = DjangoBlock.Parse(templateText);
                    if (block != null) {
                        if (position <= block.ParseInfo.Start + block.ParseInfo.Command.Length) {
                            // we are completing before the command
                            // TODO: Return a new set of tags?  Do nothing?  Do this based upon ctrl-space?
                            tags = FilterBlocks(CompletionInfo.ToCompletionInfo(analyzer.GetTags(), StandardGlyphGroup.GlyphKeyword), triggerPoint);
                        } else {
                            // we are in the arguments, let the block handle the completions
                            context = new ProjectBlockCompletionContext(analyzer, _buffer);
                            tags = block.GetCompletions(context, position);
                        }
                    } else {
                        // no tag entered yet, provide the known list of tags.
                        tags = FilterBlocks(CompletionInfo.ToCompletionInfo(analyzer.GetTags(), StandardGlyphGroup.GlyphKeyword), triggerPoint);
                    }
                    break;
                case TemplateTokenKind.Variable:
                    var variable = DjangoVariable.Parse(templateText);
                    context = new ProjectBlockCompletionContext(analyzer, _buffer);
                    if (variable != null) {
                        tags = variable.GetCompletions(context, position);
                    } else {
                        // show variable names
                        tags = CompletionInfo.ToCompletionInfo(context.Variables, StandardGlyphGroup.GlyphGroupVariable);
                    }

                    break;
                default:
                    throw new InvalidOperationException();
            }

            var completions = tags
                .OrderBy(tag => tag.DisplayText, StringComparer.OrdinalIgnoreCase)
                .Select(tag => new DynamicallyVisibleCompletion(
                    tag.DisplayText,
                    tag.InsertionText,
                    StripDocumentation(tag.Documentation),
                    _glyphService.GetGlyph(tag.Glyph, StandardGlyphItem.GlyphItemPublic),
                    "tag"));
            return new FuzzyCompletionSet(
                "PythonDjangoTags",
                Resources.DjangoTagsCompletionSetDisplayName,
                applicableSpan,
                completions,
                options,
                CompletionComparer.UnderscoresLast);
        }

        private ITrackingSpan GetWordSpan(string templateText, int templateStart, SnapshotPoint triggerPoint) {
            ITrackingSpan applicableSpan;
            int spanStart = triggerPoint.Position;
            for (int i = triggerPoint.Position - templateStart - 1; i >= 0 && i < templateText.Length; --i, --spanStart) {
                char c = templateText[i];
                if (!char.IsLetterOrDigit(c) && c != '_') {
                    break;
                }
            }
            int length = triggerPoint.Position - spanStart;
            for (int i = triggerPoint.Position; i < triggerPoint.Snapshot.Length; i++) {
                char c = triggerPoint.Snapshot[i];
                if (!char.IsLetterOrDigit(c) && c != '_') {
                    break;
                }
                length++;
            }

            applicableSpan = triggerPoint.Snapshot.CreateTrackingSpan(
                spanStart,
                length,
                SpanTrackingMode.EdgeInclusive
            );

            return applicableSpan;
        }

        internal static string StripDocumentation(string doc) {
            if (doc == null) {
                return String.Empty;
            }
            StringBuilder result = new StringBuilder(doc.Length);
            foreach (string line in doc.Split('\n')) {
                if (result.Length > 0) {
                    result.Append("\r\n");
                }
                result.Append(line.Trim());
            }
            return result.ToString();
        }

        protected abstract IEnumerable<DjangoBlock> GetBlocks(IEnumerable<CompletionInfo> results, SnapshotPoint triggerPoint);

        private IEnumerable<CompletionInfo> FilterBlocks(IEnumerable<CompletionInfo> results, SnapshotPoint triggerPoint) {
            int depth = 0;
            HashSet<string> included = new HashSet<string>();
            foreach (var block in GetBlocks(results, triggerPoint)) {
                var cmd = block.ParseInfo.Command;
                if (cmd == "elif") {
                    if (depth == 0) {
                        included.Add("endif");
                    }
                    // otherwise elif both starts and ends a block, 
                    // so depth remains the same.
                } else if (DjangoAnalyzer._nestedEndTags.Contains(cmd)) {
                    depth++;
                } else if (DjangoAnalyzer._nestedStartTags.Contains(cmd)) {
                    if (depth == 0) {
                        included.Add(DjangoAnalyzer._nestedTags[cmd]);
                        if (cmd == "if") {
                            included.Add("elif");
                        }
                    }

                    // we happily let depth go negative, it'll prevent us from
                    // including an end tag for outer blocks when we're in an 
                    // inner block.
                    depth--;
                }
            }

            foreach (var value in results) {
                if (!(DjangoAnalyzer._nestedEndTags.Contains(value.DisplayText) || value.DisplayText == "elif") ||
                    included.Contains(value.DisplayText)) {
                    yield return value;
                }
            }
        }

        #endregion
        

        #region IDisposable Members

        public void Dispose() {
        }

        #endregion
    }
}
