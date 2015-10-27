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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.InterpreterList;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudioTools;

namespace Microsoft.PythonTools.Project {
    sealed class AddInterpreterView : DependencyObject, INotifyPropertyChanged, IDisposable {
        readonly IInterpreterOptionsService _interpreterService;
        
        public AddInterpreterView(
            IServiceProvider serviceProvider,
            IInterpreterOptionsService interpreterService,
            IEnumerable<IPythonInterpreterFactory> selected
        ) {
            _interpreterService = interpreterService;
            Interpreters = new ObservableCollection<InterpreterView>(InterpreterView.GetInterpreters(serviceProvider, interpreterService));
            
            var map = new Dictionary<IPythonInterpreterFactory, InterpreterView>();
            foreach (var view in Interpreters) {
                map[view.Interpreter] = view;
                view.IsSelected = false;
            }

            foreach (var interp in selected) {
                InterpreterView view;
                if (map.TryGetValue(interp, out view)) {
                    view.IsSelected = true;
                } else {
                    view = new InterpreterView(interp, interp.Description, false);
                    view.IsSelected = true;
                    Interpreters.Add(view);
                }
            }

            _interpreterService.InterpretersChanged += OnInterpretersChanged;
        }

        public void Dispose() {
            if (_interpreterService != null) {
                _interpreterService.InterpretersChanged -= OnInterpretersChanged;
            }
        }

        private void OnInterpretersChanged(object sender, EventArgs e) {
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.BeginInvoke((Action)(() => OnInterpretersChanged(sender, e)));
                return;
            }
            var def = _interpreterService.DefaultInterpreter;
            Interpreters.Merge(
                _interpreterService.Interpreters.Select(i => new InterpreterView(i, i.Description, i == def)),
                InterpreterView.EqualityComparer,
                InterpreterView.Comparer
            );
        }

        public ObservableCollection<InterpreterView> Interpreters {
            get { return (ObservableCollection<InterpreterView>)GetValue(InterpretersProperty); }
            private set { SetValue(InterpretersPropertyKey, value); }
        }

        private static readonly DependencyPropertyKey InterpretersPropertyKey = DependencyProperty.RegisterReadOnly("Interpreters", typeof(ObservableCollection<InterpreterView>), typeof(AddInterpreterView), new PropertyMetadata());
        public static readonly DependencyProperty InterpretersProperty = InterpretersPropertyKey.DependencyProperty;


        public event PropertyChangedEventHandler PropertyChanged;

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) {
            base.OnPropertyChanged(e);

            var evt = PropertyChanged;
            if (evt != null) {
                evt(this, new PropertyChangedEventArgs(e.Property.Name));
            }
        }
    }
}
