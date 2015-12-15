using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.PythonTools.Interpreter;

namespace Microsoft.PythonTools.Analysis.Values {
    class BuiltinsModule : ModuleValue {
        private readonly TypeValue _noneType, _code;
        private readonly NumericValue _bool, _int, _long, _float, _complex;
        private readonly StringValue _bytes, _str;

        private readonly BuiltinFunctionValue _abs, _all, _any, _ascii, _bin,
            _callable, _chr, _compile, _delattr;

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
            _str = new StringValue(Key, true);

            _abs = new BuiltinFunctionValue(K("abs"), "Callable[[T], T]", ReturnParameter1);
            _all = new BuiltinFunctionValue(K("all"), "Callable[..., bool]", ReturnBool);
            _any = new BuiltinFunctionValue(K("any"), "Callable[..., bool]", ReturnBool);
            _ascii = new BuiltinFunctionValue(K("ascii"), "Callable[[Any], str]", ReturnStr);
            _bin = new BuiltinFunctionValue(K("bin"), "Callable[[Any], str]", ReturnStr);
            _callable = new BuiltinFunctionValue(K("callable"), "Callable[[Any], bool]", ReturnBool);
            _chr = new BuiltinFunctionValue(K("chr"), "Callable[[Any], str]", ReturnStr);
            //_classmethod
            _compile = new BuiltinFunctionValue(K("compile"), "Callable[..., code]", CompileCall);
            _delattr = new BuiltinFunctionValue(K("delattr"), "Callable[[Any, str]]", DelAttrCall);
        }

        internal Task<IAnalysisSet> ReturnParameter1(CallSiteKey callSite, CancellationToken cancellationToken) {
            return callSite.GetArgValue(0, null, cancellationToken);
        }

        internal Task<IAnalysisSet> ReturnBool(CallSiteKey callSite, CancellationToken cancellationToken) {
            return Task.FromResult<IAnalysisSet>(Bool.Instance);
        }

        internal Task<IAnalysisSet> ReturnStr(CallSiteKey callSite, CancellationToken cancellationToken) {
            return Task.FromResult<IAnalysisSet>(Str.Instance);
        }

        private Task<IAnalysisSet> CompileCall(CallSiteKey callSite, CancellationToken cancellationToken) {
            return Task.FromResult<IAnalysisSet>(Code.Instance);
        }

        private async Task<IAnalysisSet> DelAttrCall(CallSiteKey callSite, CancellationToken cancellationToken) {
            var target = await callSite.GetArgValue(0, null, cancellationToken);
            var key = await callSite.GetArgValue(1, null, cancellationToken);
            // TODO: Mark key as deleted
            return None;
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

        internal CallableValue Abs => _abs;
        internal CallableValue All => _all;
        internal CallableValue Any => _any;
        internal CallableValue Ascii => _ascii;
        internal CallableValue Bin => _bin;
        internal CallableValue Callable => _callable;
        internal CallableValue Chr => _chr;
        internal CallableValue DelAttr => _delattr;

        public async override Task<IAnalysisSet> GetAttribute(
            IAnalysisState caller,
            string attribute,
            CancellationToken cancellationToken
        ) {
            return GetAttribute(attribute, caller != Key.State);
        }

        internal IAnalysisSet GetAttribute(string attribute, bool onlyImportable) {
            switch (attribute) {
                case "bool": return Bool;
                case "int": return Int;
                case "long": return Long;
                case "float": return Float;
                case "complex": return Complex;
                case "NoneType": return NoneType;
                case "str": return Key.State.Features.IsUnicodeCalledStr ? Str : Bytes;
                case "bytes": return Bytes;
                case "unicode": return Str;
                case "code": return onlyImportable ? AnalysisSet.Empty : Code;

                case "copyright": return Str.Instance;
                case "credits": return Str.Instance;

                case "abs": return Abs;
                case "all": return All;
                case "any": return Any;
                case "ascii": return Ascii;
                case "bin": return Ascii;
                case "callable": return Callable;
                case "chr": return Chr;
                case "compile": return _compile;
                case "delattr": return _delattr;
                default: return AnalysisSet.Empty;
            }
        }
    }
}
