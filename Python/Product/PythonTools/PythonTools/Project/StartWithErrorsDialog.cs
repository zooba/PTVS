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
using System.Windows.Forms;
using System.Drawing;
using System;

namespace Microsoft.PythonTools.Project {
    public partial class StartWithErrorsDialog : Form {
        private readonly PythonToolsService _pyService;

        [Obsolete("Use constructor which provides a PythonToolsService instead")]
        public StartWithErrorsDialog()
            : this(PythonToolsPackage.Instance._pyService) {
        }

        public StartWithErrorsDialog(PythonToolsService pyService) {
            _pyService = pyService;
            InitializeComponent();
            _icon.Image = SystemIcons.Warning.ToBitmap();
        }

        [Obsolete("Use PythonToolsService.DebuggerOptions.PromptBeforeRunningWithBuildError instead")]
        public static bool ShouldShow {
            get {
                var pyService = (PythonToolsService)PythonToolsPackage.GetGlobalService(typeof(PythonToolsService));

                return pyService.DebuggerOptions.PromptBeforeRunningWithBuildError;
            }
        }

        protected override void OnClosing(CancelEventArgs e) {
            if (_dontShowAgainCheckbox.Checked) {
                _pyService.DebuggerOptions.PromptBeforeRunningWithBuildError = false;
                _pyService.DebuggerOptions.Save();
            }
        }

        private void YesButtonClick(object sender, System.EventArgs e) {
            DialogResult = System.Windows.Forms.DialogResult.Yes;
            Close();
        }

        private void NoButtonClick(object sender, System.EventArgs e) {
            this.DialogResult = System.Windows.Forms.DialogResult.No;
            Close();
        }

        internal PythonToolsService PythonService {
            get {
                return _pyService;
            }
        }
    }
}
