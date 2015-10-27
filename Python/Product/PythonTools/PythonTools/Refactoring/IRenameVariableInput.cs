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

using Microsoft.PythonTools.Parsing;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Microsoft.PythonTools.Refactoring {
    /// <summary>
    /// Provides inputs/UI to the extract method refactoring.  Enables driving of the refactoring programmatically
    /// or via UI.
    /// </summary>
    interface IRenameVariableInput {
        RenameVariableRequest GetRenameInfo(string originalName, PythonLanguageVersion languageVersion);

        void CannotRename(string message);

        void ClearRefactorPane();

        void OutputLog(string message);

        ITextBuffer GetBufferForDocument(string filename);

        IVsLinkedUndoTransactionManager BeginGlobalUndo();

        void EndGlobalUndo(IVsLinkedUndoTransactionManager undo);

    }
}
