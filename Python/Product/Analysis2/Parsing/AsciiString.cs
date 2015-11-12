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
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.PythonTools.Analysis.Parsing {
    public sealed class AsciiString {
        private readonly IReadOnlyList<byte> _bytes;
        private string _str;

        public AsciiString(IReadOnlyList<byte> bytes, string str) {
            _bytes = bytes;
            _str = str;
        }

        public IReadOnlyList<byte> Bytes => _bytes;
        public string String => _str;
        public override string ToString() => String;

        public override bool Equals(object obj) => _str == (obj as AsciiString)?._str;
        public override int GetHashCode() => _str.GetHashCode();
    }
}
