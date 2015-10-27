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

namespace Microsoft.PythonTools.Parsing {
    /// <summary>
    /// Provides binary examples for a code formatting option of how it affects the code
    /// when the option is turned on or off.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple=false)]
    public sealed class CodeFormattingExampleAttribute : Attribute {
        private readonly string _on, _off;

        internal CodeFormattingExampleAttribute(string doc) {
            _on = _off = doc;
        }

        internal CodeFormattingExampleAttribute(string on, string off) {
            _on = on;
            _off = off;
        }

        public string On {
            get {
                return _on;
            }
        }

        public string Off {
            get {
                return _off;
            }
        }
    }
}
