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
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Analysis {
    sealed class XamlProjectEntry : IXamlProjectEntry {
        private XamlAnalysis _analysis;
        private readonly string _filename;
        private int _version;
        private string _content;
        private IAnalysisCookie _cookie;
        private Dictionary<object, object> _properties;
        private readonly HashSet<IProjectEntry> _dependencies = new HashSet<IProjectEntry>();

        public XamlProjectEntry(string filename) {
            _filename = filename;
        }

        public void ParseContent(TextReader content, IAnalysisCookie fileCookie) {
            _content = content.ReadToEnd();
            _cookie = fileCookie;
        }

        public void AddDependency(IProjectEntry projectEntry) {
            lock (_dependencies) {
                _dependencies.Add(projectEntry);
            }
        }

        #region IProjectEntry Members

        public bool IsAnalyzed {
            get { return _analysis != null; }
        }

        public void Analyze(CancellationToken cancel) {
            if (cancel.IsCancellationRequested) {
                return;
            }

            lock (this) {
                if (_analysis == null) {
                    _analysis = new XamlAnalysis(_filename);
                    _cookie = new FileCookie(_filename);
                }
                _analysis = new XamlAnalysis(new StringReader(_content));

                _version++;

                // update any .py files which depend upon us.
                for (var deps = GetNewDependencies(null); deps.Any(); deps = GetNewDependencies(deps)) {
                    foreach (var dep in deps) {
                        dep.Analyze(cancel);
                    }
                }
            }
        }

        private HashSet<IProjectEntry> GetNewDependencies(HashSet<IProjectEntry> oldDependencies) {
            HashSet<IProjectEntry> deps;
            lock (_dependencies) {
                deps = new HashSet<IProjectEntry>(_dependencies);
            }

            if (oldDependencies != null) {
                deps.ExceptWith(oldDependencies);
            }

            return deps;
        }

        public string FilePath { get { return _filename; } }

        public int AnalysisVersion {
            get {
                return _version;
            }
        }

        public string GetLine(int lineNo) {
            return _cookie.GetLine(lineNo);
        }

        public Dictionary<object, object> Properties {
            get {
                if (_properties == null) {
                    _properties = new Dictionary<object, object>();
                }
                return _properties;
            }
        }

        public IModuleContext AnalysisContext {
            get { return null; }
        }

        public void RemovedFromProject() { }

        #endregion

        #region IXamlProjectEntry Members

        public XamlAnalysis Analysis {
            get { return _analysis; }
        }

        #endregion

    }

    public interface IXamlProjectEntry : IExternalProjectEntry {
        XamlAnalysis Analysis {
            get;
        }
    }
}
