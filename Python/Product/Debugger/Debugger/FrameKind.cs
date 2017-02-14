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
using Microsoft.PythonTools.Debugger.DebugEngine;

namespace Microsoft.PythonTools.Debugger {
    enum FrameKind {
        None,
        Python,
        Django
    }

    internal static class FrameKindExtensions {
        internal static void GetLanguageInfo(this FrameKind self, ref string pbstrLanguage, ref Guid pguidLanguage) {
            switch (self) {
                case FrameKind.Django:
                    pbstrLanguage = DebuggerLanguageNames.DjangoTemplates;
                    pguidLanguage = Guid.Empty;
                    break;
                case FrameKind.Python:
                    pbstrLanguage = DebuggerLanguageNames.Python;
                    pguidLanguage = DebuggerConstants.guidLanguagePython;
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

    }
}
