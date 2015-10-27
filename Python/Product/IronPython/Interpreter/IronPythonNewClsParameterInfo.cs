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
using System.Reflection;
using IronPython.Runtime;
using IronPython.Runtime.Operations;
using Microsoft.PythonTools.Interpreter;
using Microsoft.Scripting;
using Microsoft.Scripting.Generation;

namespace Microsoft.IronPythonTools.Interpreter {
    class IronPythonNewClsParameterInfo : IParameterInfo {
        private readonly IronPythonType _declaringType;

        public IronPythonNewClsParameterInfo(IronPythonType declaringType) {
            _declaringType = declaringType;
        }

        #region IParameterInfo Members

        public IList<IPythonType> ParameterTypes {
            get {
                return new[] { _declaringType };
            }
        }

        public string Documentation {
            get { return ""; }
        }

        public string Name {
            get {
                return "cls";
            }
        }

        public bool IsParamArray {
            get {
                return false;
            }
        }

        public bool IsKeywordDict {
            get {
                return false;
            }
        }

        public string DefaultValue {
            get {
                return null;
            }
        }

        #endregion
    }
}
