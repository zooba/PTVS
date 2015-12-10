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
        private readonly TypeValue _noneType;
        private readonly NumericValue _bool, _int, _long, _float;
        private readonly StringValue _bytes, _str;

        private readonly BuiltinFunctionValue _abs;

        public BuiltinsModule(VariableKey key, string fullname, string name, string moniker) :
            base(key, fullname, name, moniker) {
            _noneType = new TypeValue(Key + ".NoneType", "None");
            _bool = new NumericValue(Key + ".bool", "bool");
            _int = new NumericValue(Key + ".int", "int");
            _long = Key.State.Features.HasLong ? new NumericValue(Key + ".long", "long") : _int;
            _float = new NumericValue(Key + ".float", "float");
            _bytes = new StringValue(Key, false);
            _str = new StringValue(Key, true);

            _abs = new BuiltinFunctionValue(Key + ".abs", "Callable[T, T]", AbsCall);
        }

        private Task<IAnalysisSet> AbsCall(
            IAnalysisState caller,
            VariableKey callSite,
            CancellationToken cancellationToken
        ) {
            return BuiltinFunctionValue.GetArgValue(caller, callSite, 0, null, cancellationToken);
        }

        internal AnalysisValue None => NoneType.Instance;
        internal TypeValue NoneType => _noneType;
        internal NumericValue Bool => _bool;
        internal NumericValue Int => _int;
        internal NumericValue Long => _long;
        internal NumericValue Float => _float;
        internal StringValue Bytes => _bytes;
        internal StringValue Str => _str;

        internal CallableValue Abs => _abs;

        public async override Task<IAnalysisSet> GetAttribute(
            IAnalysisState caller,
            string attribute,
            CancellationToken cancellationToken
        ) {
            return GetAttribute(attribute);
        }

        internal IAnalysisSet GetAttribute(string attribute) {
            switch (attribute) {
                case "bool": return Bool;
                case "int": return Int;
                case "long": return Long;
                case "float": return Float;
                case "NoneType": return NoneType;
                case "str": return Key.State.Features.IsUnicodeCalledStr ? Str : Bytes;
                case "bytes": return Bytes;
                case "unicode": return Str;

                case "abs": return Abs;
                default: return null;
            }
        }
    }
}
