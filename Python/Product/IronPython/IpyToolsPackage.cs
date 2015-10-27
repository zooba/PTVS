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

using System.ComponentModel;
using Microsoft.IronPythonTools.Interpreter;
using Microsoft.PythonTools;
using Microsoft.VisualStudio.Shell;

namespace Microsoft.IronPythonTools {
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [Description("Python Tools IronPython Interpreter")]
    [ProvidePythonInterpreterFactoryProvider("{80659AB7-4D53-4E0C-8588-A766116CBD46}", typeof(IronPythonInterpreterFactoryProvider))]
    [ProvidePythonInterpreterFactoryProvider("{FCC291AA-427C-498C-A4D7-4502D6449B8C}", typeof(IronPythonInterpreterFactoryProvider))]
    class IpyToolsPackage : Package {
        public IpyToolsPackage() {
        }
    }
}
