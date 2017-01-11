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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.PythonTools.Analyzer {
    [Export(typeof(IPythonAnalyzerService))]
    class PythonAnalyzerService : IPythonAnalyzerService {
        private readonly IServiceProvider _site;
        private readonly ConcurrentDictionary<string, IPythonAnalyzer> _analyzers;

        public PythonAnalyzerService([Import(typeof(SVsServiceProvider))] IServiceProvider site) {
            _site = site;
            _analyzers = new ConcurrentDictionary<string, IPythonAnalyzer>(StringComparer.OrdinalIgnoreCase);
        }

        private IPythonAnalyzer CreateAnalyzer(string moniker) {
            var ext = Path.GetExtension(moniker).ToLowerInvariant();
            switch (ext) {
                case ".pyproj":
                    return new PythonProjectAnalyzer(_site, moniker);
                default:
                    throw new NotSupportedException("moniker '{0}' is not supported".FormatInvariant(moniker));
            }
        }

        public Task<IPythonAnalyzer> GetOrCreateAnalyzerAsync(string moniker, PythonAnalyzerOptions options) {
            return Task.FromResult(_analyzers.GetOrAdd(moniker, CreateAnalyzer));
        }

        public Task<IPythonAnalyzer> TryGetAnalyzerAsync(string moniker) {
            IPythonAnalyzer analyzer;
            if (_analyzers.TryGetValue(moniker, out analyzer)) {
                return Task.FromResult(analyzer);
            }
            return Task.FromResult<IPythonAnalyzer>(null);
        }
    }
}
