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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using Microsoft.Web.Editor.Controller;

namespace Microsoft.PythonTools.Django.Intellisense {
    [Export(typeof(ICommandFactory))]
    [ContentType(TemplateTagContentType.ContentTypeName)]
    internal class TemplateCommandFactory : ICommandFactory {
        [Import(typeof(IEditorOptionsFactoryService))]
        IEditorOptionsFactoryService _editorOptionsFactory = null;

        [Import(typeof(IEditorOperationsFactoryService))]
        IEditorOperationsFactoryService _editorOperationsFactory = null;

        public TemplateCommandFactory() {
        }

        public IEnumerable<ICommand> GetCommands(ITextView textView, ITextBuffer textBuffer) {
            return new ICommand[] {
                new TemplateTypingCommandHandler(
                    textView, textBuffer,
                    _editorOptionsFactory.GetOptions(textView),
                    _editorOperationsFactory.GetEditorOperations(textView)),
                new TemplateCompletionCommandHandler(textView)
            };
        }
    }
}
