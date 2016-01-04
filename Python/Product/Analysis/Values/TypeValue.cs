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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;

namespace Microsoft.PythonTools.Analysis.Values {
    class TypeValue : AnalysisValue {
        private readonly string _name;
        private readonly InstanceValue _instance;
        private readonly Dictionary<string, IAnalysisSet> _members;

        public TypeValue(VariableKey key, string name) : base(key) {
            _name = name;
            _instance = new InstanceValue(key, key);
            _members = new Dictionary<string, IAnalysisSet>();
        }

        public void AddMember(string name, Func<VariableKey, IAnalysisSet> value) {
            _members[name] = value(Key + "." + name);
        }

        public AnalysisValue Instance => _instance;

        public override Task Call(
            CallSiteKey callSite,
            IAssignable variable,
            CancellationToken cancellationToken
        ) {
            return variable.AddTypesAsync(Instance, cancellationToken);
        }

        public override Task GetAttribute(
            IAnalysisState caller,
            string attribute,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            IAnalysisSet value;
            if (_members.TryGetValue(attribute, out value)) {
                return result.AddTypesAsync(value, cancellationToken);
            }
            return base.GetAttribute(caller, attribute, result, cancellationToken);
        }

        public override async Task<string> ToAnnotationAsync(CancellationToken cancellationToken) {
            return "type";
        }

        public virtual async Task<string> ToInstanceAnnotationAsync(CancellationToken cancellationToken) {
            return _name;
        }
    }
}
