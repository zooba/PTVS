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
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    [Export(typeof(PythonLanguageServiceProvider))]
    public sealed class PythonLanguageServiceProvider : IDisposable {
        private readonly PathSet<PythonLanguageService> _services;
        private readonly SemaphoreSlim _servicesLock = new SemaphoreSlim(1, 1);

        public PythonLanguageServiceProvider() {
            _services = new PathSet<PythonLanguageService>(null);
        }

        public void Dispose() {
            _servicesLock.Wait();
            foreach (var v in _services.GetValues()) {
                v.Dispose();
            }
        }

        public async Task<PythonLanguageService> GetServiceAsync(
            InterpreterConfiguration config,
            PythonFileContextProvider fileContextProvider,
            CancellationToken cancellationToken
        ) {
            await _servicesLock.WaitAsync(cancellationToken);
            try {
                PythonLanguageService service;
                if (!_services.TryGetValue(config.InterpreterPath, out service)) {
                    service = new PythonLanguageService(config);
                    if (fileContextProvider != null) {
                        var contexts = await fileContextProvider.GetContextsForInterpreterAsync(
                            config,
                            null,
                            cancellationToken
                        );
                        foreach (var context in contexts) {
                            await service.AddFileContextAsync(context, cancellationToken);
                        }
                    }
                    _services.Add(config.InterpreterPath, service);
                } else {
                    service.AddReference();
                }
                return service;
            } finally {
                _servicesLock.Release();
            }
        }
    }
}
