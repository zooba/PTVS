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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Parsing;

namespace Microsoft.PythonTools.Analysis.Analyzer.Tasks {
    sealed class DocumentChanged : QueueItem {
        private readonly ISourceDocument _document;

        public DocumentChanged(AnalysisState item, ISourceDocument document)
            : base(item) {
            _document = document;
        }

        public override ThreadPriority Priority {
            get { return ThreadPriority.AboveNormal; }
        }

        public override async Task PerformAsync(CancellationToken cancellationToken) {
            _item.SetDocument(_document);

            var tokenization = await Tokenization.TokenizeAsync(
                _document,
                _item.Analyzer.Configuration.Version,
                cancellationToken
            );
            _item.SetTokenization(tokenization);

            _item.Analyzer.Enqueue(_item.Context, new UpdateVariables(_item));
        }
    }
}
