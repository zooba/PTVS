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

using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudioTools.MockVsTests;
using Microsoft.VisualStudioTools.Project;
using Microsoft.VisualStudioTools.VSTestHost;
using TestUtilities.SharedProject;

namespace TestUtilities.UI {
    public static class TestExtensions {
        public static IVisualStudioInstance ToVs(this SolutionFile self) {
            if (VSTestContext.IsMock) {
                return self.ToMockVs();
            }
            return new VisualStudioInstance(self);
        }

        public static string[] GetDisplayTexts(this ICompletionSession completionSession) {
            return completionSession.CompletionSets.First().Completions.Select(x => x.DisplayText).ToArray();
        }

        public static string[] GetInsertionTexts(this ICompletionSession completionSession) {
            return completionSession.CompletionSets.First().Completions.Select(x => x.InsertionText).ToArray();
        }

        public static bool GetIsFolderExpanded(this EnvDTE.Project project, string folder) {
            return GetNodeState(project, folder, __VSHIERARCHYITEMSTATE.HIS_Expanded);
        }

        public static bool GetIsItemBolded(this EnvDTE.Project project, string item) {
            return GetNodeState(project, item, __VSHIERARCHYITEMSTATE.HIS_Bold);
        }

        public static bool GetNodeState(this EnvDTE.Project project, string item, __VSHIERARCHYITEMSTATE state) {
            IVsHierarchy hier = null;
            uint id = 0;
            ThreadHelper.Generic.Invoke((Action)(() => {
                hier = ((dynamic)project).Project as IVsHierarchy;
                object projectDir;
                ErrorHandler.ThrowOnFailure(
                    hier.GetProperty(
                        (uint)VSConstants.VSITEMID.Root,
                        (int)__VSHPROPID.VSHPROPID_ProjectDir,
                        out projectDir
                    )
                );

                string itemPath = Path.Combine((string)projectDir, item);
                if (ErrorHandler.Failed(hier.ParseCanonicalName(itemPath, out id))) {
                    ErrorHandler.ThrowOnFailure(
                        hier.ParseCanonicalName(itemPath + "\\", out id)
                    );
                }
            }));

            // make sure we're still expanded.
            var solutionWindow = UIHierarchyUtilities.GetUIHierarchyWindow(
                VSTestContext.ServiceProvider,
                new Guid(ToolWindowGuids80.SolutionExplorer)
            );

            uint result;
            ErrorHandler.ThrowOnFailure(
                solutionWindow.GetItemState(
                    hier as IVsUIHierarchy,
                    id,
                    (uint)state,
                    out result
                )
            );
            return (result & (uint)state) != 0;
        }

    }
}
