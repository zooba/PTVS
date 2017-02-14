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

using System.Windows;
using Microsoft.VisualStudioTools;

namespace Microsoft.PythonTools.Refactoring {
    /// <summary>
    /// Interaction logic for ExtractMethodDialog.xaml
    /// </summary>
    internal partial class ExtractMethodDialog : DialogWindowVersioningWorkaround {
        private bool _firstActivation;

        public ExtractMethodDialog(ExtractMethodRequestView viewModel) {
            DataContext = viewModel;

            InitializeComponent();

            _firstActivation = true;
        }

        protected override void OnActivated(System.EventArgs e) {
            base.OnActivated(e);
            if (_firstActivation) {
                _methodName.Focus();
                _methodName.SelectAll();
                _firstActivation = false;
            }
        }

        private void OkClick(object sender, RoutedEventArgs e) {
            this.DialogResult = true;
            Close();
        }

        private void CancelClick(object sender, RoutedEventArgs e) {
            this.DialogResult = false;
            Close();
        }
    }
}
