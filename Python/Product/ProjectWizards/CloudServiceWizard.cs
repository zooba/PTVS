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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TemplateWizard;
using Project = EnvDTE.Project;
using ProjectItem = EnvDTE.ProjectItem;

namespace Microsoft.PythonTools.ProjectWizards {
    public sealed class CloudServiceWizard : IWizard {
        private IWizard _wizard;

#if DEV14
        private readonly bool _recommendUpgrade;
        const string AzureToolsDownload = "http://go.microsoft.com/fwlink/?linkid=518003";
#elif DEV15_OR_LATER
#else
#error Unsupported VS version
#endif

        const string DontShowUpgradeDialogAgainProperty = "SuppressUpgradeAzureTools";

#if DEV14
        private static bool ShouldRecommendUpgrade(Assembly asm) {
            var attr = asm.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)
                .OfType<AssemblyFileVersionAttribute>()
                .FirstOrDefault();

            Version ver;
            if (attr != null && Version.TryParse(attr.Version, out ver)) {
                Debug.WriteLine(ver);
                // 2.4 is where we added integration, so we should recommend it
                // to people who don't have it.
                return ver < new Version(2, 4);
            }
            return false;
        }
#endif

        public CloudServiceWizard() {
            try {
                // If we fail to find the wizard, we will redirect the user to
                // the WebPI download.
                var asm = Assembly.Load("Microsoft.VisualStudio.CloudService.Wizard,Version=1.0.0.0,Culture=neutral,PublicKeyToken=b03f5f7f11d50a3a");

#if DEV14
                _recommendUpgrade = ShouldRecommendUpgrade(asm);
#endif

                var type = asm.GetType("Microsoft.VisualStudio.CloudService.Wizard.CloudServiceWizard");
                _wizard = type.InvokeMember(null, BindingFlags.CreateInstance, null, null, new object[0]) as IWizard;
            } catch (ArgumentException) {
            } catch (BadImageFormatException) {
            } catch (IOException) {
            } catch (MemberAccessException) {
            }
        }

        public void BeforeOpeningFile(ProjectItem projectItem) {
            if (_wizard != null) {
                _wizard.BeforeOpeningFile(projectItem);
            }
        }

        public void ProjectFinishedGenerating(Project project) {
            if (_wizard != null) {
                _wizard.ProjectFinishedGenerating(project);
            }
        }

        public void ProjectItemFinishedGenerating(ProjectItem projectItem) {
            if (_wizard != null) {
                _wizard.ProjectItemFinishedGenerating(projectItem);
            }
        }

        public void RunFinished() {
            if (_wizard != null) {
                _wizard.RunFinished();
            }
        }

        public bool ShouldAddProjectItem(string filePath) {
            if (_wizard != null) {
                return _wizard.ShouldAddProjectItem(filePath);
            }
            return false;
        }

#if DEV14
        private void StartDownload(IServiceProvider provider) {
            Process.Start(new ProcessStartInfo(AzureToolsDownload));
        }

        private void OfferUpgrade(IServiceProvider provider) {
            if (!_recommendUpgrade) {
                return;
            }

            var sm = SettingsManagerCreator.GetSettingsManager(provider);
            var store = sm.GetReadOnlySettingsStore(SettingsScope.UserSettings);

            if (!store.CollectionExists(PythonConstants.DontShowUpgradeDialogAgainCollection) ||
                !store.GetBoolean(PythonConstants.DontShowUpgradeDialogAgainCollection, DontShowUpgradeDialogAgainProperty, false)) {
                var dlg = new TaskDialog(provider) {
                    Title = Strings.ProductTitle,
                    MainInstruction = Strings.AzureToolsUpgradeRecommended,
                    Content = Strings.AzureToolsUpgradeInstructions,
                    AllowCancellation = true,
                    VerificationText = Strings.DontShowAgain
                };
                var download = new TaskDialogButton(Strings.DownloadAndInstall);
                dlg.Buttons.Add(download);
                var cont = new TaskDialogButton(Strings.ContinueWithoutAzureToolsUpgrade);
                dlg.Buttons.Add(cont);
                dlg.Buttons.Add(TaskDialogButton.Cancel);

                var response = dlg.ShowModal();

                if (dlg.SelectedVerified) {
                    var rwStore = sm.GetWritableSettingsStore(SettingsScope.UserSettings);
                    rwStore.CreateCollection(PythonConstants.DontShowUpgradeDialogAgainCollection);
                    rwStore.SetBoolean(PythonConstants.DontShowUpgradeDialogAgainCollection, DontShowUpgradeDialogAgainProperty, true);
                }

                if (response == download) {
                    Process.Start(new ProcessStartInfo(AzureToolsDownload));
                    throw new WizardCancelledException();
                } else if (response == TaskDialogButton.Cancel) {
                    // User cancelled, so go back to the New Project dialog
                    throw new WizardBackoutException();
                }
            }
        }
#else
        private void StartDownload(IServiceProvider provider) {
            var svc = (IVsTrackProjectRetargeting2)provider.GetService(typeof(SVsTrackProjectRetargeting));
            if (svc != null) {
                IVsProjectAcquisitionSetupDriver driver;
                if (ErrorHandler.Succeeded(svc.GetSetupDriver(VSConstants.SetupDrivers.SetupDriver_VS, out driver)) &&
                    driver != null) {
                    var task = driver.Install("Microsoft.VisualStudio.Component.Azure.Waverton");
                    if (task != null) {
                        task.Start();
                        throw new WizardCancelledException();
                    }
                }
            }
        }

        private void OfferUpgrade(IServiceProvider provider) {
        }
#endif

        public void RunStarted(object automationObject, Dictionary<string, string> replacementsDictionary, WizardRunKind runKind, object[] customParams) {
            var provider = WizardHelpers.GetProvider(automationObject);

            if (_wizard == null) {
                try {
                    Directory.Delete(replacementsDictionary["$destinationdirectory$"]);
                    Directory.Delete(replacementsDictionary["$solutiondirectory$"]);
                } catch {
                    // If it fails (doesn't exist/contains files/read-only), let the directory stay.
                }

                var dlg = new TaskDialog(provider) {
                    Title = Strings.ProductTitle,
                    MainInstruction = Strings.AzureToolsRequired,
                    Content = Strings.AzureToolsInstallInstructions,
                    AllowCancellation = true
                };
                dlg.Buttons.Add(TaskDialogButton.Cancel);
                var download = new TaskDialogButton(Strings.DownloadAndInstall);
                dlg.Buttons.Insert(0, download);

                if (dlg.ShowModal() == download) {
                    StartDownload(provider);
                    throw new WizardCancelledException();
                }

                // User cancelled, so go back to the New Project dialog
                throw new WizardBackoutException();
            }

            OfferUpgrade(provider);

            // Run the original wizard to get the right replacements
            _wizard.RunStarted(automationObject, replacementsDictionary, runKind, customParams);
        }
    }
}
