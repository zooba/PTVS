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
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Common.Infrastructure;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using IServiceProvider = System.IServiceProvider;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.PythonTools.Editor.Intellisense {
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType(ContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class DumpAnalysisHandler : EditorCommandHandler<ITextView> {

        protected override ITextView CreateSource(IWpfTextView textView, IVsTextView textViewAdapter) {
            return textView;
        }

        protected override void QueryStatus(
            ITextView source,
            Guid cmdGroup,
            uint cmdID,
            ref string name,
            ref string status,
            ref bool supported,
            ref bool visible,
            ref bool enable,
            ref bool check
        ) {
            if (cmdGroup == Constants.Commands && cmdID == Constants.DumpAnalysis) {
                if (source.TextBuffer.IsPythonContent() && source.TextBuffer.GetDocument() != null) {
                    supported = true;
                    enable = true;
#if DEBUG
                    visible = true;
#endif
                }
            }
        }

        protected override int Exec(
            ITextView source,
            Guid cmdGroup,
            uint cmdId,
            object argIn,
            ref object argOut,
            bool allowUserInteraction,
            bool showHelpOnly,
            Func<int> forward
        ) {
            if (cmdGroup == Constants.Commands && cmdId == Constants.DumpAnalysis) {
                var analyzer = source.TextBuffer.GetAnalyzer();
                var context = source.TextBuffer.GetPythonFileContext();
                var document = source.TextBuffer.GetDocument();
                if (analyzer != null && document != null) {
                    DumpAnalysis(analyzer, context, document);
                }
            }

            return forward();
        }

        private async void DumpAnalysis(
            PythonLanguageService analyzer,
            PythonFileContext context,
            ISourceDocument document
        ) {
            try {
                await DumpAnalysisAsync(analyzer, context, document, CancellationTokens.After5s);
            } catch (OperationCanceledException) {
            } catch (Exception ex) when (!ex.IsCriticalException()) {
                
            }
        }

        private async Task DumpAnalysisAsync(
            PythonLanguageService analyzer,
            PythonFileContext context,
            ISourceDocument document,
            CancellationToken cancellationToken
        ) {
            var state = analyzer.GetAnalysisState(context, document.Moniker, true);
            using (var sw = new StringWriter()) {
                await state.DumpAsync(sw, cancellationToken);
                Clipboard.SetText(sw.ToString());
            }
        }
    }
}
