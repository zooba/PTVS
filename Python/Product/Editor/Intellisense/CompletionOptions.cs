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

namespace Microsoft.PythonTools.Editor.Intellisense {
    public class CompletionOptions {
        /// <summary>
        /// Only show completions for members belonging to all potential types
        /// of the variable.
        /// </summary>
        public bool IntersectMembers { get; set; }

        /// <summary>
        /// Omit completions for advanced members.
        /// </summary>
        public bool HideAdvancedMembers { get; set; }

        /// <summary>
        /// Show context-sensitive completions for statement keywords.
        /// </summary>
        public bool IncludeStatementKeywords { get; set; }


        /// <summary>
        /// Show context-sensitive completions for expression keywords.
        /// </summary>
        public bool IncludeExpressionKeywords { get; set; }

        /// <summary>
        /// Convert Tab characters to TabSize spaces.
        /// </summary>
        public bool ConvertTabsToSpaces { get; set; }

        /// <summary>
        /// The number of spaces each Tab character occupies.
        /// </summary>
        public int TabSize { get; set; }

        /// <summary>
        /// The number of spaces added for each level of indentation.
        /// </summary>
        public int IndentSize { get; set; }

        /// <summary>
        /// True to filter completions to those similar to the search string.
        /// </summary>
        public bool FilterCompletions { get; set; }

        /// <summary>
        /// The search mode to use for completions.
        /// </summary>
        public FuzzyMatchMode SearchMode { get; set; }

        public CompletionOptions() {
            IncludeStatementKeywords = true;
            IncludeExpressionKeywords = true;
            HideAdvancedMembers = true;
            FilterCompletions = true;
            SearchMode = FuzzyMatchMode.Default;
        }

        /// <summary>
        /// Returns a new instance of this CompletionOptions that cannot be modified
        /// by the code that provided the original.
        /// </summary>
        public CompletionOptions Clone() {
            return (CompletionOptions)MemberwiseClone();
        }

    }
}
