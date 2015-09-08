﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    [Export(typeof(PythonLanguageServiceProvider))]
    public sealed class PythonLanguageServiceProvider {
        private readonly PathSet<PythonLanguageService> _services;
        private readonly SemaphoreSlim _servicesLock = new SemaphoreSlim(1, 1);

        public PythonLanguageServiceProvider() {
            _services = new PathSet<PythonLanguageService>(null);
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
                        await fileContextProvider.FindContextsAsync(config.PrefixPath, null, cancellationToken);
                        foreach (var context in await fileContextProvider.GetContextsForFileAsync(config.PrefixPath, config.PrefixPath + "\\Lib\\os.py", cancellationToken)) {
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
