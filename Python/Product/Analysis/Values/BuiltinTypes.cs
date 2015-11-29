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

namespace Microsoft.PythonTools.Analysis.Values {
    static class BuiltinTypes {
        public static readonly TypeInfo Type = new TypeInfo("type");
        public static readonly TypeInfo Module = new TypeInfo("module");
        public static readonly TypeInfo Function = new TypeInfo("function");

        public static readonly TypeInfo NoneType = new TypeInfo("NoneType");
        public static readonly AnalysisValue None = NoneType.Instance;

        public static readonly TypeInfo Bool = new NumberInfo("bool");
        public static readonly TypeInfo Int = new NumberInfo("int");
        public static readonly TypeInfo Long = new NumberInfo("long");
        public static readonly TypeInfo Float = new NumberInfo("float");
        public static readonly TypeInfo Complex = new NumberInfo("complex");

        // We use the Python 3.x names internally, and the string
        // representations switch accordingly
        public static readonly TypeInfo String = new StringInfo(isUnicode: true);
        public static readonly TypeInfo Bytes = new StringInfo(isUnicode: false);
    }
}
