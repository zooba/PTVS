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
using Microsoft.PythonTools.Infrastructure;
using TestUtilities.SharedProject;
using MSBuild = Microsoft.Build.Evaluation;

namespace PythonToolsUITests {
    [Export(typeof(IProjectProcessor))]
    [ProjectExtension(".pyproj")]
    public class PythonProjectProcessor : IProjectProcessor {
        public void PreProcess(MSBuild.Project project) {
            project.SetProperty("ProjectHome", ".");
            project.SetProperty("WorkingDirectory", ".");

            var installPath = PathUtils.GetParent(PythonToolsInstallPath.GetFile("Microsoft.PythonTools.dll", typeof(PythonToolsPackage).Assembly));
            project.SetProperty("_PythonToolsPath", installPath);
            project.Xml.AddImport(Microsoft.PythonTools.Project.PythonProjectFactory.PtvsTargets);
        }

        public void PostProcess(MSBuild.Project project) {
        }
    }
}
