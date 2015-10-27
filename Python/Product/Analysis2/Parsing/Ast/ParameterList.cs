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

#if DEBUG
        protected override void OnFreeze() {
            base.OnFreeze();
            _parameters = new ReadOnlyCollection<Parameter>(_parameters);
        }
#endif

        public IList<Parameter> Parameters {
            get { return _parameters; }
            set { ThrowIfFrozen(); _parameters = value; }
        }

        internal void AddParameter(Parameter parameter) {
            if (_parameters == null) {
                _parameters = new List<Parameter>();
            }
            _parameters.Add(parameter);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
            }
            walker.PostWalk(this);
        }
    }
}
