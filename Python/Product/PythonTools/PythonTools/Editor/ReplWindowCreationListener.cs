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
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Language;
using Microsoft.PythonTools.Repl;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.PythonTools.InteractiveWindow;
using Microsoft.PythonTools.InteractiveWindow.Shell;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using IOleCommandTarget = Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget;

namespace Microsoft.PythonTools.Editor {
    [Export(typeof(IVsInteractiveWindowOleCommandTargetProvider))]
    [ContentType(PythonCoreConstants.ContentType)]
    public class ReplWindowCreationListener : IVsInteractiveWindowOleCommandTargetProvider {
        private readonly IServiceProvider _serviceProvider;
        private readonly IComponentModel _componentModel;

        [ImportingConstructor]
        public ReplWindowCreationListener([Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider) {
            _serviceProvider = serviceProvider;
            _componentModel = _serviceProvider.GetComponentModel();
        }

        public IOleCommandTarget GetCommandTarget(IWpfTextView textView, IOleCommandTarget nextTarget) {
            var window = textView.TextBuffer.GetInteractiveWindow();

            var controller = IntellisenseControllerProvider.GetOrCreateController(
                _serviceProvider,
                _componentModel,
                textView
            );
            controller._oldTarget = nextTarget;

            var editFilter = EditFilter.GetOrCreate(_serviceProvider, _componentModel, textView, controller);

            if (window == null) {
                return editFilter;
            }

            textView.Properties[IntellisenseController.SuppressErrorLists] = IntellisenseController.SuppressErrorLists;
            return ReplEditFilter.GetOrCreate(_serviceProvider, _componentModel, textView, editFilter);
        }
    }
}
