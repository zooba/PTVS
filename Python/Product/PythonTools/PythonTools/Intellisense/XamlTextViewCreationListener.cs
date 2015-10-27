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
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.PythonTools.Intellisense {
    /// <summary>
    /// Watches for text views to be created for xaml code.  Then wires up to support analysis so that
    /// we can use the analysis for completion in .py code.
    /// </summary>
    [Export(typeof(IVsTextViewCreationListener))]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [ContentType("xaml")]
    class XamlTextViewCreationListener : IVsTextViewCreationListener {
        internal readonly IVsEditorAdaptersFactoryService AdapterService;
        private readonly IServiceProvider _serviceProvider;

        [ImportingConstructor]
        public XamlTextViewCreationListener(
            [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider,
            IVsEditorAdaptersFactoryService adapterService
        ) {
            _serviceProvider = serviceProvider;
            AdapterService = adapterService;
        }

        public void VsTextViewCreated(VisualStudio.TextManager.Interop.IVsTextView textViewAdapter) {
            // TODO: We should probably only track text views in Python projects or loose files.
            ITextView textView = AdapterService.GetWpfTextView(textViewAdapter);
            
            if (textView != null) {
                var analyzer = textView.GetAnalyzer(_serviceProvider);
                if (analyzer != null) {
                    var monitorResult = analyzer.MonitorTextBuffer(textView, textView.TextBuffer);
                    textView.Closed += TextView_Closed;
                }
            }
        }

        private void TextView_Closed(object sender, EventArgs e) {
            var textView = (ITextView)sender;

            BufferParser bufferParser;
            if (textView.Properties.TryGetProperty<BufferParser>(typeof(BufferParser), out bufferParser)) {
                textView.GetAnalyzer(_serviceProvider).StopMonitoringTextBuffer(bufferParser, textView);
            }

            textView.Closed -= TextView_Closed;
        }
    }
}

