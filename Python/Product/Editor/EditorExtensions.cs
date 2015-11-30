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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.PythonTools.Editor {
    internal static class EditorExtensions {
        internal static bool IsPythonContent(this ITextBuffer buffer) {
            return buffer.ContentType.IsOfType(ContentType.Name);
        }

        internal static bool IsPythonContent(this ITextSnapshot buffer) {
            return buffer.ContentType.IsOfType(ContentType.Name);
        }

        internal static ISourceDocument GetDocument(this ITextBuffer buffer) {
            ISourceDocument document;
            return buffer.Properties.TryGetProperty(typeof(ISourceDocument), out document) ? document : null;
        }

        internal static PythonFileContext GetPythonFileContext(this ITextBuffer buffer) {
            PythonFileContext context;
            return buffer.Properties.TryGetProperty(typeof(PythonFileContext), out context) ? context : null;
        }

        internal static PythonLanguageService GetAnalyzer(this ITextBuffer buffer) {
            PythonLanguageService analyzer;
            return buffer.Properties.TryGetProperty(typeof(PythonLanguageService), out analyzer) ? analyzer : null;
        }

        internal static Tokenization GetTokenization(this ITextBuffer buffer) {
            Tokenization tokenization;
            return buffer.Properties.TryGetProperty(typeof(Tokenization), out tokenization) ? tokenization : null;
        }

        internal static Tokenization GetTokenization(
            this ITextSnapshot snapshot,
            CancellationToken cancellationToken
        ) {
            var buffer = snapshot.TextBuffer;
            ISourceDocument document = null;
            PythonFileContext context = null;
            PythonLanguageService analyzer = null;
            Tokenization tokenization;

            if (buffer.Properties.TryGetProperty(typeof(Tokenization), out tokenization) &&
                (tokenization.Cookie as ITextSnapshot) == snapshot) {
                return tokenization;
            }

            if (!buffer.Properties.TryGetProperty(typeof(ISourceDocument), out document) ||
                !buffer.Properties.TryGetProperty(typeof(PythonLanguageService), out analyzer)) {
                return null;
            }
            buffer.Properties.TryGetProperty(typeof(PythonFileContext), out context);

            var state = analyzer.GetAnalysisState(context, document.Moniker, true);
            tokenization = state.TryGetTokenization();
            if ((tokenization?.Cookie as ITextSnapshot) != snapshot) {
                tokenization = state.GetTokenizationAsync(cancellationToken).WaitAndUnwrapExceptions();
            }
            buffer.Properties[typeof(Tokenization)] = tokenization;
            return tokenization;
        }

        internal static async Task<PythonAst> GetAstAsync(
            this ITextBuffer buffer,
            CancellationToken cancellationToken
        ) {
            ISourceDocument document;
            PythonFileContext context;
            PythonLanguageService analyzer;

            if (!buffer.Properties.TryGetProperty(typeof(ISourceDocument), out document) ||
                !buffer.Properties.TryGetProperty(typeof(PythonFileContext), out context) ||
                !buffer.Properties.TryGetProperty(typeof(PythonLanguageService), out analyzer)) {
                return null;
            }

            var state = analyzer.GetAnalysisState(context, document.Moniker, false);
            return await state.GetAstAsync(cancellationToken);
        }

        public static SnapshotSpan GetApplicableSpan(
            this ITrackingPoint trigger,
            ITextSnapshot snapshot,
            bool toEndOfToken,
            CancellationToken cancellationToken
        ) {
            return trigger.GetApplicableSpan(
                snapshot,
                toEndOfToken,
                DefaultApplicableSpanPredicate,
                cancellationToken
            );
        }

        public static SnapshotSpan GetApplicableSpan(
            this ITrackingPoint trigger,
            ITextSnapshot snapshot,
            bool toEndOfToken,
            Func<Token, bool> predicate,
            CancellationToken cancellationToken
        ) {
            var tokenization = snapshot.GetTokenization(cancellationToken);

            var point = trigger.GetPoint(snapshot);
            int position = point.Position;

            foreach (var token in tokenization.GetLineByIndex(position)) {
                if (token.Span.End.Index < position || token.Span.Length == 0) {
                    // Definitely not this token
                    continue;
                } else if (predicate != null && !predicate(token)) {
                    // Not this token
                    continue;
                } else if (token.Span.Start.Index > position) {
                    // No tokens anywhere
                    break;
                }

                return new SnapshotSpan(
                    snapshot,
                    Span.FromBounds(token.Span.Start.Index, toEndOfToken ? token.Span.End.Index : position)
                );
            }

            return new SnapshotSpan(snapshot, position, 0);
        }

        public static bool DefaultApplicableSpanPredicate(Token token) {
            return token.Is(TokenCategory.Identifier);
        }

        /*public static bool CommentOrUncommentBlock(this ITextView view, bool comment) {
            SnapshotPoint start, end;
            SnapshotPoint? mappedStart, mappedEnd;

            if (view.Selection.IsActive && !view.Selection.IsEmpty) {
                // comment every line in the selection
                start = view.Selection.Start.Position;
                end = view.Selection.End.Position;
                mappedStart = MapPoint(view, start);

                var endLine = end.GetContainingLine();
                if (endLine.Start == end) {
                    // http://pytools.codeplex.com/workitem/814
                    // User selected one extra line, but no text on that line.  So let's
                    // back it up to the previous line.  It's impossible that we're on the
                    // 1st line here because we have a selection, and we end at the start of
                    // a line.  In normal selection this is only possible if we wrapped onto the
                    // 2nd line, and it's impossible to have a box selection with a single line.
                    end = end.Snapshot.GetLineFromLineNumber(endLine.LineNumber - 1).End;
                }

                mappedEnd = MapPoint(view, end);
            } else {
                // comment the current line
                start = end = view.Caret.Position.BufferPosition;
                mappedStart = mappedEnd = MapPoint(view, start);
            }

            if (mappedStart != null && mappedEnd != null &&
                mappedStart.Value <= mappedEnd.Value) {
                if (comment) {
                    CommentRegion(view, mappedStart.Value, mappedEnd.Value);
                } else {
                    UncommentRegion(view, mappedStart.Value, mappedEnd.Value);
                }

                // TODO: select multiple spans?
                // Select the full region we just commented, do not select if in projection buffer 
                // (the selection might span non-language buffer regions)
                if (view.TextBuffer.IsPythonContent()) {
                    UpdateSelection(view, start, end);
                }
                return true;
            }

            return false;
        }

        private static SnapshotPoint? MapPoint(ITextView view, SnapshotPoint point) {
            return view.BufferGraph.MapDownToFirstMatch(
               point,
               PointTrackingMode.Positive,
               IsPythonContent,
               PositionAffinity.Successor
            );
        }

        /// <summary>
        /// Adds comment characters (#) to the start of each line.  If there is a selection the comment is applied
        /// to each selected line.  Otherwise the comment is applied to the current line.
        /// </summary>
        /// <param name="view"></param>
        private static void CommentRegion(ITextView view, SnapshotPoint start, SnapshotPoint end) {
            Debug.Assert(start.Snapshot == end.Snapshot);
            var snapshot = start.Snapshot;

            using (var edit = snapshot.TextBuffer.CreateEdit()) {
                int minColumn = Int32.MaxValue;
                // first pass, determine the position to place the comment
                for (int i = start.GetContainingLine().LineNumber; i <= end.GetContainingLine().LineNumber; i++) {
                    var curLine = snapshot.GetLineFromLineNumber(i);
                    var text = curLine.GetText();

                    int firstNonWhitespace = IndexOfNonWhitespaceCharacter(text);
                    if (firstNonWhitespace >= 0 && firstNonWhitespace < minColumn) {
                        // ignore blank lines
                        minColumn = firstNonWhitespace;
                    }
                }

                // second pass, place the comment
                for (int i = start.GetContainingLine().LineNumber; i <= end.GetContainingLine().LineNumber; i++) {
                    var curLine = snapshot.GetLineFromLineNumber(i);
                    if (String.IsNullOrWhiteSpace(curLine.GetText())) {
                        continue;
                    }

                    Debug.Assert(curLine.Length >= minColumn);

                    edit.Insert(curLine.Start.Position + minColumn, "#");
                }

                edit.Apply();
            }
        }

        private static int IndexOfNonWhitespaceCharacter(string text) {
            for (int j = 0; j < text.Length; j++) {
                if (!Char.IsWhiteSpace(text[j])) {
                    return j;
                }
            }
            return -1;
        }

        /// <summary>
        /// Removes a comment character (#) from the start of each line.  If there is a selection the character is
        /// removed from each selected line.  Otherwise the character is removed from the current line.  Uncommented
        /// lines are ignored.
        /// </summary>
        private static void UncommentRegion(ITextView view, SnapshotPoint start, SnapshotPoint end) {
            Debug.Assert(start.Snapshot == end.Snapshot);
            var snapshot = start.Snapshot;

            using (var edit = snapshot.TextBuffer.CreateEdit()) {

                // first pass, determine the position to place the comment
                for (int i = start.GetContainingLine().LineNumber; i <= end.GetContainingLine().LineNumber; i++) {
                    var curLine = snapshot.GetLineFromLineNumber(i);

                    DeleteFirstCommentChar(edit, curLine);
                }

                edit.Apply();
            }
        }

        private static void UpdateSelection(ITextView view, SnapshotPoint start, SnapshotPoint end) {
            view.Selection.Select(
                new SnapshotSpan(
                // translate to the new snapshot version:
                    start.GetContainingLine().Start.TranslateTo(view.TextBuffer.CurrentSnapshot, PointTrackingMode.Negative),
                    end.GetContainingLine().End.TranslateTo(view.TextBuffer.CurrentSnapshot, PointTrackingMode.Positive)
                ),
                false
            );
        }

        private static void DeleteFirstCommentChar(ITextEdit edit, ITextSnapshotLine curLine) {
            var text = curLine.GetText();
            for (int j = 0; j < text.Length; j++) {
                if (!Char.IsWhiteSpace(text[j])) {
                    if (text[j] == '#') {
                        edit.Delete(curLine.Start.Position + j, 1);
                    }
                    break;
                }
            }
        }*/
    }
}
