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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Common.Infrastructure;
using Microsoft.PythonTools.Infrastructure;

namespace Microsoft.PythonTools.Analysis {
    public abstract class AnalysisValue : IAnalysisValue, IAnalysisSet {
        public static readonly AnalysisValue Empty = new EmptyAnalysisValue();

        protected static readonly IReadOnlyCollection<string> EmptyNames = new string[0];

        private readonly VariableKey _key;

        public AnalysisValue(VariableKey key) {
            _key = key;
        }

        public VariableKey Key => _key;

        public override int GetHashCode() => GetType().GetHashCode() ^ _key.GetHashCode();

        public override bool Equals(object obj) {
            if (GetType() != obj?.GetType()) {
                return false;
            }
            return _key == ((AnalysisValue)obj)._key;
        }

        public virtual Task<string> ToAnnotationAsync(CancellationToken cancellationToken) {
            return Task.FromResult("Any");
        }

        public virtual Task<string> ToDebugAnnotationAsync(CancellationToken cancellationToken) {
            return ToAnnotationAsync(cancellationToken);
        }

        public virtual async Task GetAttribute(
            IAnalysisState caller,
            string attribute,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            if (Key.IsEmpty) {
                return;
            }
            var attr = Key + ("." + attribute);
            var res = attr.GetTypes(caller) ?? await attr.GetTypesAsync(cancellationToken) ?? AnalysisSet.Empty;
            await result.AddTypesAsync(res, cancellationToken);
        }

        public virtual async Task GetDescriptor(
            IAnalysisState caller,
            VariableKey instance,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            await result.AddTypesAsync(this, cancellationToken);
        }

        public virtual Task<IReadOnlyCollection<string>> GetAttributeNames(
            IAnalysisState caller,
            CancellationToken cancellationToken
        ) {
            return Task.FromResult(EmptyNames);
        }

        public virtual async Task Call(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            if (Key.IsEmpty) {
                return;
            }
            var callee = new LocalAssignable("__call__");
            await GetAttribute(callSite.State, "__call__", callee, cancellationToken);
            await callee.Values.Call(callSite, result, cancellationToken);
            // TODO: Report uncallable object
            //await _key.State.ReportErrorAsync();
        }

        public virtual Task AssignWithCallContext(
            CallSiteKey callSite,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            return result.AddTypesAsync(this, cancellationToken);
        }

        long IAnalysisSet.Version => 0;
        int IAnalysisSet.Count => 1;
        bool IAnalysisSet.Any() => true;
        int ICollection<AnalysisValue>.Count => 1;
        int IReadOnlyCollection<AnalysisValue>.Count => 1;

        bool ICollection<AnalysisValue>.IsReadOnly => true;

        void ICollection<AnalysisValue>.Add(AnalysisValue item) {
            throw new NotSupportedException("Collection is read only");
        }

        void ICollection<AnalysisValue>.Clear() {
            throw new NotSupportedException("Collection is read only");
        }

        bool ICollection<AnalysisValue>.Contains(AnalysisValue item) {
            return this == item;
        }

        void ICollection<AnalysisValue>.CopyTo(AnalysisValue[] array, int arrayIndex) {
            array[arrayIndex] = this;
        }

        bool ICollection<AnalysisValue>.Remove(AnalysisValue item) {
            throw new NotSupportedException("Collection is read only");
        }

        IEnumerator<AnalysisValue> IEnumerable<AnalysisValue>.GetEnumerator() {
            yield return this;
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return ((IEnumerable<AnalysisValue>)this).GetEnumerator();
        }

        IAnalysisSet IAnalysisSet.Clone(bool asReadOnly) {
            if (asReadOnly) {
                return this;
            }
            var set = new AnalysisSet(1);
            set.Add(this);
            return set;
        }

        void IAnalysisSet.AddRange(IEnumerable<AnalysisValue> values) {
            throw new NotSupportedException("Collection is read only");
        }

        IAnalysisSet IAnalysisSet.Union(IEnumerable<AnalysisValue> other) {
            var r = new AnalysisSet(other);
            r.Add(this);
            return r;
        }

        bool IAnalysisSet.SetEquals(IAnalysisSet other) {
            return other != null && other.Count == 1 &&
                (other as AnalysisValue ?? other.Single()) == this;
        }

        private class EmptyAnalysisValue : AnalysisValue {
            public EmptyAnalysisValue() : base(VariableKey.Empty) { }
        }
    }
}
