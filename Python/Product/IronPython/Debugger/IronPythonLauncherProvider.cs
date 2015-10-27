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
using System.ComponentModel.Composition;
using Microsoft.PythonTools;
using Microsoft.PythonTools.Project;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.IronPythonTools.Debugger {
    [Export(typeof(IPythonLauncherProvider))]
    class IronPythonLauncherProvider : IPythonLauncherProvider {
        private readonly PythonToolsService _pyService;
        private readonly IServiceProvider _serviceProvider;

        [ImportingConstructor]
        public IronPythonLauncherProvider([Import(typeof(SVsServiceProvider))]IServiceProvider serviceProvider) {
            _serviceProvider = serviceProvider;
            _pyService = (PythonToolsService)serviceProvider.GetService(typeof(PythonToolsService));
        }

        #region IPythonLauncherProvider Members

        public IPythonLauncherOptions GetLauncherOptions(IPythonProject properties) {
            return new IronPythonLauncherOptions(properties);
        }

        public string Name {
            get { return "IronPython (.NET) launcher"; }
        }

        public string Description {
            get {
                return "Launches IronPython scripts using the .NET debugger.  This enables debugging both IronPython code as well as other .NET code such as C# or VB.NET.";
            }
        }

        public IProjectLauncher CreateLauncher(IPythonProject project) {
            return new IronPythonLauncher(_serviceProvider, _pyService, project);
        }

        #endregion
    }
}
