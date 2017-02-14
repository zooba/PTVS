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

using System.Runtime.InteropServices;
using Microsoft.PythonTools.Parsing;

namespace Microsoft.PythonTools.Options {
    [ComVisible(true)]
    public class PythonFormattingSpacingOptionsPage : PythonFormattingOptionsPage {
        public PythonFormattingSpacingOptionsPage()
            : base( 
            new OptionCategory(Strings.FormattingOptionsCategoryClassDefinitions, OptionCategory.GetOptions(CodeFormattingCategory.Classes)),
            new OptionCategory(Strings.FormattingOptionsCategoryFunctionDefinitions, OptionCategory.GetOptions(CodeFormattingCategory.Functions)),
            new OptionCategory(Strings.FormattingOptionsCategoryOperators, OptionCategory.GetOptions(CodeFormattingCategory.Operators)),
            new OptionCategory(Strings.FormattingOptionsCategoryExpressionSpacing, OptionCategory.GetOptions(CodeFormattingCategory.Spacing))
            )
        
        {
        }
    }
}