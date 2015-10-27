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
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.PythonTools.Editor {
    [Export(typeof(ISmartIndentProvider))]
    [ContentType(PythonCoreConstants.ContentType)]
    public sealed class SmartIndentProvider : ISmartIndentProvider {
        private readonly PythonToolsService _pyService;

        [ImportingConstructor]
        internal SmartIndentProvider([Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider) {
            _pyService = serviceProvider.GetPythonToolsService();
        }

        private sealed class Indent : ISmartIndent {
            private readonly ITextView _textView;
            private readonly SmartIndentProvider _provider;

            public Indent(SmartIndentProvider provider, ITextView view) {
                _provider = provider;
                _textView = view;
            }

            public int? GetDesiredIndentation(ITextSnapshotLine line) {
                if (_provider._pyService.LangPrefs.IndentMode == vsIndentStyle.vsIndentStyleSmart) {
                    return AutoIndent.GetLineIndentation(line, _textView);
                } else {
                    return null;
                }
            }

            public void Dispose() {
            }
        }

        public ISmartIndent CreateSmartIndent(ITextView textView) {
            return new Indent(this, textView);
        }
    }
}
