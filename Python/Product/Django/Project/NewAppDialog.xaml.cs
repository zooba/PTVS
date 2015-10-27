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
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Microsoft.PythonTools.Django.Project {
    /// <summary>
    /// Interaction logic for NewAppDialog.xaml
    /// </summary>
    partial class NewAppDialog : DialogWindowVersioningWorkaround {
        private readonly NewAppDialogViewModel _viewModel;

        public static readonly object BackgroundKey = VsBrushes.WindowKey;
        public static readonly object ForegroundKey = VsBrushes.WindowTextKey;
        
        public NewAppDialog() {
            InitializeComponent();

            DataContext = _viewModel = new NewAppDialogViewModel();

            _newAppName.Focus();
        }

        internal NewAppDialogViewModel ViewModel {
            get {
                return _viewModel;
            }
        }

        private void _ok_Click(object sender, RoutedEventArgs e) {
            DialogResult = true;
            Close();
        }

        private void _cancel_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }
    }
}
