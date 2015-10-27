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
using System.Linq;
using Microsoft.PythonTools.Interpreter;
using Microsoft.Scripting.Actions;

namespace Microsoft.IronPythonTools.Interpreter {
    class IronPythonNamespace : PythonObject, IPythonModule {
        public IronPythonNamespace(IronPythonInterpreter interpreter, ObjectIdentityHandle ns)
            : base(interpreter, ns) {
        }

        #region IPythonModule Members

        public string Name {
            get {
                var ri = RemoteInterpreter;
                return ri != null ? ri.GetNamespaceName(Value) : string.Empty;
            }
        }

        public void Imported(IModuleContext context) {
            ((IronPythonModuleContext)context).ShowClr = true;
        }

        public IEnumerable<string> GetChildrenModules() {
            var ri = RemoteInterpreter;
            return ri != null ? ri.GetNamespaceChildren(Value) : Enumerable.Empty<string>();
        }

        public string Documentation {
            get { return string.Empty; }
        }

        #endregion
    }
}
