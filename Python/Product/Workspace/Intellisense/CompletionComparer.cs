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
using Microsoft.PythonTools.Analysis;
using Microsoft.VisualStudio.Language.Intellisense;

namespace Microsoft.PythonTools.Intellisense {
    /// <summary>
    /// Compares various types of completions.
    /// </summary>
    public class CompletionComparer : IEqualityComparer<CompletionResult>, IComparer<CompletionResult>, IComparer<Completion>, IComparer<string> {
        /// <summary>
        /// A CompletionComparer that sorts names beginning with underscores to
        /// the end of the list.
        /// </summary>
        public static readonly CompletionComparer UnderscoresLast = new CompletionComparer(true);
        /// <summary>
        /// A CompletionComparer that determines whether
        /// <see cref="MemberResult" /> structures are equal.
        /// </summary>
        public static readonly IEqualityComparer<CompletionResult> MemberEquality = UnderscoresLast;
        /// <summary>
        /// A CompletionComparer that sorts names beginning with underscores to
        /// the start of the list.
        /// </summary>
        public static readonly CompletionComparer UnderscoresFirst = new CompletionComparer(false);

        bool _sortUnderscoresLast;

        /// <summary>
        /// Compares two strings.
        /// </summary>
        public int Compare(string xName, string yName) {
            if (yName == null) {
                return xName == null ? 0 : -1;
            } else if (xName == null) {
                return yName == null ? 0 : 1;
            }

            if (_sortUnderscoresLast) {
                bool xUnder = xName.StartsWith("__") && xName.EndsWith("__");
                bool yUnder = yName.StartsWith("__") && yName.EndsWith("__");

                if (xUnder != yUnder) {
                    // The one that starts with an underscore comes later
                    return xUnder ? 1 : -1;
                }

                bool xSingleUnder = xName.StartsWith("_");
                bool ySingleUnder = yName.StartsWith("_");
                if (xSingleUnder != ySingleUnder) {
                    // The one that starts with an underscore comes later
                    return xSingleUnder ? 1 : -1;
                }
            }
            return String.Compare(xName, yName, StringComparison.CurrentCultureIgnoreCase);
        }

        private CompletionComparer(bool sortUnderscoresLast) {
            _sortUnderscoresLast = sortUnderscoresLast;
        }

        /// <summary>
        /// Compares two instances of <see cref="Completion"/> using their
        /// displayed text.
        /// </summary>
        public int Compare(Completion x, Completion y) {
            return Compare(x.DisplayText, y.DisplayText);
        }

        /// <summary>
        /// Compares two <see cref="MemberResult"/> structures using their
        /// names.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public int Compare(CompletionResult x, CompletionResult y) {
            return Compare(x.Name, y.Name);
        }

        /// <summary>
        /// Compares two <see cref="MemberResult"/> structures for equality.
        /// </summary>
        public bool Equals(CompletionResult x, CompletionResult y) {
            return x.Name.Equals(y.Name);
        }

        /// <summary>
        /// Gets the hash code for a <see cref="MemberResult"/> structure.
        /// </summary>
        public int GetHashCode(CompletionResult obj) {
            return obj.Name.GetHashCode();
        }
    }
}
