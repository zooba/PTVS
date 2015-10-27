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
using System.Windows.Automation;

namespace TestUtilities.UI {
    public class AzureWebSitePublishDialog : AutomationDialog {
        public AzureWebSitePublishDialog(VisualStudioApp app, AutomationElement element)
            : base(app, element) {
        }

        public static AzureWebSitePublishDialog FromDte(VisualStudioApp app) {
            var publishDialogHandle = app.OpenDialogWithDteExecuteCommand("Build.PublishSelection");
            return new AzureWebSitePublishDialog(app, AutomationElement.FromHandle(publishDialogHandle));
        }

        public AzureWebSiteImportPublishSettingsDialog ClickImportSettings() {
            WaitForInputIdle();
            ClickButtonByAutomationId("ImportSettings");
            return new AzureWebSiteImportPublishSettingsDialog(App, AutomationElement.FromHandle(App.WaitForDialogToReplace(Element)));
        }

        public void ClickPublish() {
            WaitForInputIdle();
            WaitForClosed(TimeSpan.FromSeconds(10.0), () => ClickButtonByAutomationId("PublishButton"));
        }
    }
}
