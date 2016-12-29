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
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.PythonTools.Debugger.DebugEngine;
using Microsoft.PythonTools.Editor.Core;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.InterpreterList;
using Microsoft.PythonTools.Parsing;
using Microsoft.PythonTools.Parsing.Ast;
using Microsoft.PythonTools.Project;
using Microsoft.PythonTools.Repl;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.PythonTools.InteractiveWindow;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Differencing;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.Project;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.PythonTools {
    public static class Extensions {
        internal static bool IsAppxPackageableProject(this ProjectNode projectNode) {
            var appxProp = projectNode.BuildProject.GetPropertyValue(ProjectFileConstants.AppxPackage);
            var containerProp = projectNode.BuildProject.GetPropertyValue(ProjectFileConstants.WindowsAppContainer);
            var appxFlag = false;
            var containerFlag = false;

            if (bool.TryParse(appxProp, out appxFlag) && bool.TryParse(containerProp, out containerFlag)) {
                return appxFlag && containerFlag;
            } else {
                return false;
            }
        }

        public static StandardGlyphGroup ToGlyphGroup(this PythonMemberType objectType) {
            StandardGlyphGroup group;
            switch (objectType) {
                case PythonMemberType.Class: group = StandardGlyphGroup.GlyphGroupClass; break;
                case PythonMemberType.DelegateInstance: 
                case PythonMemberType.Delegate: group = StandardGlyphGroup.GlyphGroupDelegate; break;
                case PythonMemberType.Enum: group = StandardGlyphGroup.GlyphGroupEnum; break;
                case PythonMemberType.Namespace: group = StandardGlyphGroup.GlyphGroupNamespace; break;
                case PythonMemberType.Multiple: group = StandardGlyphGroup.GlyphGroupOverload; break;
                case PythonMemberType.Field: group = StandardGlyphGroup.GlyphGroupField; break;
                case PythonMemberType.Module: group = StandardGlyphGroup.GlyphGroupModule; break;
                case PythonMemberType.Property: group = StandardGlyphGroup.GlyphGroupProperty; break;
                case PythonMemberType.Instance: group = StandardGlyphGroup.GlyphGroupVariable; break;
                case PythonMemberType.Constant: group = StandardGlyphGroup.GlyphGroupVariable; break;
                case PythonMemberType.EnumInstance: group = StandardGlyphGroup.GlyphGroupEnumMember; break;
                case PythonMemberType.Event: group = StandardGlyphGroup.GlyphGroupEvent; break;
                case PythonMemberType.Keyword: group = StandardGlyphGroup.GlyphKeyword; break;
                case PythonMemberType.CodeSnippet: group = StandardGlyphGroup.GlyphCSharpExpansion; break;
                case PythonMemberType.Function:
                case PythonMemberType.Method:
                default:
                    group = StandardGlyphGroup.GlyphGroupMethod;
                    break;
            }
            return group;
        }

        internal static bool CanComplete(this ClassificationSpan token) {
            return token.ClassificationType.IsOfType(PredefinedClassificationTypeNames.Keyword) |
                token.ClassificationType.IsOfType(PredefinedClassificationTypeNames.Identifier) |
                token.ClassificationType.IsOfType(PredefinedClassificationTypeNames.String);
        }

        /// <summary>
        /// Returns the span to use for the provided intellisense session.
        /// </summary>
        /// <returns>A tracking span. The span may be of length zero if there
        /// is no suitable token at the trigger point.</returns>
        internal static ITrackingSpan GetApplicableSpan(this IIntellisenseSession session, ITextBuffer buffer) {
            var snapshot = buffer.CurrentSnapshot;
            var triggerPoint = session.GetTriggerPoint(buffer);

            var span = snapshot.GetApplicableSpan(triggerPoint);
            if (span != null) {
                return span;
            }
            return snapshot.CreateTrackingSpan(triggerPoint.GetPosition(snapshot), 0, SpanTrackingMode.EdgeInclusive);
        }

        /// <summary>
        /// Returns the applicable span at the provided position.
        /// </summary>
        /// <returns>A tracking span, or null if there is no token at the
        /// provided position.</returns>
        internal static ITrackingSpan GetApplicableSpan(this ITextSnapshot snapshot, ITrackingPoint point) {
            return snapshot.GetApplicableSpan(point.GetPosition(snapshot));
        }

        /// <summary>
        /// Returns the applicable span at the provided position.
        /// </summary>
        /// <returns>A tracking span, or null if there is no token at the
        /// provided position.</returns>
        internal static ITrackingSpan GetApplicableSpan(this ITextSnapshot snapshot, int position) {
            var classifier = snapshot.TextBuffer.GetPythonClassifier();
            var line = snapshot.GetLineFromPosition(position);
            if (classifier == null || line == null) {
                return null;
            }

            var spanLength = position - line.Start.Position;
            // Increase position by one to include 'fob' in: "abc.|fob"
            if (spanLength < line.Length) {
                spanLength += 1;
            }
            
            var classifications = classifier.GetClassificationSpans(new SnapshotSpan(line.Start, spanLength));
            // Handle "|"
            if (classifications == null || classifications.Count == 0) {
                return null;
            }

            var lastToken = classifications[classifications.Count - 1];
            // Handle "fob |"
            if (lastToken == null || position > lastToken.Span.End) {
                return null;
            }

            if (position > lastToken.Span.Start) {
                if (lastToken.ClassificationType.IsOfType(PredefinedClassificationTypeNames.String)) {
                    // Handle "'contents of strin|g"
                    var text = lastToken.Span.GetText();
                    var span = StringLiteralCompletionList.GetStringContentSpan(text, lastToken.Span.Start) ?? lastToken.Span;

                    return snapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeInclusive);
                } else if (lastToken.CanComplete()) {
                    // Handle "fo|o"
                    return snapshot.CreateTrackingSpan(lastToken.Span, SpanTrackingMode.EdgeInclusive);
                } else {
                    // Handle "<|="
                    return null;
                }
            }

            var secondLastToken = classifications.Count >= 2 ? classifications[classifications.Count - 2] : null;
            if (lastToken.Span.Start == position && lastToken.CanComplete() && 
                (secondLastToken == null ||             // Handle "|fob"
                 position > secondLastToken.Span.End || // Handle "if |fob"
                 !secondLastToken.CanComplete())) {     // Handle "abc.|fob"
                return snapshot.CreateTrackingSpan(lastToken.Span, SpanTrackingMode.EdgeInclusive);
            }

            // Handle "abc|."
            // ("ab|c." would have been treated as "ab|c")
            if (secondLastToken != null && secondLastToken.Span.End == position && secondLastToken.CanComplete()) {
                return snapshot.CreateTrackingSpan(secondLastToken.Span, SpanTrackingMode.EdgeInclusive);
            }

            return null;
        }

        internal static ITrackingSpan CreateTrackingSpan(this IQuickInfoSession session, ITextBuffer buffer) {
            var triggerPoint = session.GetTriggerPoint(buffer);
            var position = triggerPoint.GetPosition(buffer.CurrentSnapshot);
            if (position == buffer.CurrentSnapshot.Length) {
                return ((IIntellisenseSession)session).GetApplicableSpan(buffer);
            }

            return buffer.CurrentSnapshot.CreateTrackingSpan(position, 1, SpanTrackingMode.EdgeInclusive);
        }

#pragma warning disable 0618

        // TODO: Switch from smart tags to Light Bulb: http://go.microsoft.com/fwlink/?LinkId=394601
        internal static ITrackingSpan CreateTrackingSpan(this ISmartTagSession session, ITextBuffer buffer) {
            var triggerPoint = session.GetTriggerPoint(buffer);
            var position = triggerPoint.GetPosition(buffer.CurrentSnapshot);
            if (position == buffer.CurrentSnapshot.Length) {
                return ((IIntellisenseSession)session).GetApplicableSpan(buffer);
            }

            var triggerChar = triggerPoint.GetCharacter(buffer.CurrentSnapshot);
            if (position != 0 && !char.IsLetterOrDigit(triggerChar)) {
                // end of line, back up one char as we may have an identifier
                return buffer.CurrentSnapshot.CreateTrackingSpan(position - 1, 1, SpanTrackingMode.EdgeInclusive);
            }

            return buffer.CurrentSnapshot.CreateTrackingSpan(position, 1, SpanTrackingMode.EdgeInclusive);
        }

#pragma warning restore 0618

        public static IPythonInterpreterFactory GetPythonInterpreterFactory(this IVsHierarchy self) {
            var node = (self.GetProject().GetCommonProject() as PythonProjectNode);
            if (node != null) {
                return node.GetInterpreterFactory();
            }
            return null;
        }

        public static IEnumerable<IVsProject> EnumerateLoadedProjects(this IVsSolution solution) {
            var guid = new Guid(PythonConstants.ProjectFactoryGuid);
            IEnumHierarchies hierarchies;
            ErrorHandler.ThrowOnFailure((solution.GetProjectEnum(
                (uint)(__VSENUMPROJFLAGS.EPF_MATCHTYPE | __VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION),
                ref guid,
                out hierarchies)));
            IVsHierarchy[] hierarchy = new IVsHierarchy[1];
            uint fetched;
            while (ErrorHandler.Succeeded(hierarchies.Next(1, hierarchy, out fetched)) && fetched == 1) {
                var project = hierarchy[0] as IVsProject;
                if (project != null) {
                    yield return project;
                }
            }
        }

        internal static IEnumerable<PythonProjectNode> EnumerateLoadedPythonProjects(this IVsSolution solution) {
            return EnumerateLoadedProjects(solution)
                .Select(p => p.GetPythonProject())
                .Where(p => p != null);
        }

        public static IPythonProject AsPythonProject(this IVsProject project) {
            return ((IVsHierarchy)project).GetProject().GetCommonProject() as PythonProjectNode;
        }

        public static IPythonProject AsPythonProject(this EnvDTE.Project project) {
            return project.GetCommonProject() as PythonProjectNode;
        }


        internal static PythonProjectNode GetPythonProject(this IVsProject project) {
            return ((IVsHierarchy)project).GetProject().GetCommonProject() as PythonProjectNode;
        }

        internal static PythonProjectNode GetPythonProject(this EnvDTE.Project project) {
            return project.GetCommonProject() as PythonProjectNode;
        }

        internal static bool TryGetAnalyzer(this ITextView view, ITextBuffer buffer, IServiceProvider provider, out VsProjectAnalyzer analyzer) {
            analyzer = null;

            IPythonInteractiveIntellisense evaluator;
            if ((evaluator = buffer.GetInteractiveWindow()?.Evaluator as IPythonInteractiveIntellisense) != null) {
                analyzer = evaluator.Analyzer;
                return analyzer != null;
            }

            string path = buffer.GetFilePath();
            if (path != null) {
                var docTable = (IVsRunningDocumentTable4)provider.GetService(typeof(SVsRunningDocumentTable));
                if (docTable != null) {
                    var cookie = VSConstants.VSCOOKIE_NIL;
                    try {
                        cookie = docTable.GetDocumentCookie(path);
                    } catch (ArgumentException) {
                    }
                    if (cookie != VSConstants.VSCOOKIE_NIL) {
                        IVsHierarchy hierarchy;
                        uint itemid;
                        docTable.GetDocumentHierarchyItem(cookie, out hierarchy, out itemid);
                        if (hierarchy != null) {
                            var pyProject = hierarchy.GetProject()?.GetPythonProject();
                            if (pyProject != null) {
                                analyzer = pyProject.GetAnalyzer();
                            }
                        }
                    }
                }

                if (analyzer == null && view != null) {
                    // We could spin up a new analyzer for non Python projects...
                    analyzer = view.GetBestAnalyzer(provider);
                }

                return analyzer != null;
            }

            analyzer = null;
            return false;
        }

        /// <summary>
        /// Gets the analysis entry for the given view and buffer.
        /// 
        /// For files on disk this is pretty easy - we analyze each file on it's own in a buffer parser.  
        /// Therefore we map filename -> analyzer and then get the analysis from teh analyzer.  If we
        /// determine an analyzer but the file isn't loaded into it for some reason this would return null.
        /// We can also apply some policy to buffers depending upon the view that they're hosted in.  For
        /// example if a buffer is outside of any projects, but hosted in a difference view with a buffer
        /// that is in a project, then we'll look in the view that has the project.
        /// 
        /// For interactive windows we will use the analyzer that's configured for the window.
        /// </summary>
        /// <param name="view"></param>
        /// <param name="buffer"></param>
        /// <param name="provider"></param>
        /// <param name="entry"></param>
        /// <returns></returns>
        internal static bool TryGetAnalysisEntry(this ITextView view, ITextBuffer buffer, IServiceProvider provider, out AnalysisEntry entry) {
            entry = null;

            VsProjectAnalyzer analyzer;
            if (view.TryGetAnalyzer(buffer, provider, out analyzer)) {
                string path = null;
                PythonInteractiveEvaluator evaluator;
                if ((evaluator = buffer.GetInteractiveWindow()?.GetPythonEvaluator()) != null) {
                    path = evaluator.AnalysisFilename;
                } else {
                    path = buffer.GetFilePath();
                }

                if (!string.IsNullOrEmpty(path)) {
                    entry = analyzer.GetAnalysisEntryFromPath(path);
                }
                return entry != null;
            }
            return false;
        }

        internal static AnalysisEntry GetAnalysisEntry(this FileNode node) {
            return ((PythonProjectNode)node.ProjectMgr).GetAnalyzer().GetAnalysisEntryFromPath(node.Url);
        }

        /// <summary>
        /// Gets the best analyzer for this text view, accounting for things like REPL windows and
        /// difference windows.
        /// </summary>
        internal static VsProjectAnalyzer GetBestAnalyzer(this ITextView textView, IServiceProvider serviceProvider) {
            // If we have set an analyzer explicitly, return that
            VsProjectAnalyzer analyzer = null;
            if (textView.TextBuffer.Properties.TryGetProperty(typeof(VsProjectAnalyzer), out analyzer)) {
                return analyzer;
            }

            // If we have a REPL evaluator we'll use it's analyzer
            var evaluator = textView.TextBuffer.GetInteractiveWindow()?.Evaluator as IPythonInteractiveIntellisense;
            if (evaluator != null) {
                return evaluator.Analyzer;
            }

            // If we have a difference viewer we'll match the LHS w/ the RHS
            IWpfDifferenceViewerFactoryService diffService = null;
            try {
                diffService = serviceProvider.GetComponentModel().GetService<IWpfDifferenceViewerFactoryService>();
            } catch (System.ComponentModel.Composition.CompositionException) {
            } catch (System.ComponentModel.Composition.ImportCardinalityMismatchException) {
            }
            if (diffService != null) {
                var viewer = diffService.TryGetViewerForTextView(textView);
                if (viewer != null) {

                    var entry = GetAnalysisEntry(null, viewer.DifferenceBuffer.LeftBuffer, serviceProvider) ??
                        GetAnalysisEntry(null, viewer.DifferenceBuffer.RightBuffer, serviceProvider);

                    if (entry != null) {
                        return entry.Analyzer;
                    }
                }
            }

            return serviceProvider.GetPythonToolsService().DefaultAnalyzer;
        }

        internal static AnalysisEntry GetAnalysisEntry(this ITextView view, ITextBuffer buffer, IServiceProvider provider) {
            AnalysisEntry res;
            view.TryGetAnalysisEntry(buffer, provider, out res);
            return res;
        }

        /// <summary>
        /// Gets an analysis entry for this buffer.  This will only succeed if the buffer is a file
        /// on disk.  It is not able to support things like difference views because we don't know
        /// what view this buffer is hosted in.  This method should only be used when we don't know
        /// the current view for the buffer.  Instead calling view.GetAnalysisEntry or view.TryGetAnalysisEntry
        /// should be used.
        /// </summary>
        internal static AnalysisEntry GetAnalysisEntry(this ITextBuffer buffer, IServiceProvider serviceProvider) {
            AnalysisEntry res;
            TryGetAnalysisEntry(null, buffer, serviceProvider, out res);
            return res;
        }

        internal static PythonProjectNode GetProject(this ITextBuffer buffer, IServiceProvider serviceProvider) {
            var path = buffer.GetFilePath();
            if (path != null) {
                var sln = serviceProvider.GetService(typeof(SVsSolution)) as IVsSolution;
                if (sln != null) {
                    foreach (var proj in sln.EnumerateLoadedPythonProjects()) {
                        int found;
                        var priority = new VSDOCUMENTPRIORITY[1];
                        uint itemId;
                        ErrorHandler.ThrowOnFailure(proj.IsDocumentInProject(path, out found, priority, out itemId));
                        if (found != 0) {
                            return proj;
                        }
                    }
                }
            }
            return null;
        }

        internal static PythonLanguageVersion GetLanguageVersion(this ITextView textView, IServiceProvider serviceProvider) {
            var evaluator = textView.TextBuffer.GetInteractiveWindow().GetPythonEvaluator();
            if (evaluator != null) {
                return evaluator.LanguageVersion;
            }
            return textView.GetBestAnalyzer(serviceProvider).LanguageVersion;
        }

        /// <summary>
        /// Returns the active VsProjectAnalyzer being used for where the caret is currently located in this view.
        /// </summary>
        internal static VsProjectAnalyzer GetAnalyzerAtCaret(this ITextView textView, IServiceProvider serviceProvider) {
            var buffer = textView.GetPythonBufferAtCaret();
            if (buffer != null) {
                return textView.GetAnalysisEntry(buffer, serviceProvider)?.Analyzer;
            }

            return null;
        }

        /// <summary>
        /// Returns the AnalysisEntry being used for where the caret is currently located in this view.
        /// 
        /// Returns null if the caret isn't in Python code or an analysis doesn't exist for some reason.
        /// </summary>
        internal static AnalysisEntry GetAnalysisAtCaret(this ITextView textView, IServiceProvider serviceProvider) {
            var buffer = textView.GetPythonBufferAtCaret();
            if (buffer != null) {
                return textView.GetAnalysisEntry(buffer, serviceProvider);
            }

            return null;
        }

        /// <summary>
        /// Returns the ITextBuffer whose content type is Python for the current caret position in the text view.
        /// 
        /// Returns null if the caret isn't in a Python buffer.
        /// </summary>
        internal static ITextBuffer GetPythonBufferAtCaret(this ITextView textView) {
            return GetPythonCaret(textView)?.Snapshot.TextBuffer;
        }

        /// <summary>
        /// Gets the point where the caret is currently located in a Python buffer, or null if the caret
        /// isn't currently positioned in a Python buffer.
        /// </summary>
        internal static SnapshotPoint? GetPythonCaret(this ITextView textView) {
            return textView.BufferGraph.MapDownToFirstMatch(
                textView.Caret.Position.BufferPosition,
                PointTrackingMode.Positive,
                EditorExtensions.IsPythonContent,
                PositionAffinity.Successor
            );
        }

        /// <summary>
        /// Gets the current selection in a text view mapped down to the Python buffer(s).
        /// </summary>
        internal static NormalizedSnapshotSpanCollection GetPythonSelection(this ITextView textView) {
            return textView.BufferGraph.MapDownToFirstMatch(
                textView.Selection.StreamSelectionSpan.SnapshotSpan,
                SpanTrackingMode.EdgeInclusive,
                EditorExtensions.IsPythonContent
            );
        }

        /// <summary>
        /// Gets the Python project node associatd with the buffer where the caret is located.
        /// 
        /// This maps down to the current Python buffer, determines its filename, and then resolves
        /// that filename back to the project.
        /// </summary>
        internal static PythonProjectNode GetProjectAtCaret(this ITextView textView, IServiceProvider serviceProvider) {
            var point = textView.BufferGraph.MapDownToFirstMatch(
                textView.Caret.Position.BufferPosition,
                PointTrackingMode.Positive,
                EditorExtensions.IsPythonContent,
                PositionAffinity.Successor
            );

            if (point != null) {
                var filename = point.Value.Snapshot.TextBuffer.GetFilePath();
                return GetProjectFromFile(serviceProvider, filename);
            }

            return null;
        }

        internal static PythonProjectNode GetProjectFromFile(this IServiceProvider serviceProvider, string filename) {
            var docTable = serviceProvider.GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable4;
            var cookie = docTable.GetDocumentCookie(filename);

            if (cookie != VSConstants.VSCOOKIE_NIL) {
                IVsHierarchy hierarchy;
                uint itemid;
                docTable.GetDocumentHierarchyItem(cookie, out hierarchy, out itemid);
                var project = hierarchy.GetProject();
                if (project != null) {
                    return project.GetPythonProject();
                }

                object projectObj;
                ErrorHandler.ThrowOnFailure(hierarchy.GetProperty(itemid, (int)__VSHPROPID.VSHPROPID_ExtObject, out projectObj));
                return (projectObj as EnvDTE.Project)?.GetPythonProject();
            }
            return null;
        }
        
        internal static ITrackingSpan GetCaretSpan(this ITextView view) {
            var caretPoint = view.GetPythonCaret();
            Debug.Assert(caretPoint != null);
            var snapshot = caretPoint.Value.Snapshot;
            var caretPos = caretPoint.Value.Position;

            // fob(
            //    ^
            //    +---  Caret here
            //
            // We want to lookup fob, not fob(
            //
            ITrackingSpan span;
            if (caretPos != snapshot.Length) {
                string curChar = snapshot.GetText(caretPos, 1);
                if (!IsIdentifierChar(curChar[0]) && caretPos > 0) {
                    string prevChar = snapshot.GetText(caretPos - 1, 1);
                    if (IsIdentifierChar(prevChar[0])) {
                        caretPos--;
                    }
                }
                span = snapshot.CreateTrackingSpan(
                    caretPos,
                    1,
                    SpanTrackingMode.EdgeInclusive
                );
            } else {
                span = snapshot.CreateTrackingSpan(
                    caretPos,
                    0,
                    SpanTrackingMode.EdgeInclusive
                );
            }

            return span;
        }

        private static bool IsIdentifierChar(char curChar) {
            return Char.IsLetterOrDigit(curChar) || curChar == '_';
        }

        /// <summary>
        /// Reads a string from the socket which is encoded as:
        ///     U, byte count, bytes 
        ///     A, byte count, ASCII
        ///     
        /// Which supports either UTF-8 or ASCII strings.
        /// </summary>
        internal static string ReadString(this Socket socket) {
            byte[] cmd_buffer = new byte[4];
            if (socket.Receive(cmd_buffer, 1, SocketFlags.None) == 1) {
                bool isUnicode = cmd_buffer[0] == 'U';

                if (socket.Receive(cmd_buffer) == 4) {
                    int filenameLen = BitConverter.ToInt32(cmd_buffer, 0);
                    byte[] buffer = new byte[filenameLen];
                    if (filenameLen != 0) {
                        int bytesRead = 0;
                        do {
                            bytesRead += socket.Receive(buffer, bytesRead, filenameLen - bytesRead, SocketFlags.None);
                        } while (bytesRead != filenameLen);
                    }

                    if (isUnicode) {
                        return Encoding.UTF8.GetString(buffer);
                    } else {
                        char[] chars = new char[buffer.Length];
                        for (int i = 0; i < buffer.Length; i++) {
                            chars[i] = (char)buffer[i];
                        }
                        return new string(chars);
                    }
                } else {
                    Debug.Assert(false, "Failed to read length");
                }
            } else {
                Debug.Assert(false, "Failed to read unicode/ascii byte");
            }
            return null;
        }

        internal static int ReadInt(this Socket socket) {
            byte[] cmd_buffer = new byte[4];
            if (socket.Receive(cmd_buffer) == 4) {
                return BitConverter.ToInt32(cmd_buffer, 0);
            }
            throw new InvalidOperationException();
        }

        internal static VsProjectAnalyzer GetAnalyzer(this ITextBuffer buffer, IServiceProvider serviceProvider) {
            var analysisEntry = GetAnalysisEntry(null, buffer, serviceProvider);
            if (analysisEntry != null) {
                return analysisEntry.Analyzer;
            }

            VsProjectAnalyzer analyzer;
            // exists for tests where we don't run in VS and for the existing changes preview
            if (buffer.Properties.TryGetProperty(typeof(VsProjectAnalyzer), out analyzer)) {
                return analyzer;
            }

            return serviceProvider.GetPythonToolsService().DefaultAnalyzer;
        }

        internal static PythonToolsService GetPythonToolsService(this IServiceProvider serviceProvider) {
            if (serviceProvider == null) {
                return null;
            }
#if DEBUG
            // https://github.com/Microsoft/PTVS/issues/1205
            // Help see when this function is being incorrectly called from off
            // the UI thread. There's a chance that GetService() will fail in
            // this case too, but mostly it will succeed (and then assert).
            var uiThread = serviceProvider.GetService(typeof(UIThreadBase)) as UIThreadBase;
            uiThread?.MustBeCalledFromUIThread();
#endif
            return serviceProvider.GetPythonToolsService_NotThreadSafe();
        }

        /// <summary>
        /// Gets the current Python Tools service without validating that we are
        /// on the UI thread. This may return null or crash at (somewhat) random
        /// but is necessary for some tests.
        /// </summary>
        internal static PythonToolsService GetPythonToolsService_NotThreadSafe(this IServiceProvider serviceProvider) {
            var pyService = (PythonToolsService)serviceProvider.GetService(typeof(PythonToolsService));
            if (pyService == null) {
                var shell = (IVsShell)serviceProvider.GetService(typeof(SVsShell));

                var pkgGuid = GuidList.guidPythonToolsPackage;
                IVsPackage pkg;
                if (!ErrorHandler.Succeeded(shell.IsPackageLoaded(ref pkgGuid, out pkg)) && pkg != null) {
                    Debug.Fail("Python Tools Package was loaded but could not get service");
                    return null;
                }
                var hr = shell.LoadPackage(ref pkgGuid, out pkg);
                if (!ErrorHandler.Succeeded(hr)) {
                    Debug.Fail("Failed to load Python Tools Package: 0x{0:X08}".FormatUI(hr));
                    ErrorHandler.ThrowOnFailure(hr);
                }

                pyService = (PythonToolsService)serviceProvider.GetService(typeof(PythonToolsService));
            }
            return pyService;
        }

        internal static IComponentModel GetComponentModel(this IServiceProvider serviceProvider) {
            if (serviceProvider == null) {
                return null;
            }
            return (IComponentModel)serviceProvider.GetService(typeof(SComponentModel));
        }

        public static string BrowseForFileSave(this IServiceProvider provider, IntPtr owner, string filter, string initialPath = null) {
            if (string.IsNullOrEmpty(initialPath)) {
                initialPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal) + Path.DirectorySeparatorChar;
            }

            IVsUIShell uiShell = provider.GetService(typeof(SVsUIShell)) as IVsUIShell;
            if (null == uiShell) {
                using (var sfd = new System.Windows.Forms.SaveFileDialog()) {
                    sfd.AutoUpgradeEnabled = true;
                    sfd.Filter = filter;
                    sfd.FileName = Path.GetFileName(initialPath);
                    sfd.InitialDirectory = Path.GetDirectoryName(initialPath);
                    DialogResult result;
                    if (owner == IntPtr.Zero) {
                        result = sfd.ShowDialog();
                    } else {
                        result = sfd.ShowDialog(NativeWindow.FromHandle(owner));
                    }
                    if (result == DialogResult.OK) {
                        return sfd.FileName;
                    } else {
                        return null;
                    }
                }
            }

            if (owner == IntPtr.Zero) {
                ErrorHandler.ThrowOnFailure(uiShell.GetDialogOwnerHwnd(out owner));
            }

            VSSAVEFILENAMEW[] saveInfo = new VSSAVEFILENAMEW[1];
            saveInfo[0].lStructSize = (uint)Marshal.SizeOf(typeof(VSSAVEFILENAMEW));
            saveInfo[0].pwzFilter = filter.Replace('|', '\0') + "\0";
            saveInfo[0].hwndOwner = owner;
            saveInfo[0].nMaxFileName = 260;
            var pFileName = Marshal.AllocCoTaskMem(520);
            saveInfo[0].pwzFileName = pFileName;
            saveInfo[0].pwzInitialDir = Path.GetDirectoryName(initialPath);
            var nameArray = (Path.GetFileName(initialPath) + "\0").ToCharArray();
            Marshal.Copy(nameArray, 0, pFileName, nameArray.Length);
            try {
                int hr = uiShell.GetSaveFileNameViaDlg(saveInfo);
                if (hr == VSConstants.OLE_E_PROMPTSAVECANCELLED) {
                    return null;
                }
                ErrorHandler.ThrowOnFailure(hr);
                return Marshal.PtrToStringAuto(saveInfo[0].pwzFileName);
            } finally {
                if (pFileName != IntPtr.Zero) {
                    Marshal.FreeCoTaskMem(pFileName);
                }
            }
        }

        public static string BrowseForFileOpen(this IServiceProvider serviceProvider, IntPtr owner, string filter, string initialPath = null) {
            if (string.IsNullOrEmpty(initialPath)) {
                initialPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal) + Path.DirectorySeparatorChar;
            }

            IVsUIShell uiShell = serviceProvider.GetService(typeof(SVsUIShell)) as IVsUIShell;
            if (null == uiShell) {
                using (var sfd = new System.Windows.Forms.OpenFileDialog()) {
                    sfd.AutoUpgradeEnabled = true;
                    sfd.Filter = filter;
                    sfd.FileName = Path.GetFileName(initialPath);
                    sfd.InitialDirectory = Path.GetDirectoryName(initialPath);
                    DialogResult result;
                    if (owner == IntPtr.Zero) {
                        result = sfd.ShowDialog();
                    } else {
                        result = sfd.ShowDialog(NativeWindow.FromHandle(owner));
                    }
                    if (result == DialogResult.OK) {
                        return sfd.FileName;
                    } else {
                        return null;
                    }
                }
            }

            if (owner == IntPtr.Zero) {
                ErrorHandler.ThrowOnFailure(uiShell.GetDialogOwnerHwnd(out owner));
            }

            VSOPENFILENAMEW[] openInfo = new VSOPENFILENAMEW[1];
            openInfo[0].lStructSize = (uint)Marshal.SizeOf(typeof(VSOPENFILENAMEW));
            openInfo[0].pwzFilter = filter.Replace('|', '\0') + "\0";
            openInfo[0].hwndOwner = owner;
            openInfo[0].nMaxFileName = 260;
            var pFileName = Marshal.AllocCoTaskMem(520);
            openInfo[0].pwzFileName = pFileName;
            openInfo[0].pwzInitialDir = Path.GetDirectoryName(initialPath);
            var nameArray = (Path.GetFileName(initialPath) + "\0").ToCharArray();
            Marshal.Copy(nameArray, 0, pFileName, nameArray.Length);
            try {
                int hr = uiShell.GetOpenFileNameViaDlg(openInfo);
                if (hr == VSConstants.OLE_E_PROMPTSAVECANCELLED) {
                    return null;
                }
                ErrorHandler.ThrowOnFailure(hr);
                return Marshal.PtrToStringAuto(openInfo[0].pwzFileName);
            } finally {
                if (pFileName != IntPtr.Zero) {
                    Marshal.FreeCoTaskMem(pFileName);
                }
            }
        }

        internal static IContentType GetPythonContentType(this IServiceProvider provider) {
            return provider.GetComponentModel().GetService<IContentTypeRegistryService>().GetContentType(PythonCoreConstants.ContentType);
        }

        internal static EnvDTE.DTE GetDTE(this IServiceProvider provider) {
            return (EnvDTE.DTE)provider.GetService(typeof(EnvDTE.DTE));
        }

        internal static IVsShell GetShell(this IServiceProvider provider) {
            return (IVsShell)provider.GetService(typeof(SVsShell));
        }

        internal static bool TryGetShellProperty<T>(this IServiceProvider provider, __VSSPROPID propId, out T value) {
            object obj;
            if (ErrorHandler.Failed(provider.GetShell().GetProperty((int)propId, out obj))) {
                value = default(T);
                return false;
            }
            try {
                value = (T)obj;
                return true;
            } catch (InvalidCastException) {
                Debug.Fail("Expected property of type {0} but got value of type {1}".FormatUI(typeof(T).FullName, obj.GetType().FullName));
                value = default(T);
                return false;
            }
        }

        internal static bool IsShellInitialized(this IServiceProvider provider) {
            bool isInitialized;
            return provider.TryGetShellProperty((__VSSPROPID)__VSSPROPID4.VSSPROPID_ShellInitialized, out isInitialized) &&
                isInitialized;
        }

        class ShellInitializedNotification : IVsShellPropertyEvents {
            private readonly IVsShell _shell;
            private readonly uint _cookie;
            private readonly TaskCompletionSource<object> _tcs;

            public ShellInitializedNotification(IVsShell shell) {
                _shell = shell;
                _tcs = new TaskCompletionSource<object>();
                ErrorHandler.ThrowOnFailure(_shell.AdviseShellPropertyChanges(this, out _cookie));

                // Check again in case we raised with initialization
                object value;
                if (ErrorHandler.Succeeded(_shell.GetProperty((int)__VSSPROPID4.VSSPROPID_ShellInitialized, out value)) &&
                    CheckProperty((int)__VSSPROPID4.VSSPROPID_ShellInitialized, value)) {
                    return;
                }

                if (ErrorHandler.Succeeded(_shell.GetProperty((int)__VSSPROPID6.VSSPROPID_ShutdownStarted, out value)) &&
                    CheckProperty((int)__VSSPROPID6.VSSPROPID_ShutdownStarted, value)) {
                    return;
                }
            }

            private bool CheckProperty(int propid, object var) {
                if (propid == (int)__VSSPROPID4.VSSPROPID_ShellInitialized && var is bool && (bool)var) {
                    _shell.UnadviseShellPropertyChanges(_cookie);
                    _tcs.TrySetResult(null);
                    return true;
                } else if (propid == (int)__VSSPROPID6.VSSPROPID_ShutdownStarted && var is bool && (bool)var) {
                    _shell.UnadviseShellPropertyChanges(_cookie);
                    _tcs.TrySetCanceled();
                    return true;
                }
                return false;
            }

            public Task Task => _tcs.Task;

            int IVsShellPropertyEvents.OnShellPropertyChange(int propid, object var) {
                CheckProperty(propid, var);
                return VSConstants.S_OK;
            }
        }

        internal static Task WaitForShellInitializedAsync(this IServiceProvider provider) {
            if (provider.IsShellInitialized()) {
                return Task.FromResult<object>(null);
            }
            return new ShellInitializedNotification(provider.GetShell()).Task;
        }

        [Conditional("DEBUG")]
        internal static void AssertShellIsInitialized(this IServiceProvider provider) {
            Debug.Assert(provider.IsShellInitialized(), "Shell is not yet initialized");
        }

        internal static IVsDebugger GetShellDebugger(this IServiceProvider provider) {
            return (IVsDebugger)provider.GetService(typeof(SVsShellDebugger));
        }

        internal static async Task RefreshVariableViews(this IServiceProvider serviceProvider) {
            serviceProvider.GetUIThread().MustBeCalledFromUIThread();
            EnvDTE.Debugger debugger = serviceProvider.GetDTE().Debugger;
            AD7Engine engine = AD7Engine.GetEngineForProcess(debugger.CurrentProcess);
            if (engine != null) {
                await engine.RefreshThreadFrames(debugger.CurrentThread.ID);
                var vsDebugger = serviceProvider.GetShellDebugger() as IDebugRefreshNotification140;
                if (vsDebugger != null) {
                    // Passing fCallstackFormattingAffected = TRUE to OnExpressionEvaluationRefreshRequested to force refresh
                    vsDebugger.OnExpressionEvaluationRefreshRequested(1);
                }
            }
        }

        internal static SolutionEventsListener GetSolutionEvents(this IServiceProvider serviceProvider) {
            return (SolutionEventsListener)serviceProvider.GetService(typeof(SolutionEventsListener));
        }

        internal static void GlobalInvoke(this IServiceProvider serviceProvider, CommandID cmdID) {
            OleMenuCommandService mcs = serviceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            mcs.GlobalInvoke(cmdID);
        }

        internal static void GlobalInvoke(this IServiceProvider serviceProvider, CommandID cmdID, object arg) {
            OleMenuCommandService mcs = serviceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            mcs.GlobalInvoke(cmdID, arg);
        }

        internal static void ShowOptionsPage(this IServiceProvider serviceProvider, Type optionsPageType) {
            CommandID cmd = new CommandID(VSConstants.GUID_VSStandardCommandSet97, VSConstants.cmdidToolsOptions);
            serviceProvider.GlobalInvoke(
                cmd,
                optionsPageType.GUID.ToString()
            );
        }

        internal static void ShowInterpreterList(this IServiceProvider serviceProvider) {
            serviceProvider.ShowWindowPane(typeof(InterpreterListToolWindow), true);
        }

        internal static void ShowWindowPane(this IServiceProvider serviceProvider, Type windowPane, bool focus) {
            var toolWindowService = (IPythonToolsToolWindowService)serviceProvider.GetService(typeof(IPythonToolsToolWindowService));
            toolWindowService.ShowWindowPane(windowPane, focus);
        }

        public static string BrowseForDirectory(this IServiceProvider provider, IntPtr owner, string initialDirectory = null) {
            IVsUIShell uiShell = provider.GetService(typeof(SVsUIShell)) as IVsUIShell;
            if (null == uiShell) {
                using (var ofd = new FolderBrowserDialog()) {
                    ofd.RootFolder = Environment.SpecialFolder.Desktop;
                    ofd.ShowNewFolderButton = false;
                    DialogResult result;
                    if (owner == IntPtr.Zero) {
                        result = ofd.ShowDialog();
                    } else {
                        result = ofd.ShowDialog(NativeWindow.FromHandle(owner));
                    }
                    if (result == DialogResult.OK) {
                        return ofd.SelectedPath;
                    } else {
                        return null;
                    }
                }
            }

            if (owner == IntPtr.Zero) {
                ErrorHandler.ThrowOnFailure(uiShell.GetDialogOwnerHwnd(out owner));
            }

            VSBROWSEINFOW[] browseInfo = new VSBROWSEINFOW[1];
            browseInfo[0].lStructSize = (uint)Marshal.SizeOf(typeof(VSBROWSEINFOW));
            browseInfo[0].pwzInitialDir = initialDirectory;
            browseInfo[0].hwndOwner = owner;
            browseInfo[0].nMaxDirName = 260;
            IntPtr pDirName = Marshal.AllocCoTaskMem(520);
            browseInfo[0].pwzDirName = pDirName;
            try {
                int hr = uiShell.GetDirectoryViaBrowseDlg(browseInfo);
                if (hr == VSConstants.OLE_E_PROMPTSAVECANCELLED) {
                    return null;
                }
                ErrorHandler.ThrowOnFailure(hr);
                return Marshal.PtrToStringAuto(browseInfo[0].pwzDirName);
            } finally {
                if (pDirName != IntPtr.Zero) {
                    Marshal.FreeCoTaskMem(pDirName);
                }
            }
        }

        /// <summary>
        /// Checks to see if this is a REPL buffer starting with a extensible command such as %cls, %load, etc...
        /// </summary>
        internal static bool IsReplBufferWithCommand(this ITextSnapshot snapshot) {
            return snapshot.TextBuffer.Properties.ContainsProperty(typeof(IInteractiveEvaluator)) &&
                   snapshot.Length != 0 &&
                   (snapshot[0] == '%' || snapshot[0] == '$'); // IPython and normal repl commands
        }

        internal static bool IsAnalysisCurrent(this IPythonInterpreterFactory factory) {
            var interpFact = factory as IPythonInterpreterFactoryWithDatabase;
            if (interpFact != null) {
                return interpFact.IsCurrent;
            }

            return true;
        }

        internal static bool IsOpenGrouping(this ClassificationSpan span) {
            return span.ClassificationType.IsOfType(PythonPredefinedClassificationTypeNames.Grouping) &&
                span.Span.Length == 1 &&
                (span.Span.GetText() == "{" || span.Span.GetText() == "[" || span.Span.GetText() == "(");
        }

        internal static bool IsCloseGrouping(this ClassificationSpan span) {
            return span.ClassificationType.IsOfType(PythonPredefinedClassificationTypeNames.Grouping) &&
                span.Span.Length == 1 &&
                (span.Span.GetText() == "}" || span.Span.GetText() == "]" || span.Span.GetText() == ")");
        }

        internal static T Pop<T>(this List<T> list) {
            if (list.Count == 0) {
                throw new InvalidOperationException();
            }
            var res = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return res;
        }

        internal static T Peek<T>(this List<T> list) {
            if (list.Count == 0) {
                throw new InvalidOperationException();
            }
            return list[list.Count - 1];
        }

        internal static System.Threading.Tasks.Task StartNew(this TaskScheduler scheduler, Action func) {
            return System.Threading.Tasks.Task.Factory.StartNew(func, default(CancellationToken), TaskCreationOptions.None, scheduler);
        }

        internal static int GetStartIncludingIndentation(this Node self, PythonAst ast) {
            return self.StartIndex - (self.GetIndentationLevel(ast) ?? "").Length;
        }

        internal static string LimitLines(
            this string str,
            int maxLines = 30,
            int charsPerLine = 200,
            bool ellipsisAtEnd = true,
            bool stopAtFirstBlankLine = false
        ) {
            if (string.IsNullOrEmpty(str)) {
                return str;
            }

            int lineCount = 0;
            var prettyPrinted = new StringBuilder();
            bool wasEmpty = true;

            using (var reader = new StringReader(str)) {
                for (var line = reader.ReadLine(); line != null && lineCount < maxLines; line = reader.ReadLine()) {
                    if (string.IsNullOrWhiteSpace(line)) {
                        if (wasEmpty) {
                            continue;
                        }
                        wasEmpty = true;
                        if (stopAtFirstBlankLine) {
                            lineCount = maxLines;
                            break;
                        }
                        lineCount += 1;
                        prettyPrinted.AppendLine();
                    } else {
                        wasEmpty = false;
                        lineCount += (line.Length / charsPerLine) + 1;
                        prettyPrinted.AppendLine(line);
                    }
                }
            }
            if (ellipsisAtEnd && lineCount >= maxLines) {
                prettyPrinted.AppendLine("...");
            }
            return prettyPrinted.ToString().Trim();
        }
    }
}
