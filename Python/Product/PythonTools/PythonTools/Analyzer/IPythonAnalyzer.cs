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
using System.Threading.Tasks;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Parsing;

namespace Microsoft.PythonTools.Analyzer {
    public interface IPythonAnalyzer : IDisposable {
        string Moniker { get; }

        PythonLanguageVersion LanguageVersion { get; }
        IPythonInterpreterFactory CurrentInterpreter { get; }
        void SetInterpreter(IPythonInterpreterFactory factory);
        event EventHandler CurrentInterpreterChanged;

        Task AddFileAsync(string moniker);
        Task AddFilesAsync(IReadOnlyList<string> monikers);
        Task RenameFileAsync(string oldMoniker, string newMoniker);
        Task ForgetFileAsync(string moniker);

        Task<IPythonFileView> GetFileViewAsync(string moniker);

        Task ResetAllAsync();

        event EventHandler AnalysisStarted;
        event EventHandler AnalysisCompleted;
    }
}
