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

using System.Collections.Generic;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Django.Project;
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Django.TemplateParsing {
    /// <summary>
    /// Provides context for returning the available variables/filters in a template file.
    /// 
    /// This is implemented as an interface so we can mock it out for the purposes of our tests
    /// and not need to do a fully analysis of the Django library.
    /// </summary>
    interface IDjangoCompletionContext {
        Dictionary<string, HashSet<AnalysisValue>> Variables {
            get;
        }

        Dictionary<string, TagInfo> Filters {
            get;
        }

        IModuleContext ModuleContext {
            get;
        }
    }
}
