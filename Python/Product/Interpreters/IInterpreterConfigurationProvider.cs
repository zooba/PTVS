using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Interpreter {
    public interface IInterpreterConfigurationProvider {
        void Initialize();
        IReadOnlyList<InterpreterConfiguration> GetInterpreters();
        event EventHandler InterpretersChanged;
    }
}
