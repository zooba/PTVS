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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Infrastructure;

namespace Microsoft.PythonTools.Interpreter.Default {
    class CPythonInterpreter : IPythonInterpreter, IPythonInterpreterWithProjectReferences2, IDisposable {
        readonly Version _langVersion;
        private PythonInterpreterFactoryWithDatabase _factory;
        private PythonTypeDatabase _typeDb;
        private HashSet<ProjectReference> _references;
        private readonly object _referencesLock = new object();

        public CPythonInterpreter(PythonInterpreterFactoryWithDatabase factory) {
            _langVersion = factory.Configuration.Version;
            _factory = factory;
            _typeDb = _factory.GetCurrentDatabase();
            _factory.NewDatabaseAvailable += OnNewDatabaseAvailable;
        }

        private async void OnNewDatabaseAvailable(object sender, EventArgs e) {
            var factory = _factory;
            if (factory == null) {
                // We have been disposed already, so ignore this event
                return;
            }

            _typeDb = factory.GetCurrentDatabase();

            List<ProjectReference> references = null;
            lock (_referencesLock) {
                references = _references != null ? _references.ToList() : null;
            }
            if (references != null) {
                _typeDb = _typeDb.Clone();
                foreach (var reference in references) {
                    string modName;
                    try {
                        modName = Path.GetFileNameWithoutExtension(reference.Name);
                    } catch (ArgumentException) {
                        continue;
                    }
                    try {
                        await _typeDb.LoadExtensionModuleAsync(modName, reference.Name);
                    } catch (Exception ex) {
                        try {
                            Directory.CreateDirectory(factory.DatabasePath);
                        } catch (IOException) {
                        } catch (UnauthorizedAccessException) {
                        }
                        if (Directory.Exists(factory.DatabasePath)) {
                            var analysisLog = Path.Combine(factory.DatabasePath, "AnalysisLog.txt");
                            for (int retries = 10; retries > 0; --retries) {
                                try {
                                    File.AppendAllText(analysisLog, string.Format(
                                        "Exception while loading extension module {0}{1}{2}{1}",
                                        reference.Name,
                                        Environment.NewLine,
                                        ex
                                    ));
                                    break;
                                } catch (Exception ex2) {
                                    if (ex2.IsCriticalException()) {
                                        throw;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            var evt = ModuleNamesChanged;
            if (evt != null) {
                evt(this, EventArgs.Empty);
            }
        }

        #region IPythonInterpreter Members

        public IPythonType GetBuiltinType(BuiltinTypeId id) {
            if (id == BuiltinTypeId.Unknown) {
                return null;
            }

            if (_typeDb == null) {
                throw new KeyNotFoundException(string.Format("{0} ({1})", id, (int)id));
            }

            var name = SharedDatabaseState.GetBuiltinTypeName(id, _typeDb.LanguageVersion);
            var res = _typeDb.BuiltinModule.GetAnyMember(name) as IPythonType;
            if (res == null) {
                throw new KeyNotFoundException(string.Format("{0} ({1})", id, (int)id));
            }
            return res;
        }


        public IList<string> GetModuleNames() {
            if (_typeDb == null) {
                return new string[0];
            }
            return new List<string>(_typeDb.GetModuleNames());
        }

        public IPythonModule ImportModule(string name) {
            if (_typeDb == null) {
                return null;
            }
            return _typeDb.GetModule(name);
        }

        public IModuleContext CreateModuleContext() {
            return null;
        }

        public void Initialize(PythonAnalyzer state) {
        }

        public event EventHandler ModuleNamesChanged;

        public Task AddReferenceAsync(ProjectReference reference, CancellationToken cancellationToken = default(CancellationToken)) {
            if (reference == null) {
                return MakeExceptionTask(new ArgumentNullException("reference"));
            }

            bool cloneDb = false;
            lock (_referencesLock) {
                if (_references == null) {
                    _references = new HashSet<ProjectReference>();
                    cloneDb = true;
                }
            }

            if (cloneDb && _typeDb != null) {
                // If we needed to set _references, then we also need to clone
                // _typeDb to avoid adding modules to the shared database.
                _typeDb = _typeDb.Clone();
            }

            switch (reference.Kind) {
                case ProjectReferenceKind.ExtensionModule:
                    lock (_referencesLock) {
                        _references.Add(reference);
                    }
                    string filename;
                    try {
                        filename = Path.GetFileNameWithoutExtension(reference.Name);
                    } catch (Exception e) {
                        return MakeExceptionTask(e);
                    }

                    if (_typeDb != null) {
                        return _typeDb.LoadExtensionModuleAsync(filename,
                            reference.Name,
                            cancellationToken).ContinueWith(RaiseModulesChanged);
                    }
                    break;
            }

            return Task.Factory.StartNew(EmptyTask);
        }

        public void RemoveReference(ProjectReference reference) {
            switch (reference.Kind) {
                case ProjectReferenceKind.ExtensionModule:
                    bool removed = false;
                    lock (_referencesLock) {
                        removed = _references != null && _references.Remove(reference);
                    }
                    if (removed && _typeDb != null) {
                        _typeDb.UnloadExtensionModule(Path.GetFileNameWithoutExtension(reference.Name));
                        RaiseModulesChanged(null);
                    }
                    break;
            }
        }

        public IEnumerable<ProjectReference> GetReferences() {
            var references = _references;
            return references != null ?
                references.AsLockedEnumerable(_referencesLock) :
                Enumerable.Empty<ProjectReference>();
        }

        private static Task MakeExceptionTask(Exception e) {
            var res = new TaskCompletionSource<Task>();
            res.SetException(e);
            return res.Task;
        }

        private static void EmptyTask() {
        }

        private void RaiseModulesChanged(Task task) {
            if (task != null && task.Exception != null) {
                throw task.Exception;
            }
            var modNamesChanged = ModuleNamesChanged;
            if (modNamesChanged != null) {
                modNamesChanged(this, EventArgs.Empty);
            }
        }

        #endregion


        public void Dispose() {
            _typeDb = null;

            var factory = _factory;
            _factory = null;
            if (factory != null) {
                factory.NewDatabaseAvailable -= OnNewDatabaseAvailable;
            }
        }
    }
}
