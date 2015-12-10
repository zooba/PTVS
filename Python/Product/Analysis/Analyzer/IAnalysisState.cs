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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Analysis.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    public interface IAnalysisState {
        PythonFileContext Context { get; }
        ISourceDocument Document { get; }
        long Version { get; }

        LanguageFeatures Features { get; }

        Task DumpAsync(TextWriter output, CancellationToken cancellationToken);

        Task WaitForUpToDateAsync(CancellationToken cancellationToken);

        Tokenization TryGetTokenization();
        Task<Tokenization> GetTokenizationAsync(CancellationToken cancellationToken);
        Task<PythonAst> GetAstAsync(CancellationToken cancellationToken);
        Task<IReadOnlyCollection<string>> GetVariablesAsync(CancellationToken cancellationToken);
        Task<IAnalysisSet> GetTypesAsync(string name, CancellationToken cancellationToken);
        Task<string> GetFullNameAsync(string name, SourceLocation location, CancellationToken cancellationToken);

        Task<bool> ReportErrorAsync(
            string code,
            string text,
            SourceLocation location,
            CancellationToken cancellationToken
        );
    }
}
