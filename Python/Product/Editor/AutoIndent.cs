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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.OptionsExtensionMethods;

namespace Microsoft.PythonTools.Editor {
    internal static class AutoIndent {
        internal static int GetIndentation(string line, int tabSize) {
            int res = 0;
            for (int i = 0; i < line.Length; i++) {
                if (line[i] == ' ') {
                    res++;
                } else if (line[i] == '\t') {
                    res += tabSize;
                } else {
                    break;
                }
            }
            return res;
        }

        private struct LineInfo {
            public static readonly LineInfo Empty = new LineInfo();
            public int NextIndentation;
            public int Indentation;
            public int NextIndentationIfNewline;
            public bool UseBlockIndent;
            public bool HasExplicitLineJoin;
        }

        internal static int? CalculateIndentation(Tokenization tokenization, int lineNumber, IEditorOptions options) {
            int tabSize = options.GetTabSize();
            int indentSize = options.GetIndentSize();

            var tokens = tokenization.GetTokensEndingAtLineReversed(lineNumber);
            var tokenStack = new Stack<Token>();
            int lastLine = lineNumber + 1;
            foreach (var token in tokens) {
                if (token.Is(TokenKind.Whitespace) && token.Span.End.Line != lastLine) {
                    // Saw whitespace that used to be a newline. We want the
                    // newline here for indentation purposes, so change it back
                    tokenStack.Push(new Token(TokenKind.NewLine, token.Span.Start, token.Span.End));
                    lastLine = token.Span.End.Line;
                    continue;
                } else if (token.Is(TokenKind.LiteralString) && token.Span.End.Line != lastLine) {
                    // Multiline strings eat the newlines. We want a token here
                    // before the literal is added
                    tokenStack.Push(new Token(TokenKind.NewLine, token.Span.End, 0));
                }
                tokenStack.Push(token);
                lastLine = token.Span.End.Line;
                if (token.Is(TokenKind.SignificantWhitespace)) {
                    break;
                }
            }

            var indentStack = new Stack<LineInfo>();
            var current = LineInfo.Empty;
            bool firstOnLine = true;

            foreach(var token in tokenStack) {
                if (firstOnLine && current.UseBlockIndent) {
                    current.Indentation = GetIndentation(tokenization.GetTokenText(token), tabSize);
                    current.NextIndentation = current.Indentation;
                }

                firstOnLine = false;

                if (token.Is(TokenKind.SignificantWhitespace)) {
                    // Significant whitespace only occurs at the highest level
                    indentStack.Clear();
                    current.Indentation = GetIndentation(tokenization.GetTokenText(token), tabSize);
                } else if (token.Is(TokenKind.NewLine)) {
                    if (current.NextIndentationIfNewline > 0) {
                        current.Indentation = current.NextIndentationIfNewline;
                        current.NextIndentation = current.Indentation;
                    } else {
                        current.Indentation = current.NextIndentation;
                    }
                    firstOnLine = true;
                }

                if (!token.IsAny(TokenKind.Whitespace, TokenKind.Comment)) {
                    current.NextIndentationIfNewline = 0;
                }

                if (token.Is(TokenUsage.BeginGroup)) {
                    indentStack.Push(current);
                    if (token.Is(TokenCategory.StringLiteral)) {
                        current = new LineInfo {
                            UseBlockIndent = true
                        };
                    } else {
                        current = new LineInfo {
                            NextIndentationIfNewline = current.Indentation + indentSize,
                            NextIndentation = token.Span.End.Column - 1
                        };
                    }
                } else if (token.Is(TokenUsage.EndGroup)) {
                    if (indentStack.Count > 0) {
                        current = indentStack.Pop();
                    }
                }

                if (!current.UseBlockIndent) {
                    // dedent after some statements
                    if (ShouldDedentAfterKeyword(token)) {
                        current.NextIndentation = current.Indentation - indentSize;
                    }

                    if (token.Is(TokenKind.ExplicitLineJoin)) {
                        if (!current.HasExplicitLineJoin) {
                            current.HasExplicitLineJoin = true;
                            current.NextIndentation = current.Indentation + indentSize;
                        }
                    }

                    // indent after a colon outside of a grouping if it is followed by a newline
                    if (token.Is(TokenKind.Colon) && indentStack.Count == 0) {
                        current.NextIndentationIfNewline = current.Indentation + indentSize;
                    }
                }
            }

            return current.Indentation;
        }

