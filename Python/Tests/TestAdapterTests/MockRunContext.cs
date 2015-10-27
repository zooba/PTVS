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
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace TestAdapterTests {
    class MockRunContext : IRunContext {
        public ITestCaseFilterExpression GetTestCaseFilter(IEnumerable<string> supportedProperties, Func<string, TestProperty> propertyProvider) {
            throw new NotImplementedException();
        }

        public bool InIsolation {
            get { throw new NotImplementedException(); }
        }

        public bool IsBeingDebugged {
            get { return false; }
        }

        public bool IsDataCollectionEnabled {
            get { throw new NotImplementedException(); }
        }

        public bool KeepAlive {
            get { throw new NotImplementedException(); }
        }

        public string SolutionDirectory {
            get { throw new NotImplementedException(); }
        }

        public string TestRunDirectory {
            get { throw new NotImplementedException(); }
        }

        public IRunSettings RunSettings {
            get { throw new NotImplementedException(); }
        }
    }
}
