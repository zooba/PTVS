﻿// Python Tools for Visual Studio
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

using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.CookiecutterTools.Infrastructure;
using Microsoft.CookiecutterTools.Model;
using Microsoft.CookiecutterTools.ViewModel;
using WpfCommands = Microsoft.VisualStudioTools.Wpf.Commands;

namespace Microsoft.CookiecutterTools.View {
    /// <summary>
    /// Interaction logic for CookiecutterOptionsPage.xaml
    /// </summary>
    internal partial class CookiecutterOptionsPage : Page {
        public CookiecutterOptionsPage() {
            InitializeComponent();
        }

        private CookiecutterViewModel ViewModel => (CookiecutterViewModel)DataContext;

        private void Browse_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            WpfCommands.CanExecute(null, sender, e);
        }

        private void Browse_Executed(object sender, ExecutedRoutedEventArgs e) {
            WpfCommands.Executed(null, sender, e);
        }

        private void Odbc_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            SqlCommands.CanExecute(null, sender, e);
        }

        private void Odbc_Executed(object sender, ExecutedRoutedEventArgs e) {
            SqlCommands.Executed(null, sender, e);
        }

        private void Home_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = ViewModel != null && ViewModel.CreatingStatus != OperationStatus.InProgress;
            e.Handled = true;
        }

        private void Home_Executed(object sender, ExecutedRoutedEventArgs e) {
            ViewModel.Home();
        }

        private void CreateFiles_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = ViewModel?.CanRunSelectedTemplate == true;
            e.Handled = true;
        }

        private void CreateFiles_Executed(object sender, ExecutedRoutedEventArgs e) {
            if (!PathUtils.IsValidPath(ViewModel.OutputFolderPath)) {
                MessageBox.Show(Strings.InvalidOutputFolder, Strings.ProductTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!ViewModel.IsOutputFolderEmpty()) {
                var result = MessageBox.Show(Strings.OutputFolderNotEmpty, Strings.ProductTitle, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Cancel) {
                    return;
                }
            }

            ViewModel.CreateFilesAsync().DoNotWait();
        }
    }

    class TemplateContextItemTemplateSelector : DataTemplateSelector {
        public override DataTemplate SelectTemplate(object item, DependencyObject container) {
            var element = container as FrameworkElement;
            var p = item as ContextItemViewModel;
            if (element != null && p != null) {
                string selector = p.Selector?.ToLowerInvariant()?.Truncate(30);
                if (selector == null || !Regex.IsMatch(selector, "^[a-z]+$")) {
                    selector = Selectors.String;
                }

                try {
                    return element.FindResource(selector + "Template") as DataTemplate;
                } catch (ResourceReferenceKeyNotFoundException) {
                    return element.FindResource("stringTemplate") as DataTemplate;
                }
            }
            return base.SelectTemplate(item, container);
        }
    }
}
