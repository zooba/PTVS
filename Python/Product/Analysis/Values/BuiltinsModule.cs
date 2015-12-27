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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;

namespace Microsoft.PythonTools.Analysis.Values {
    class BuiltinsModule : ModuleValue, IDirectAttributeProvider {
        private readonly TypeValue _noneType, _code;
        private readonly NumericValue _bool, _int, _long, _float, _complex;
        private readonly StringValue _bytes, _str, _unicode;

        private readonly Dictionary<string, BuiltinFunctionValue> _functions;

        public BuiltinsModule(VariableKey key, string fullname, string name, string moniker) :
            base(key, fullname, name, moniker) {
            Func<string, VariableKey> K = n => new VariableKey(key.State, n);
            _noneType = new TypeValue(K("NoneType"), "None");
            _code = new TypeValue(K("code"), "code");
            _bool = new NumericValue(K("bool"), "bool");
            _int = new NumericValue(K("int"), "int");
            _long = Key.State.Features.HasLong ? new NumericValue(K("long"), "long") : _int;
            _float = new NumericValue(K("float"), "float");
            _complex = new NumericValue(K("complex"), "complex");
            _bytes = new StringValue(Key, false);
            _unicode = new StringValue(Key, true);
            _str = Key.State.Features.IsUnicodeCalledStr ? _unicode : _bytes;

            _functions = new Dictionary<string, BuiltinFunctionValue>();
            Action<string, string, BuiltinFunctionValue.CallDelegate> F = (n, sig, fn) => {
                _functions[n] = new BuiltinFunctionValue(K(n), sig, fn);
            };

            F("abs", "Callable[[T], T]", ReturnParameter1);
            F("all", "Callable[..., bool]", ReturnBool);
            F("any", "Callable[..., bool]", ReturnBool);
            F("ascii", "Callable[[Any], str]", ReturnStr);
            F("bin", "Callable[[Any], str]", ReturnStr);
            F("callable", "Callable[[Any], bool]", ReturnBool);
            F("chr", "Callable[[Any], str]", ReturnStr);
            //_classmethod
            F("compile", "Callable[..., code]", CompileCall);
            F("delattr", "Callable[[Any, str]]", DelAttrCall);
            F("dir", "Callable[[Any], List[str]]", ReturnListOfStr);
            F("divmod", "Callable[[Any, Any], Tuple[Any, Any]]", DivModCall);
            F("enumerate", "Callable[[Iterable], Iterable]", EnumerateCall);
            F("eval", "Callable[[Any, Optional[Mapping], Optional[Mapping]], Any]", null);
            if (!Key.State.Features.HasExecStatement) {
                F("exec", "Callable[[Any, Optional[Mapping], Optional[Mapping]]]", null);
            }
            F("exit", "Callable[[Any]]", null);
            F("filter", "Callable[[Callable[Any, bool], Iterable], Iterable]", ReturnParameter2);
            F("format", "Callable[[...], str]", ReturnStr);
            F("getattr", "Callable[[Any, str, Optional[Any]], Any]", GetAttrCall);
            F("globals", "Callable[[], Mapping[str, Any]]", GlobalsCall);
            F("hasattr", "Callable[[Any, str], bool]", ReturnBool);
            F("hash", "Callable[[Any], int]", ReturnInt);
            F("help", "Callable[[Any]]", null);
            F("hex", "Callable[[Any], str]", ReturnStr);
            F("id", "Callable[[Any], int]", ReturnInt);
            F("input", "Callable[[str], Any]", null);
            F("isinstance", "Callable[[Any, Type], bool]", ReturnBool);
            F("issubclass", "Callable[[Type, Type], bool]", ReturnBool);
            F("iter", "Callable[[Any], Iterator]", IterCall);
            F("len", "Callable[[Any], int]", ReturnInt);
            F("locals", "Callable[[], Mapping[str, Any]]", LocalsCall);
            F("map", "Callable[[Callable[Any, Any], Iterable], Iterable]", MapCall);
            F("max", "Callable[[...], Any]", MinMaxCall);
        }

        internal static async Task ReturnParameter1(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            await result.AddTypesAsync(await callSite.GetArgValue(0, null, cancellationToken), cancellationToken);
        }

        internal static async Task ReturnParameter2(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            await result.AddTypesAsync(await callSite.GetArgValue(1, null, cancellationToken), cancellationToken);
        }

        internal static async Task MinMaxCall(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            var iterable = await callSite.GetArgValue(0, null, cancellationToken);
            // TODO: Get iterator types
        }

        internal static async Task ReturnBool(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            await result.AddTypesAsync(callSite.State.Analyzer.BuiltinsModule.Bool.Instance, cancellationToken);
        }

