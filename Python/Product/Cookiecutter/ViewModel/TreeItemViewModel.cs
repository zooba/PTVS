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

using System.ComponentModel;

namespace Microsoft.CookiecutterTools.ViewModel {
    class TreeItemViewModel : INotifyPropertyChanged {
        private bool _isSelected;

        /// <summary>
        /// Constructor for design view.
        /// </summary>
        public TreeItemViewModel() {
        }

        public bool IsSelected {
            get {
                return _isSelected;
            }

            set {
                if (value != _isSelected) {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public virtual string AutomationName => ToString();
        public virtual string AutomationHelpText => null;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(PropertyChangedEventArgs ea) {
            PropertyChanged?.Invoke(this, ea);
        }
    }
}