        private static bool ShouldDedentAfterKeyword(Token token) {
            return token.IsAny(
                TokenKind.KeywordPass,
                TokenKind.KeywordReturn,
                TokenKind.KeywordBreak,
                TokenKind.KeywordContinue,
                TokenKind.KeywordRaise
            );
        }

        private static bool IsBlankLine(string lineText) {
            foreach (char c in lineText) {
                if (!Char.IsWhiteSpace(c)) {
                    return false;
                }
            }
            return true;
        }

        private static void SkipPreceedingBlankLines(ITextSnapshotLine line, out string baselineText, out ITextSnapshotLine baseline) {
            string text;
            while (line.LineNumber > 0) {
                line = line.Snapshot.GetLineFromLineNumber(line.LineNumber - 1);
                text = line.GetText();
                if (!IsBlankLine(text)) {
                    baseline = line;
                    baselineText = text;
                    return;
                }
            }
            baselineText = line.GetText();
            baseline = line;
        }

        private static bool PythonContentTypePrediciate(ITextSnapshot snapshot) {
            return snapshot.ContentType.IsOfType(ContentType.Name);
        }

        internal static int? GetLineIndentation(ITextSnapshotLine line, ITextView textView) {
            var options = textView.Options;

            ITextBuffer targetBuffer = textView.TextBuffer;
            if (!targetBuffer.ContentType.IsOfType(ContentType.Name)) {
                var match = textView.BufferGraph.MapDownToFirstMatch(
                    line.Start,
                    PointTrackingMode.Positive,
                    EditorExtensions.IsPythonContent,
                    PositionAffinity.Successor
                );
                if (match == null) {
                    return 0;
                }
                targetBuffer = match.Value.Snapshot.TextBuffer;
            }

            if (!targetBuffer.IsPythonContent()) {
                // workaround debugger canvas bug - they wire our auto-indent provider up to a C# buffer
                // (they query MEF for extensions by hand and filter incorrectly) and we don't have a Python classifier.  
                // So now the user's auto-indent is broken in C# but returning null is better than crashing.
                return null;
            }

            Tokenization tokenization = null;
            try {
                tokenization = line.Snapshot.GetTokenization(CancellationTokens.After1s);
            } catch (OperationCanceledException) {
            }
            var desiredIndentation = tokenization == null ? null : CalculateIndentation(
                tokenization,
                line.LineNumber,
                options
            );

            if (desiredIndentation.HasValue && desiredIndentation.Value < 0) {
                desiredIndentation = null;
            }
            var caretLine = textView.Caret.Position.BufferPosition.GetContainingLine();
            // VS will get the white space when the user is moving the cursor or when the user is doing an edit which
            // introduces a new line.  When the user is moving the cursor the caret line differs from the line
            // we're querying.  When editing the lines are the same and so we want to account for the white space of
            // non-blank lines.  An alternate strategy here would be to watch for the edit and fix things up after
            // the fact which is what would happen pre-Dev10 when the language would not get queried for non-blank lines
            // (and is therefore what C# and other languages are doing).
            if (caretLine.LineNumber == line.LineNumber) {
                var lineText = caretLine.GetText();
                int indentationUpdate = 0;
                for (int i = textView.Caret.Position.BufferPosition.Position - caretLine.Start; i < lineText.Length; i++) {
                    if (lineText[i] == ' ') {
                        indentationUpdate++;
                    } else if (lineText[i] == '\t') {
                        indentationUpdate += textView.Options.GetIndentSize();
                    } else {
                        if (indentationUpdate > desiredIndentation) {
                            // we would dedent this line (e.g. there's a return on the previous line) but the user is
                            // hitting enter with a statement to the right of the caret and they're in the middle of white space.
                            // So we need to instead just maintain the existing indentation level.
                            //desiredIndentation = Math.Max(GetIndentation(baselineText, options.GetTabSize()) - indentationUpdate, 0);
                        } else {
                            desiredIndentation -= indentationUpdate;
                        }
                        break;
                    }
                }
            }

            return desiredIndentation;
        }
    }
}