        internal static async Task ReturnInt(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            await result.AddTypesAsync(callSite.State.Analyzer.BuiltinsModule.Int.Instance, cancellationToken);
        }

        internal static async Task ReturnStr(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            await result.AddTypesAsync(callSite.State.Analyzer.BuiltinsModule.Str.Instance, cancellationToken);
        }

        internal static async Task ReturnListOfStr(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            //await result.AddTypesAsync(callSite.State.Analyzer.BuiltinsModule.List.Instance, cancellationToken);
            await result.WithSuffix("[]").AddTypesAsync(callSite.State.Analyzer.BuiltinsModule.Str.Instance, cancellationToken);
        }

        private async Task CompileCall(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            await result.AddTypesAsync(Code.Instance, cancellationToken);
        }

        private async Task DelAttrCall(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            var target = await callSite.GetArgValue(0, null, cancellationToken);
            var key = await callSite.GetArgValue(1, null, cancellationToken);

            foreach (var k in key.OfType<StringValue>()) {
                // TODO: Mark key as deleted
            }
        }

        private async Task DivModCall(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            // TODO: Import operator, call operator.divmod
        }

        private async Task EnumerateCall(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            // TODO: Create iterable with int and (if possible) T
        }

        private async Task GetAttrCall(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            var target = await callSite.GetArgValue(0, null, cancellationToken);
            var key = await callSite.GetArgValue(1, null, cancellationToken);
            // Start with the default, or empty if none provided
            var attr = await callSite.GetArgValue(2, null, cancellationToken);

            foreach (var k in key.OfType<StringValue>()) {
                // TODO: Get original key values
            }
            await result.AddTypesAsync(attr, cancellationToken);
        }

        private async Task GlobalsCall(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            // TODO: Return specialized dict that can do name lookups
        }

        private async Task IterCall(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            var target = await callSite.GetArgValue(0, null, cancellationToken);
            var iter = new LocalAssignable(callSite.CallSite.Key + "#__iter__");
            await target.GetAttribute(callSite.State, "__iter__", iter, cancellationToken);
            // Args to __iter__ match, so reuse call site
            await iter.Values.Call(callSite, result, cancellationToken);
        }

        private async Task LocalsCall(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            // TODO: Return specialized dict that can do name lookups
        }

        private async Task MapCall(CallSiteKey callSite, IAssignable result, CancellationToken cancellationToken) {
            var callable = await callSite.GetArgValue(0, null, cancellationToken);
            // TODO: Return callable's return value in an iterable
        }

        internal AnalysisValue None => NoneType.Instance;
        internal TypeValue NoneType => _noneType;
        internal TypeValue Code => _code;
        internal NumericValue Bool => _bool;
        internal NumericValue Int => _int;
        internal NumericValue Long => _long;
        internal NumericValue Float => _float;
        internal NumericValue Complex => _complex;
        internal StringValue Bytes => _bytes;
        internal StringValue Str => _str;
        internal StringValue Unicode => _unicode;

        public async Task<IAnalysisSet> GetAttribute(string attribute, CancellationToken cancellationToken) {
            return GetAttributeWorker(attribute);
        }

        public async override Task GetAttribute(
            IAnalysisState caller,
            string attribute,
            IAssignable result,
            CancellationToken cancellationToken
        ) {
            await result.AddTypesAsync(GetAttributeWorker(attribute), cancellationToken);
        }

        public async Task<IReadOnlyCollection<string>> GetAttributeNames(CancellationToken cancellationToken) {
            return GetAttributeNamesWorker().ToArray();
        }

        public override Task<IReadOnlyCollection<string>> GetAttributeNames(
            IAnalysisState caller,
            CancellationToken cancellationToken
        ) {
            return GetAttributeNames(cancellationToken);
        }

        internal IAnalysisSet GetAttributeWorker(string attribute) {
            switch (attribute) {
                case "bool": return Bool;
                case "int": return Int;
                case "long": return Long;
                case "float": return Float;
                case "complex": return Complex;
                case "NoneType": return NoneType;
                case "bytes": return Bytes;
                case "str": return Str;
                case "unicode": return Unicode;
                case "code": return Code;

                case "copyright": return Str.Instance;
                case "credits": return Str.Instance;

                default:
                    BuiltinFunctionValue r;
                    if (_functions.TryGetValue(attribute, out r)) {
                        return r;
                    }
                    return AnalysisSet.Empty;
            }
        }

        internal IEnumerable<string> GetAttributeNamesWorker() {
            yield return "bool";
            yield return "int";
            yield return "long";
            yield return "float";
            yield return "complex";
            yield return "NoneType";
            yield return "bytes";
            yield return "str";
            yield return "unicode";
            //yield return "code";

            yield return "copyright";
            yield return "credits";

            foreach (var name in _functions.Keys) {
                yield return name;
            }
        }
    }
}
