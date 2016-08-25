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
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.PythonTools.Intellisense {
    static class IntellisenseExtensions {
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
                case PythonMemberType.Function:
                case PythonMemberType.Method:
                default:
                    group = StandardGlyphGroup.GlyphGroupMethod;
                    break;
            }
            return group;
        }

        internal static bool TryGetAnalyzer(this ITextView view, ITextBuffer buffer, IServiceProvider provider, out PythonLanguageService analyzer) {
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

            PythonLanguageService analyzer;
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

        /// <summary>
        /// Gets the best analyzer for this text view, accounting for things like REPL windows and
        /// difference windows.
        /// </summary>
        internal static PythonLanguageService GetBestAnalyzer(this ITextView textView, IServiceProvider serviceProvider) {
            // If we have set an analyzer explicitly, return that
            PythonLanguageService analyzer = null;
            if (textView.TextBuffer.Properties.TryGetProperty(typeof(PythonLanguageService), out analyzer)) {
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

    }
}
