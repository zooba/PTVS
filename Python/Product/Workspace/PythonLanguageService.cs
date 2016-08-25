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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Editor;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Package;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.PythonTools {
    [Guid(GuidList.guidPythonLanguageService)]
    class PythonLanguageService : LanguageService {
        public override string Name => PythonConstants.LanguageName;

        public override string GetFormatFilterList() {
            return string.Join(";",
                PythonConstants.FileExtension,
                PythonConstants.WindowsFileExtension
            );
        }

        public override LanguagePreferences GetLanguagePreferences() {
            return new LanguagePreferences(Site, GetType().GUID, Name);
        }

        public override TypeAndMemberDropdownBars CreateDropDownHelper(IVsTextView forView) {
            var factory = Site.GetComponentModel().GetService<IVsEditorAdaptersFactoryService>();
            return new PythonDropdownBars(this, factory.GetWpfTextView(forView));
        }

        public override IScanner GetScanner(IVsTextLines buffer) {
            // We implement colorization via MEF
            return null;
        }

        public override AuthoringScope ParseSource(ParseRequest req) {
            // We implement parsing via workspaces
            return null;
        }

        #region IVsLanguageDebugInfo Members

        public override int GetLanguageID(IVsTextBuffer pBuffer, int iLine, int iCol, out Guid pguidLanguageID) {
            pguidLanguageID = new Guid(PythonConstants.DebugEngineGuid);
            return VSConstants.S_OK;
        }

        public override int GetLocationOfName(string pszName, out string pbstrMkDoc, TextSpan[] pspanLocation) {
            pbstrMkDoc = null;
            return VSConstants.E_FAIL;
        }

        public override int GetNameOfLocation(IVsTextBuffer pBuffer, int iLine, int iCol, out string pbstrName, out int piLineOffset) {
            var model = Site.GetComponentModel();
            var service = model.GetService<IVsEditorAdaptersFactoryService>();
            var buffer = service.GetDataBuffer(pBuffer);

            var projFile = buffer.GetAnalysisEntry(Site);
            if (projFile != null) {
                var location = projFile.Analyzer.GetNameOfLocationAsync(projFile, buffer, iLine, iCol).WaitOrDefault(1000);
                if (location != null) {
                    pbstrName = location.name;
                    piLineOffset = location.lineOffset;

                    return VSConstants.S_OK;
                }
            }

            pbstrName = null;
            piLineOffset = 0;
            return VSConstants.E_FAIL;
        }

        /// <summary>
        /// Called by debugger to get the list of expressions for the Autos debugger tool window.
        /// </summary>
        /// <remarks>
        /// MSDN docs specify that <paramref name="iLine"/> and <paramref name="iCol"/> specify the beginning of the span,
        /// but they actually specify the end of it (going <paramref name="cLines"/> lines back).
        /// </remarks>
        public override int GetProximityExpressions(IVsTextBuffer pBuffer, int iLine, int iCol, int cLines, out IVsEnumBSTR ppEnum) {
            var model = Site.GetComponentModel();
            var service = model.GetService<IVsEditorAdaptersFactoryService>();
            var buffer = service.GetDataBuffer(pBuffer);

            var projFile = buffer.GetAnalysisEntry(Site);
            if (projFile != null) {
                var names = projFile.Analyzer.GetProximityExpressionsAsync(projFile, buffer, iLine, iCol, cLines).WaitOrDefault(1000);
                ppEnum = new EnumBSTR(names);
            } else {
                ppEnum = new EnumBSTR(Enumerable.Empty<string>());
            }
            return VSConstants.S_OK;
        }

        /*public int ResolveName(string pszName, uint dwFlags, out IVsEnumDebugName ppNames) {
            if((((RESOLVENAMEFLAGS)dwFlags) & RESOLVENAMEFLAGS.RNF_BREAKPOINT) != 0) {
                // TODO: This should go through the project/analysis and see if we can
                // resolve the names...
            }
            ppNames = null;
            return VSConstants.E_FAIL;
        }*/

        public override int ValidateBreakpointLocation(IVsTextBuffer pBuffer, int iLine, int iCol, TextSpan[] pCodeSpan) {
            // per the docs, even if we don't indend to validate, we need to set the span info:
            // http://msdn.microsoft.com/en-us/library/microsoft.visualstudio.textmanager.interop.ivslanguagedebuginfo.validatebreakpointlocation.aspx
            // 
            // Caution
            // Even if you do not intend to support the ValidateBreakpointLocation method but your 
            // language does support breakpoints, you must implement this method and return a span 
            // that contains the specified line and column; otherwise, breakpoints cannot be set 
            // anywhere except line 1. You can return E_NOTIMPL to indicate that you do not otherwise 
            // support this method but the span must always be set. The example shows how this can be done.

            // http://pytools.codeplex.com/workitem/787
            // We were previously returning S_OK here indicating to VS that we have in fact validated
            // the breakpoint.  Validating breakpoints actually interacts and effectively disables
            // the "Highlight entire source line for breakpoints and current statement" option as instead
            // VS highlights the validated region.  So we return E_NOTIMPL here to indicate that we have 
            // not validated the breakpoint, and then VS will happily respect the option when we're in 
            // design mode.
            pCodeSpan[0].iStartLine = iLine;
            pCodeSpan[0].iEndLine = iLine;
            return VSConstants.E_NOTIMPL;
        }

        #endregion

    }
}
