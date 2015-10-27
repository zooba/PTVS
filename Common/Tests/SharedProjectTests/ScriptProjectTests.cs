﻿// Visual Studio Shared Project
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

using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestUtilities;
using TestUtilities.SharedProject;
using TestUtilities.UI;

namespace Microsoft.VisualStudioTools.SharedProjectTests {
    /// <summary>
    /// Test cases which are applicable to projects designed for scripting languages.
    /// </summary>
    [TestClass]
    public class ScriptProjectTests : SharedProjectTest {
        [TestMethod, Priority(0), TestCategory("Core")]
        [HostType("VSTestHost")]
        public void RunWithoutStartupFile() {
            foreach (var projectType in ProjectTypes) {
                var testDef = new ProjectDefinition("RunWithoutStartupFile", projectType);

                using (var solution = testDef.Generate().ToVs()) {
                    solution.OpenDialogWithDteExecuteCommand("Debug.Start");
                    solution.CheckMessageBox("startup file");

                    solution.OpenDialogWithDteExecuteCommand("Debug.StartWithoutDebugging");
                    solution.CheckMessageBox("startup file");
                }
            }
        }

        /// <summary>
        /// Renaming the folder containing the startup script should update the startup script
        /// https://nodejstools.codeplex.com/workitem/476
        /// </summary>
        [TestMethod, Priority(0), TestCategory("Core")]
        [HostType("VSTestHost")]
        public void RenameStartupFileFolder() {
            foreach (var projectType in ProjectTypes) {
                var testDef = new ProjectDefinition(
                    "RenameStartupFileFolder", 
                    projectType,
                    Folder("Folder"),
                    Compile("Folder\\server"),
                    Property("StartupFile", "Folder\\server" + projectType.CodeExtension)
                );

                using (var solution = testDef.Generate().ToVs()) {
                    var folder = solution.GetProject("RenameStartupFileFolder").ProjectItems.Item("Folder");
                    folder.Name = "FolderNew";

                    string startupFile = (string)solution.GetProject("RenameStartupFileFolder").Properties.Item("StartupFile").Value;
                    Assert.IsTrue(
                        startupFile.EndsWith(projectType.Code("FolderNew\\server")),
                        "Expected FolderNew in path, got {0}",
                        startupFile
                    );
                }
            }
        }

        [TestMethod, Priority(0), TestCategory("Core")]
        [HostType("VSTestHost")]
        public void RenameStartupFile() {
            foreach (var projectType in ProjectTypes) {
                var testDef = new ProjectDefinition(
                    "RenameStartupFileFolder",
                    projectType,
                    Folder("Folder"),
                    Compile("Folder\\server"),
                    Property("StartupFile", "Folder\\server" + projectType.CodeExtension)
                );

                using (var solution = testDef.Generate().ToVs()) {
                    var file = solution.GetProject("RenameStartupFileFolder").ProjectItems.Item("Folder").ProjectItems.Item("server" + projectType.CodeExtension);
                    file.Name = "server2" + projectType.CodeExtension;

                    Assert.AreEqual(
                        "server2" + projectType.CodeExtension,
                        Path.GetFileName(
                            (string)solution.GetProject("RenameStartupFileFolder").Properties.Item("StartupFile").Value
                        )
                    );
                }
            }
        }
    }
}
