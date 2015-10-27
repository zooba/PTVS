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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    class PathSet<T> {
        private Node _root;
        private int _count;
        private IReadOnlyCollection<PathPart> _prefix;

        public PathSet(string prefix) {
            if (!string.IsNullOrEmpty(prefix)) {
                prefix = prefix.TrimEnd(DirSeparators);
                _root = new Node(prefix);
                if (!TryNormalize(prefix, null, out _prefix)) {
                    throw new ArgumentException("Failed to normalize prefix");
                }
            } else {
                _root = new Node(null);
            }
        }

        public int Count { get { return _count; } }

        struct PathPart {
            public string Key;
            public string Name;

            public static readonly PathPart Empty = new PathPart();
            
            public static string Normalize(string path) {
                return path.ToUpperInvariant().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            public PathPart(string name) {
                Key = Normalize(name);
                Name = name;
            }
        }

        private static readonly char[] DirSeparators =
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        private bool TryNormalize(string path, IEnumerable<string> extraParts, out IReadOnlyCollection<PathPart> parts) {
            var result = new List<PathPart>();
            parts = result;
            int start = 0, end = 0;
            IEnumerator<PathPart> prefix = _prefix == null ? null : _prefix.GetEnumerator();
            IEnumerator<string> e = null;
            bool breakOnNext = false;

            while (!breakOnNext) {
                PathPart pp;
                if (e != null) {
                    if (!e.MoveNext()) {
                        break;
                    }
                    pp = new PathPart(e.Current);
                } else if ((end = path.IndexOfAny(DirSeparators, start + 1)) > 0) {
                    pp = new PathPart(path.Substring(start, end - start));
                } else {
                    if (extraParts != null) {
                        e = extraParts.GetEnumerator();
                    } else {
                        breakOnNext = true;
                    }
                    if (0 <= start && start < path.Length) {
                        pp = new PathPart(path.Substring(start));
                    } else {
                        continue;
                    }
                }
                if (prefix != null && !prefix.MoveNext()) {
                    prefix.Dispose();
                    prefix = null;
                }
                if (prefix == null) {
                    result.Add(pp);
                } else if (prefix.Current.Key != pp.Key) {
                    prefix.Dispose();
                    prefix = null;
                    if (e != null) {
                        e.Dispose();
                    }
                    return false;
                }

                start = end + 1;
            }

            if (e != null) {
                e.Dispose();
            }
            return true;
        }

        private IEnumerable<PathPart> Normalize(IEnumerable<string> parts) {
            return parts.Select(p => new PathPart(p));
        }


        public bool Add(string fullPath, T value) {
            IReadOnlyCollection<PathPart> parts;
            if (!TryNormalize(fullPath, null, out parts)) {
                throw new ArgumentException("Path does not match prefix");
            }

            var node = _root;
            foreach (var part in parts) {
                var children = node.GetOrCreateChildren();
                if (!children.TryGetValue(part.Key, out node)) {
                    children[part.Key] = node = new Node(part.Name);
                }
            }

            if (node != null) {
                node.Path = fullPath;
                node.Value = value;
                _count += 1;
                return true;
            }

            return false;
        }

        public bool Contains(string path) {
            T dummy;
            return TryGetValue(path, out dummy);
        }

        public bool TryFindValueByParts(string rootPath, IEnumerable<string> parts, out T value) {
            value = default(T);
            IReadOnlyCollection<PathPart> normParts;
            if (!TryNormalize(rootPath, parts, out normParts)) {
                return false;
            }

            var node = _root;
            foreach (var part in normParts) {
                var children = node.GetChildren();
                if (children == null || !children.TryGetValue(part.Key, out node)) {
                    return false;
                }
            }

            if (node == null) {
                return false;
            }
            value = node.Value;
            return true;
        }

        public void Clear() {
            _root.NewChildren();
            _count = 0;
        }

        public bool TryGetValue(string fullPath, out T value) {
            value = default(T);
            IReadOnlyCollection<PathPart> parts;
            if (!TryNormalize(fullPath, null, out parts)) {
                return false;
            }
            var node = _root;
            foreach (var part in parts) {
                var children = node.GetChildren();
                if (children == null || !children.TryGetValue(part.Key, out node)) {
                    return false;
                }
            }

            if (node == null) {
                return false;
            }
            value = node.Value;
            return true;
        }

        public IEnumerable<string> GetPaths() {
            var q = new Stack<IEnumerator<KeyValuePair<string, Node>>>();
            var children = _root.GetChildren();
            if (children == null || !children.Any()) {
                yield break;
            }
            var e = children.OrderBy(kv => kv.Key).GetEnumerator();

            while (true) {
                if (!e.MoveNext()) {
                    e.Dispose();

                    if (!q.Any()) {
                        yield break;
                    }

                    e = q.Pop();
                    continue;
                }

                var item = e.Current;

                if (!string.IsNullOrEmpty(item.Value.Path)) {
                    yield return item.Value.Path;
                }

                children = item.Value.GetChildren();
                if (children != null && children.Any()) {
                    q.Push(e);
                    e = children.OrderBy(kv => kv.Key).GetEnumerator();
                }
            }
        }

        public IReadOnlyCollection<string> GetChildren(string rootPath, IEnumerable<string> extraParts = null) {
            var result = new List<string>();

            IReadOnlyCollection<PathPart> parts;
            if (!TryNormalize(rootPath, extraParts, out parts)) {
                return result;
            }

            var node = _root;
            Dictionary<string, Node> children = node.GetChildren();
            foreach (var part in parts) {
                if (children == null || !children.TryGetValue(part.Key, out node)) {
                    break;
                }
                children = node.GetChildren();
            }

            if (children != null) {
                foreach (var c in children) {
                    if (!string.IsNullOrEmpty(c.Value.Name)) {
                        result.Add(c.Value.Name);
                    }
                }
            }

            return result;
        }

        public IEnumerable<T> GetValues() {
            var q = new Stack<Dictionary<string, Node>.Enumerator>();
            var children = _root.GetChildren();
            if (children == null || !children.Any()) {
                yield break;
            }
            var e = children.GetEnumerator();

            while (true) {
                if (!e.MoveNext()) {
                    e.Dispose();

                    if (!q.Any()) {
                        break;
                    }

                    e = q.Pop();
                    continue;
                }

                var i = e.Current.Value;
                if (i == null) {
                    break;
                }

                if (i.Path != null) {
                    yield return i.Value;
                }

                children = i.GetChildren();
                if (children != null && children.Any()) {
                    q.Push(e);
                    e = children.GetEnumerator();
                }
            }
        }

        private sealed class Node {
            private Dictionary<string, Node> _children;
            internal readonly string Name;
            internal string Path;
            internal T Value;

            public Node(string name) {
                Name = name;
            }

            public override string ToString() {
                return "{" + Name + "}";
            }

            public Dictionary<string, Node> GetChildren() {
                return _children;
            }

            public Dictionary<string, Node> NewChildren() {
                _children = new Dictionary<string, Node>();
                return _children;
            }

            public bool TryGetChildren(out Dictionary<string, Node> children) {
                children = _children;
                return _children != null;
            }

            public Dictionary<string, Node> GetOrCreateChildren() {
                if (_children == null) {
                    _children = new Dictionary<string, Node>();
                }
                return _children;
            }
        }
    }
}
