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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Common.Infrastructure;
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    [Export(typeof(PythonLanguageServiceProvider))]
    public sealed class PythonLanguageServiceProvider : IDisposable {
        private readonly PathSet<PythonLanguageService> _services;
        private readonly IReadOnlyCollection<IModuleProvider> _moduleProviders;
        private bool _isDisposed;

        [ImportingConstructor]
        public PythonLanguageServiceProvider(
            [ImportMany] IEnumerable<IModuleProvider> moduleProviders
        ) {
            _services = new PathSet<PythonLanguageService>(null);
            _moduleProviders = moduleProviders?.ToArray();
#if DEBUG
            TraceCapacity = 1000;
#else
            TraceCapacity = 100;
#endif
        }

        public IEnumerable<IModuleProvider> ModuleProviders => _moduleProviders.MaybeEnumerate();

        public int TraceCapacity { get; set; }

        public void Dispose() {
            if (_isDisposed) {
                return;
            }
            _isDisposed = true;

            lock (_services) {
                var toDispose = _services.GetValues().ToArray();
                foreach (var v in toDispose) {
                    v.Dispose();
                }
            }
        }

        public async Task<PythonLanguageService> GetServiceAsync(
            InterpreterConfiguration config,
            PythonFileContextProvider fileContextProvider,
            CancellationToken cancellationToken
        ) {
            if (_isDisposed) {
                return null;
            }

            lock (_services) {
                PythonLanguageService service;
                if (!_services.TryGetValue(config.InterpreterPath, out service) || !service.AddReference()) {
                    service = new PythonLanguageService(this, fileContextProvider, config);
                    _services.Add(config.InterpreterPath, service);
                }
                return service;
            }
        }

        internal async Task RemoveAsync(PythonLanguageService service, CancellationToken cancellationToken) {
            if (_isDisposed) {
                return;
            }

            lock (_services) {
                _services.Remove(service.Configuration.InterpreterPath);
            }
        }
    }
}
