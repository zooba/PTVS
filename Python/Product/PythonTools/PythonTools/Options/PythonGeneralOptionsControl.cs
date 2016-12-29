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
using System.Windows.Forms;
using Microsoft.PythonTools.Parsing;

namespace Microsoft.PythonTools.Options {
    public partial class PythonGeneralOptionsControl : UserControl {
        private const int ErrorIndex = 0;
        private const int WarningIndex = 1;
        private const int DontIndex = 2;

        private const int SurveyNewsNeverIndex = 0;
        private const int SurveyNewsOnceDayIndex = 1;
        private const int SurveyNewsOnceWeekIndex = 2;
        private const int SurveyNewsOnceMonthIndex = 3;

        public PythonGeneralOptionsControl() {
            InitializeComponent();
        }

        internal Severity IndentationInconsistencySeverity {
            get {
                switch (_indentationInconsistentCombo.SelectedIndex) {
                    case ErrorIndex: 
                        return Severity.Error;
                    case WarningIndex: 
                        return Severity.Warning;
                    case DontIndex: 
                        return Severity.Ignore;
                    default:
                        return Severity.Ignore;
                }
            }
            set {
                switch (value) {
                    case Severity.Error: 
                        _indentationInconsistentCombo.SelectedIndex = ErrorIndex; 
                        break;
                    case Severity.Warning: 
                        _indentationInconsistentCombo.SelectedIndex = WarningIndex; 
                        break;
                    default: 
                        _indentationInconsistentCombo.SelectedIndex = DontIndex; 
                        break;
                }
            }
        }

        internal SurveyNewsPolicy SurveyNewsCheck {
            get {
                switch (_surveyNewsCheckCombo.SelectedIndex) {
                    case SurveyNewsNeverIndex: 
                        return SurveyNewsPolicy.Disabled; 
                    case SurveyNewsOnceDayIndex: 
                        return SurveyNewsPolicy.CheckOnceDay; 
                    case SurveyNewsOnceWeekIndex: 
                        return SurveyNewsPolicy.CheckOnceWeek; 
                    case SurveyNewsOnceMonthIndex: 
                        return SurveyNewsPolicy.CheckOnceMonth; 
                    default:
                        return SurveyNewsPolicy.Disabled;
                }
            }
            set {
                switch (value) {
                    case SurveyNewsPolicy.Disabled: 
                        _surveyNewsCheckCombo.SelectedIndex = SurveyNewsNeverIndex; 
                        break;
                    case SurveyNewsPolicy.CheckOnceDay: 
                        _surveyNewsCheckCombo.SelectedIndex = SurveyNewsOnceDayIndex; 
                        break;
                    case SurveyNewsPolicy.CheckOnceWeek: 
                        _surveyNewsCheckCombo.SelectedIndex = SurveyNewsOnceWeekIndex; 
                        break;
                    case SurveyNewsPolicy.CheckOnceMonth: 
                        _surveyNewsCheckCombo.SelectedIndex = SurveyNewsOnceMonthIndex; 
                        break;
                }
            }
        }

        internal void SyncControlWithPageSettings(PythonToolsService pyService) {
            _showOutputWindowForVirtualEnvCreate.Checked = pyService.GeneralOptions.ShowOutputWindowForVirtualEnvCreate;
            _showOutputWindowForPackageInstallation.Checked = pyService.GeneralOptions.ShowOutputWindowForPackageInstallation;
            _elevatePip.Checked = pyService.GeneralOptions.ElevatePip;
            _elevateEasyInstall.Checked = pyService.GeneralOptions.ElevateEasyInstall;
            _autoAnalysis.Checked = pyService.GeneralOptions.AutoAnalyzeStandardLibrary;
            _updateSearchPathsForLinkedFiles.Checked = pyService.GeneralOptions.UpdateSearchPathsWhenAddingLinkedFiles;
            _unresolvedImportWarning.Checked = pyService.GeneralOptions.UnresolvedImportWarning;
            _clearGlobalPythonPath.Checked = pyService.GeneralOptions.ClearGlobalPythonPath;
            IndentationInconsistencySeverity = pyService.GeneralOptions.IndentationInconsistencySeverity;
            SurveyNewsCheck = pyService.GeneralOptions.SurveyNewsCheck;
        }

        internal void SyncPageWithControlSettings(PythonToolsService pyService) {
            pyService.GeneralOptions.ShowOutputWindowForVirtualEnvCreate = _showOutputWindowForVirtualEnvCreate.Checked;
            pyService.GeneralOptions.ShowOutputWindowForPackageInstallation = _showOutputWindowForPackageInstallation.Checked;
            pyService.GeneralOptions.ElevatePip = _elevatePip.Checked;
            pyService.GeneralOptions.ElevateEasyInstall = _elevateEasyInstall.Checked;
            pyService.GeneralOptions.AutoAnalyzeStandardLibrary = _autoAnalysis.Checked;
            pyService.GeneralOptions.UpdateSearchPathsWhenAddingLinkedFiles = _updateSearchPathsForLinkedFiles.Checked;
            pyService.GeneralOptions.IndentationInconsistencySeverity = IndentationInconsistencySeverity;
            pyService.GeneralOptions.SurveyNewsCheck = SurveyNewsCheck;
            pyService.GeneralOptions.UnresolvedImportWarning = _unresolvedImportWarning.Checked;
            pyService.GeneralOptions.ClearGlobalPythonPath = _clearGlobalPythonPath.Checked;
        }

        private void _resetSuppressDialog_Click(object sender, EventArgs e) {
            System.Diagnostics.Debug.Assert(ResetSuppressDialog != null, "No listener for ResetSuppressDialog event");
            ResetSuppressDialog?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler ResetSuppressDialog;
    }
}
