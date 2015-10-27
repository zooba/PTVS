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
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.InteractiveWindow;
using Microsoft.VisualStudio.Shell;

namespace Microsoft.PythonTools.Repl {
    [Export(typeof(IInteractiveEvaluatorProvider))]
    class PythonDebugReplEvaluatorProvider : IInteractiveEvaluatorProvider {
        private const string _debugReplGuid = "BA417560-5A78-46F1-B065-638D27E1CDD0";
        private readonly PythonToolsService _pyService;
        private readonly IServiceProvider _serviceProvider;

        [ImportingConstructor]
        public PythonDebugReplEvaluatorProvider([Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider) {
            _serviceProvider = serviceProvider;
            _pyService = serviceProvider.GetPythonToolsService();
        }

        public IInteractiveEvaluator GetEvaluator(string replId) {
            if (replId.StartsWith(_debugReplGuid)) {
                return new PythonDebugReplEvaluator(_serviceProvider);
            }
            return null;
        }

        internal static string GetDebugReplId() {
            return _debugReplGuid;
        }
    }
}
