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
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.IncrementalSearch;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using IServiceProvider = System.IServiceProvider;

namespace Microsoft.PythonTools.Intellisense {
    [Export(typeof(IIntellisenseControllerProvider)), ContentType(PythonCoreConstants.ContentType), Order]
    [TextViewRole(PredefinedTextViewRoles.Analyzable)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    class IntellisenseControllerProvider : IIntellisenseControllerProvider {
        [Import]
        internal ICompletionBroker _CompletionBroker = null; // Set via MEF
        [Import]
        internal IEditorOperationsFactoryService _EditOperationsFactory = null; // Set via MEF
        [Import]
        internal IVsEditorAdaptersFactoryService _adaptersFactory { get; set; }
        [Import]
        internal ISignatureHelpBroker _SigBroker = null; // Set via MEF
        [Import]
        internal IQuickInfoBroker _QuickInfoBroker = null; // Set via MEF
        [Import]
        internal IIncrementalSearchFactoryService _IncrementalSearch = null; // Set via MEF
        internal IServiceProvider _ServiceProvider;
        internal PythonToolsService PythonService;

        [ImportingConstructor]
        public IntellisenseControllerProvider([Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider) {
            _ServiceProvider = serviceProvider;
            PythonService = serviceProvider.GetPythonToolsService();
        }

        readonly Dictionary<ITextView, Tuple<BufferParser, VsProjectAnalyzer>> _hookedCloseEvents =
            new Dictionary<ITextView, Tuple<BufferParser, VsProjectAnalyzer>>();

        public IIntellisenseController TryCreateIntellisenseController(ITextView textView, IList<ITextBuffer> subjectBuffers) {
            IntellisenseController controller;
            if (!textView.Properties.TryGetProperty(typeof(IntellisenseController), out controller)) {
                controller = new IntellisenseController(this, textView, _ServiceProvider);
            }

            foreach (var subjectBuffer in subjectBuffers) {
                controller.ConnectSubjectBuffer(subjectBuffer);
            }

            return controller;
        }

        internal static IntellisenseController GetController(ITextView textView) {
            IntellisenseController controller;
            textView.Properties.TryGetProperty(typeof(IntellisenseController), out controller);
            return controller;
        }

        internal static IntellisenseController GetOrCreateController(
            IServiceProvider serviceProvider,
            IComponentModel model,
            ITextView textView
        ) {
            IntellisenseController controller;
            if (!textView.Properties.TryGetProperty(typeof(IntellisenseController), out controller)) {
                var intellisenseControllerProvider = (
                   from export in model.DefaultExportProvider.GetExports<IIntellisenseControllerProvider, IContentTypeMetadata>()
                   from exportedContentType in export.Metadata.ContentTypes
                   where exportedContentType == PythonCoreConstants.ContentType && export.Value.GetType() == typeof(IntellisenseControllerProvider)
                   select export.Value
                ).First();
                controller = new IntellisenseController((IntellisenseControllerProvider)intellisenseControllerProvider, textView, serviceProvider);
            }
            return controller;
        }
    }

    /// <summary>
    /// Monitors creation of text view adapters for Python code so that we can attach
    /// our keyboard filter.  This enables not using a keyboard pre-preprocessor
    /// so we can process all keys for text views which we attach to.  We cannot attach
    /// our command filter on the text view when our intellisense controller is created
    /// because the adapter does not exist.
    /// </summary>
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType(PythonCoreConstants.ContentType)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    class TextViewCreationListener : IVsTextViewCreationListener {
        private readonly IVsEditorAdaptersFactoryService _adaptersFactory;

        [ImportingConstructor]
        public TextViewCreationListener(IVsEditorAdaptersFactoryService adaptersFactory) {
            _adaptersFactory = adaptersFactory;
        }

        #region IVsTextViewCreationListener Members

        public void VsTextViewCreated(VisualStudio.TextManager.Interop.IVsTextView textViewAdapter) {
            var textView = _adaptersFactory.GetWpfTextView(textViewAdapter);
            IntellisenseController controller;
            if (textView.Properties.TryGetProperty<IntellisenseController>(typeof(IntellisenseController), out controller)) {
                controller.AttachKeyboardFilter();
            }
            InitKeyBindings(textViewAdapter);
        }

        #endregion

        public void InitKeyBindings(IVsTextView vsTextView) {
            var os = vsTextView as IObjectWithSite;
            if (os == null) {
                return;
            }

            IntPtr unkSite = IntPtr.Zero;
            IntPtr unkFrame = IntPtr.Zero;

            try {
                os.GetSite(typeof(VisualStudio.OLE.Interop.IServiceProvider).GUID, out unkSite);
                var sp = Marshal.GetObjectForIUnknown(unkSite) as VisualStudio.OLE.Interop.IServiceProvider;

                sp.QueryService(typeof(SVsWindowFrame).GUID, typeof(IVsWindowFrame).GUID, out unkFrame);

                var frame = Marshal.GetObjectForIUnknown(unkFrame) as IVsWindowFrame;
                frame.SetGuidProperty((int)__VSFPROPID.VSFPROPID_InheritKeyBindings, VSConstants.GUID_TextEditorFactory);
            } finally {
                if (unkSite != IntPtr.Zero) {
                    Marshal.Release(unkSite);
                }
                if (unkFrame != IntPtr.Zero) {
                    Marshal.Release(unkFrame);
                }
            }
        }

    }
}
