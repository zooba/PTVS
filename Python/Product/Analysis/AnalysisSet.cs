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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis {
    /// <summary>
    /// HashSet used in analysis engine.
    /// 
    /// This set is thread safe for a single writer and multiple readers.  
    /// 
    /// Reads and writes on the dictionary are all lock free, but have memory
    /// barriers in place.  The key is used to indicate the current state of a bucket.
    /// When adding a bucket the key is updated last after all other values
    /// have been added.  When removing a bucket the key is cleared first.  Memory
    /// barriers are used to ensure that the writes to the key bucket are not
    /// re-ordered.
    /// 
    /// When resizing the set the buckets are replaced atomically so that the reader
    /// sees the new buckets or the old buckets.  When reading the reader first reads
    /// the buckets and then calls a static helper function to do the read from the bucket
    /// array to ensure that readers are not seeing multiple bucket arrays.
    /// </summary>
    [DebuggerTypeProxy(typeof(DebugViewProxy))]
    [Serializable]
    sealed class AnalysisSet : IAnalysisSet {
        [NonSerialized]
        private Bucket[] _buckets;
        private int _count;
        private readonly IEqualityComparer<AnalysisValue> _comparer;
        private long _version;

        public static readonly IAnalysisSet Empty = new AnalysisSet(isCopyOnWrite: false, isReadOnly: true);
        public static readonly Task<IAnalysisSet> EmptyTask = Task.FromResult(Empty);

        // Flags merged into _version
        private const long Mask = 0x7000000000000000;
        private const long CopyOnWrite = 0x1000000000000000;
        private const long ReadOnly = 0x2000000000000000;

        private const int InitialBucketSize = 3;
        private const int ResizeMultiplier = 2;
        private const double Load = .9;

        // Marker object used to indicate we have a removed value
        private class Removed : AnalysisValue {
            public static readonly Removed Value = new Removed();
            private Removed() : base(VariableKey.Empty) { }
        }

        /// <summary>
        /// Creates a new dictionary storage with no buckets
        /// </summary>
        public AnalysisSet() {
            _comparer = EqualityComparer<AnalysisValue>.Default;
        }

        private AnalysisSet(bool isReadOnly, bool isCopyOnWrite) : this() {
            _version = (isCopyOnWrite ? CopyOnWrite : 0) | (isReadOnly ? ReadOnly : 0);
        }

        /// <summary>
        /// Creates a new dictionary storage with no buckets
        /// </summary>
        public AnalysisSet(int count) {
            _buckets = new Bucket[AnalysisDictionary<object, object>.GetPrime((int)(count / Load + 2))];
            _comparer = EqualityComparer<AnalysisValue>.Default;
        }

        public AnalysisSet(IEqualityComparer<AnalysisValue> comparer) {
            _comparer = comparer;
        }

        public AnalysisSet(int count, IEqualityComparer<AnalysisValue> comparer) {
            _buckets = new Bucket[AnalysisDictionary<object, object>.GetPrime((int)(count / Load + 2))];
            _comparer = comparer;
        }

        public AnalysisSet(IEnumerable<AnalysisValue> enumerable, IEqualityComparer<AnalysisValue> comparer)
            : this(comparer) {
            AddRange(enumerable);
        }

        public AnalysisSet(IEnumerable<AnalysisValue> enumerable) : this() {
            AddRange(enumerable);
        }

        public IAnalysisSet Trim() {
            var buckets = _buckets;
            if (buckets == null) {
                return Empty;
            }
            AnalysisValue single = null;
            foreach (var b in buckets) {
                if (b.Key != null && b.Key != Removed.Value) {
                    if (single == null) {
                        single = b.Key;
                    } else {
                        return this;
                    }
                }
            }
            return single ?? Empty;
        }

        public IEqualityComparer<AnalysisValue> Comparer => _comparer;
        public long Version => Volatile.Read(ref _version);
        public bool IsReadOnly => (_version & ReadOnly) == ReadOnly;

        private void IncreaseVersion() {
            Interlocked.Increment(ref _version);
        }

        private Bucket[] GetBucketsForWriting() {
            var version = _version;
            var buckets = _buckets;
            if ((version & ReadOnly) == ReadOnly) {
                throw new NotSupportedException("Set is read-only");
            }
            if ((version & CopyOnWrite) == CopyOnWrite) {
                if (buckets != null) {
                    var newBuckets = new Bucket[buckets.Length];
                    Array.Copy(buckets, newBuckets, buckets.Length);
                    buckets = newBuckets;
                }
                var currentVersion = version;
                var newVersion = version & ~CopyOnWrite;
                while ((currentVersion = Interlocked.CompareExchange(ref _version, newVersion, version)) != version) {
                    newVersion = currentVersion & ~CopyOnWrite;
                }
            }
            if (buckets == null) {
                Initialize();
                buckets = _buckets;
            }
            return buckets;
        }

        public void Add(AnalysisValue item) {
            var buckets = GetBucketsForWriting();
            if (AddOne(ref buckets, item)) {
                _buckets = buckets;
                IncreaseVersion();
            }
        }

        public void AddRange(IEnumerable<AnalysisValue> items) {
            var buckets = GetBucketsForWriting();
            bool anyChanged = false;
            // Faster path if we are allowed to mutate ourselves
            var otherHc = items as AnalysisSet;
            if (otherHc != null) {
                if (otherHc._count != 0) {
                    // do a fast copy from the other hash set...
                    var otherBuckets = otherHc._buckets;
                    for (int i = 0; i < otherBuckets.Length; i++) {
                        var key = otherBuckets[i].Key;
                        if (key != null && key != AnalysisDictionaryRemovedValue.Instance) {
                            anyChanged |= AddOne(ref buckets, key);
                        }
                    }
                }
            } else {
                // some other set, copy it the slow way...
                using (var e = items.GetEnumerator()) {
                    anyChanged = AddFromEnumerator(ref buckets, e);
                }
            }
            if (anyChanged) {
                _buckets = buckets;
                IncreaseVersion();
            }
        }

        public IAnalysisSet Union(IEnumerable<AnalysisValue> other) {
            if (Count == 0) {
                return other.ToSet();
            }
            var r = Clone();
            r.AddRange(other);
            r.Remove(AnalysisValue.Empty);
            return r;
        }

        private bool AddFromEnumerator(ref Bucket[] buckets, IEnumerator<AnalysisValue> items) {
            bool wasChanged = false;
            while (items.MoveNext()) {
                wasChanged |= AddOne(ref buckets, items.Current);
            }
            return wasChanged;
        }

        public bool Remove(AnalysisValue key) {
            var buckets = GetBucketsForWriting();
            int i = Contains(buckets, key);
            if (i < 0) {
                return false;
            }

            _buckets[i].Key = Removed.Value;
            _count--;
            IncreaseVersion();
            return true;
        }

        public IAnalysisSet Clone(bool asReadOnly = false) {
            var buckets = _buckets;
            var count = _count;
            var version = _version;
            var res = new AnalysisSet(Comparer);
            res._buckets = buckets;
            res._count = count;
            res._version = (version & ~Mask) | (asReadOnly ? ReadOnly : CopyOnWrite);
            if (!asReadOnly) {
                // If we're using CopyOnWrite for the clone, we also need to
                // copy this set next time it is changed.
                _version |= CopyOnWrite;
            }
            return res;
        }

        public bool SetEquals(IAnalysisSet other) {
            if (ReferenceEquals(this, other)) {
                return true;
            }
            if (other == null) {
                return false;
            }
            var otherHc = new HashSet<AnalysisValue>(other, _comparer);
            foreach (var key in this) {
                if (!otherHc.Remove(key)) {
                    return false;
                }
            }
            if (otherHc.Any()) {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Adds a new item to the dictionary, replacing an existing one if it already exists.
        /// </summary>
        private bool AddOne(ref Bucket[] buckets, AnalysisValue key) {
            if (key == null) {
                throw new ArgumentNullException("key");
            }

            if (Add(buckets, key)) {
                _count++;

                CheckGrow(ref buckets);
                return true;
            }
            return false;
        }

        private void CheckGrow(ref Bucket[] buckets) {
            if (_count >= (buckets.Length * Load)) {
                // grow the hash table
                EnsureSize(ref buckets, (int)(buckets.Length / Load) * ResizeMultiplier);
            }
        }

        private void EnsureSize(ref Bucket[] buckets, int newSize) {
            // see if we can reclaim collected buckets before growing...
            if (buckets == null) {
                buckets = new Bucket[newSize];
                return;
            }

            if (newSize > buckets.Length) {
                newSize = AnalysisDictionary<object, object>.GetPrime(newSize);

                var newBuckets = new Bucket[newSize];

                for (int i = 0; i < buckets.Length; i++) {
                    var curBucket = buckets[i];
                    if (curBucket.Key != null &&
                        curBucket.Key != Removed.Value) {
                        AddOne(newBuckets, curBucket.Key, curBucket.HashCode);
                    }
                }

                buckets = newBuckets;
            }
        }

        /// <summary>
        /// Initializes the buckets to their initial capacity, the caller
        /// must check if the buckets are empty first.
        /// </summary>
        private void Initialize() {
            _buckets = new Bucket[InitialBucketSize];
        }

        /// <summary>
        /// Add helper that works over a single set of buckets.  Used for
        /// both the normal add case as well as the resize case.
        /// </summary>
        private bool Add(Bucket[] buckets, AnalysisValue key) {
            int hc = _comparer.GetHashCode(key) & Int32.MaxValue;

            return AddOne(buckets, key, hc);
        }

        /// <summary>
        /// Add helper which adds the given key/value (where the key is not null) with
        /// a pre-computed hash code.
        /// </summary>
        private bool AddOne(Bucket[] buckets, AnalysisValue/*!*/ key, int hc) {
            Debug.Assert(key != null);

            Debug.Assert(_count < buckets.Length);
            int index = hc % buckets.Length;
            int startIndex = index;
            int addIndex = -1;

            for (;;) {
                Bucket cur = buckets[index];
                var existingKey = cur.Key;
                if (existingKey == null || existingKey == Removed.Value) {
                    if (addIndex == -1) {
                        addIndex = index;
                    }
                    if (cur.Key == null) {
                        break;
                    }
                } else if (ReferenceEquals(key, existingKey) ||
                    cur.HashCode == hc && _comparer.Equals(key, existingKey)) {
                    return false;
                }

                index = ProbeNext(buckets, index);

                if (index == startIndex) {
                    break;
                }
            }

            if (buckets[addIndex].Key != null && buckets[addIndex].Key != Removed.Value) {
                _count--;
            }
            buckets[addIndex].HashCode = hc;
            Thread.MemoryBarrier();
            // we write the key last so that we can check for null to
            // determine if a bucket is available.
            buckets[addIndex].Key = key;

            return true;
        }

        private static int ProbeNext(Bucket[] buckets, int index) {
            // probe to next bucket    
            return (index + ((buckets.Length - 1) / 2)) % buckets.Length;
        }

        /// <summary>
        /// Checks to see if the key exists in the dictionary.
        /// </summary>
        public bool Contains(AnalysisValue key) {
            if (key == null) {
                throw new ArgumentNullException("key");
            }
            return Contains(_buckets, key) >= 0;
        }

        /// <summary>
        /// Static helper to try and get the value from the dictionary.
        /// 
        /// Used so the value lookup can run against a buckets while a writer
        /// replaces the buckets.
        /// </summary>
        private int Contains(Bucket[] buckets, AnalysisValue/*!*/ key) {
            Debug.Assert(key != null);

            if (_count > 0 && buckets != null) {
                int hc = _comparer.GetHashCode(key) & Int32.MaxValue;

                return Contains(buckets, key, hc);
            }

            return -1;
        }

        private int Contains(Bucket[] buckets, AnalysisValue key, int hc) {
            int index = hc % buckets.Length;
            int startIndex = index;
            do {
                var existingKey = buckets[index].Key;
                if (existingKey == null) {
                    break;
                } else {
                    if (ReferenceEquals(key, existingKey) ||
                        (existingKey != Removed.Value &&
                         buckets[index].HashCode == hc &&
                         _comparer.Equals(key, existingKey))
                    ) {
                        return index;
                    }
                }

                index = ProbeNext(buckets, index);
            } while (startIndex != index);

            return -1;
        }

        public int Count => _count;
        public bool Any() => _count > 0;

        public void Clear() {
            if (_buckets != null && _count != 0) {
                _buckets = new Bucket[InitialBucketSize];
                _count = 0;
            }
        }

        public IEnumerator<AnalysisValue> GetEnumerator() {
            var buckets = _buckets;
            if (buckets != null) {
                for (int i = 0; i < buckets.Length; i++) {
                    var key = buckets[i].Key;
                    if (key != null && key != Removed.Value) {
                        yield return key;
                    }
                }
            }
        }

        /// <summary>
        /// Used to store a single hashed key/value.
        /// 
        /// Bucket is not serializable because it stores the computed hash
        /// code which could change between serialization and deserialization.
        /// </summary>
        struct Bucket {
            public AnalysisValue Key;          // the key to be hashed
            public int HashCode;        // the hash code of the contained key.
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        public void CopyTo(AnalysisValue[] array, int arrayIndex) {
            using (var e = GetEnumerator()) {
                for (int i = arrayIndex; i < array.Length; ++i) {
                    array[i] = e.MoveNext() ? e.Current : null;
                }
            }
        }

        public override string ToString() {
            return new DebugViewProxy(this).ToString();
        }

        sealed class DebugViewProxy {
            public DebugViewProxy(AnalysisSet source) {
                Data = source.ToArray();
                Comparer = source.Comparer;
            }

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public AnalysisValue[] Data;

            public override string ToString() {
                return ToString(Data);
            }

            public static string ToString(AnalysisSet source) {
                return ToString(source.ToArray());
            }

            public static string ToString(AnalysisValue[] source) {
                var data = source.ToArray();
                if (data.Length == 0) {
                    return "{}";
                } else if (data.Length < 5) {
                    return "{" + string.Join(", ", data.AsEnumerable()) + "}";
                } else {
                    return string.Format("{{Size = {0}}}", data.Length);
                }
            }

            public IEqualityComparer<AnalysisValue> Comparer {
                get;
                private set;
            }

            public int Size {
                get { return Data.Length; }
            }
        }
    }
}

