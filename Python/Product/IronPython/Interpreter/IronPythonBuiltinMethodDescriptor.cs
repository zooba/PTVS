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

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.IronPythonTools.Interpreter {
    class IronPythonBuiltinMethodDescriptor : PythonObject, IPythonMethodDescriptor {
        private IPythonFunction _function;

        public IronPythonBuiltinMethodDescriptor(IronPythonInterpreter interpreter, ObjectIdentityHandle desc)
            : base(interpreter, desc) {
        }

        #region IBuiltinMethodDescriptor Members

        public IPythonFunction Function {
            get {
                if (_function == null) {
                    var ri = RemoteInterpreter;
                    if (ri != null) {
                        var func = ri.GetBuiltinMethodDescriptorTemplate(Value);

                        _function = (IPythonFunction)Interpreter.MakeObject(func);
                    }
                }
                return _function;
            }
        }

        public bool IsBound {
            get {
                return false;
            }
        }

        #endregion

        #region IMember Members

        public override PythonMemberType MemberType {
            get { return PythonMemberType.Method; }
        }

        #endregion
    }
}
