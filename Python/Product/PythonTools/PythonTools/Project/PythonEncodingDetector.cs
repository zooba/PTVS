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

using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using Microsoft.PythonTools.Parsing;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.PythonTools.Project {
    [ContentType(PythonCoreConstants.ContentType)]
    [Export(typeof(IEncodingDetector))]
    [Order(Before = "XmlEncodingDetector")]
    [Name("PythonEncodingDetector")]
    class PythonEncodingDetector : IEncodingDetector {
        public Encoding GetStreamEncoding(Stream stream) {
            var res = Parser.GetEncodingFromStream(stream) ?? Parser.DefaultEncodingNoFallback;
            if (res == Parser.DefaultEncoding) {
                // return a version of the fallback buffer that doesn't throw exceptions, VS will detect the failure, and inform
                // the user of the problem.
                return Parser.DefaultEncodingNoFallback;
            }
            return res;
        }
    }
}
