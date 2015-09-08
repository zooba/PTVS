using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.PythonTools.Editor.Intellisense {
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType(ContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    class EditorLanguageServiceProvider : IVsTextViewCreationListener {
        [Import]
        internal IVsEditorAdaptersFactoryService _adapterService = null;
        [Import]
        internal IInterpreterConfigurationService _configService = null;

        public void VsTextViewCreated(IVsTextView textViewAdapter) {
            var textView = _adapterService.GetWpfTextView(textViewAdapter);
            if (textView == null) {
                return;
            }

            textView.TextBuffer.Properties.GetOrCreateSingletonProperty(() => _configService.DefaultInterpreter);
        }
    }
}
