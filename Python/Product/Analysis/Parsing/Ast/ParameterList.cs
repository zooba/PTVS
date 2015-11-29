using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class ParameterList : Node {
        private IList<Parameter> _parameters;

        public ParameterList() { }

        protected override void OnFreeze() {
            base.OnFreeze();
            _parameters = FreezeList(_parameters);
        }

        public IList<Parameter> Parameters {
            get { return _parameters; }
            set { ThrowIfFrozen(); _parameters = value; }
        }

        public int Count => _parameters?.Count ?? 0;

        public Parameter this[int index] => _parameters?[index];

        internal void AddParameter(Parameter parameter) {
            if (_parameters == null) {
                _parameters = new List<Parameter>();
            }
            _parameters.Add(parameter);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_parameters != null) {
                    foreach (var p in _parameters) {
                        p.Walk(walker);
                    }
                }
            }
            walker.PostWalk(this);
        }
    }
}
