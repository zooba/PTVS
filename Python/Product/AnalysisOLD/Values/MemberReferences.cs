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
using System.Linq;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.PythonTools.Analysis.Values {
    /// <summary>
    /// Maintains a list of references keyed off of name.
    /// </summary>
    class MemberReferences {
        private readonly Dictionary<string, ReferenceDict> _references;

        public MemberReferences() {
            _references = new Dictionary<string, ReferenceDict>();
        }

        public void AddReference(Node node, AnalysisUnit unit, string name) {
            if (!unit.ForEval) {
                ReferenceDict refs;
                lock (_references) {
                    if (!_references.TryGetValue(name, out refs)) {
                        _references[name] = refs = new ReferenceDict();
                    }
                }
                lock (refs) {
                    refs.GetReferences(unit.DeclaringModule.ProjectEntry)
                        .AddReference(new EncodedLocation(unit.Tree, node));
                }
            }
        }

        public IEnumerable<IReferenceable> GetDefinitions(string name, IMemberContainer innerContainer, IModuleContext context) {
            var res = new List<IReferenceable>();

            ReferenceDict references;
            lock (_references) {
                _references.TryGetValue(name, out references);
            }

            if (references != null) {
                lock (references) {
                    res.AddRange(references.Values);
                }
            }

            var member = innerContainer.GetMember(context, name) as ILocatedMember;
            if (member != null) {
                res.AddRange(member.Locations.Select(loc => new DefinitionList(loc)));
            }

            return res;
        }
    }

    /// <summary>
    /// A collection of references which are keyd off of project entry.
    /// </summary>
    class ReferenceDict : Dictionary<IProjectEntry, ReferenceList> {
        public ReferenceList GetReferences(ProjectEntry project) {
            ReferenceList builtinRef;
            lock (this) {
                if (!TryGetValue(project, out builtinRef) || builtinRef.Version != project.AnalysisVersion) {
                    this[project] = builtinRef = new ReferenceList(project);
                }
            }
            return builtinRef;
        }

        public IEnumerable<LocationInfo> AllReferences {
            get {
                lock (this) {
                    return AllReferencesNoLock.ToList();
                }
            }
        }

        private IEnumerable<LocationInfo> AllReferencesNoLock {
            get {
                foreach (var keyValue in this) {
                    if (keyValue.Value.References != null) {
                        foreach (var reference in keyValue.Value.References) {
                            yield return reference.GetLocationInfo(keyValue.Key);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// A list of references as stored for a single project entry.
    /// </summary>
    class ReferenceList : IReferenceable {
        public readonly int Version;
        public readonly IProjectEntry Project;
        public ISet<EncodedLocation> References;

        public ReferenceList(IProjectEntry project) {
            Version = project.AnalysisVersion;
            Project = project;
            References = new HashSet<EncodedLocation>();
        }

        public void AddReference(EncodedLocation location) {
            lock (this) {
                HashSetExtensions.AddValue(ref References, location);
            }
        }

        #region IReferenceable Members

        public IEnumerable<KeyValuePair<IProjectEntry, EncodedLocation>> Definitions {
            get { yield break; }
        }

        IEnumerable<KeyValuePair<IProjectEntry, EncodedLocation>> IReferenceable.References {
            get {
                return ReferencesNoLock.AsLockedEnumerable(this);
            }
        }

        IEnumerable<KeyValuePair<IProjectEntry, EncodedLocation>> ReferencesNoLock {
            get {
                if (References != null) {
                    foreach (var location in References) {
                        yield return new KeyValuePair<IProjectEntry, EncodedLocation>(Project, location);
                    }
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// A list of references as stored for a single project entry.
    /// </summary>
    class DefinitionList : IReferenceable {
        public readonly LocationInfo _location;

        public DefinitionList(LocationInfo location) {
            _location = location;
        }

        #region IReferenceable Members

        public IEnumerable<KeyValuePair<IProjectEntry, EncodedLocation>> Definitions {
            get {
                yield return new KeyValuePair<IProjectEntry, EncodedLocation>(
                    _location.ProjectEntry,
                    new EncodedLocation(_location, null)
                );
            }
        }

        IEnumerable<KeyValuePair<IProjectEntry, EncodedLocation>> IReferenceable.References {
            get { yield break; }
        }

        #endregion
    }

}
