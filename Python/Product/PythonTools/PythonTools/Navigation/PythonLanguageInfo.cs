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
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Debugger;
using Microsoft.PythonTools.Debugger.DebugEngine;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Parsing.Ast;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.PythonTools.Navigation {
    /// <summary>
    /// Minimal language service.  Implemented directly rather than using the Managed Package
    /// Framework because we don't want to provide colorization services.  Instead we use the
    /// new Visual Studio 2010 APIs to provide these services.  But we still need this to
    /// provide a code window manager so that we can have a navigation bar (actually we don't, this
    /// should be switched over to using our TextViewCreationListener instead).
    /// </summary>
    [Guid(GuidList.guidPythonLanguageService)]
    internal sealed class PythonLanguageInfo : IVsLanguageInfo, IVsLanguageDebugInfo {
        private readonly IServiceProvider _serviceProvider;

        public PythonLanguageInfo(IServiceProvider serviceProvider) {
            _serviceProvider = serviceProvider;
        }

        public int GetCodeWindowManager(IVsCodeWindow pCodeWin, out IVsCodeWindowManager ppCodeWinMgr) {
            var ptvsService = _serviceProvider.GetPythonToolsService();
            ppCodeWinMgr = ptvsService.GetOrCreateCodeWindowManager(pCodeWin);
            return VSConstants.S_OK;
        }

        public int GetFileExtensions(out string pbstrExtensions) {
            // This is the same extension the language service was
            // registered as supporting.
            pbstrExtensions = PythonConstants.FileExtension + ";" + PythonConstants.WindowsFileExtension;
            return VSConstants.S_OK;
        }


        public int GetLanguageName(out string bstrName) {
            // This is the same name the language service was registered with.
            bstrName = PythonConstants.LanguageName;
            return VSConstants.S_OK;
        }

        /// <summary>
        /// GetColorizer is not implemented because we implement colorization using the new managed APIs.
        /// </summary>
        public int GetColorizer(IVsTextLines pBuffer, out IVsColorizer ppColorizer) {
            ppColorizer = null;
            return VSConstants.E_FAIL;
        }

        public IServiceProvider ServiceProvider {
            get {
                return _serviceProvider;
            }
        }

    }
}
