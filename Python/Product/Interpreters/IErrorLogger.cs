using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Interpreter {
    public interface IErrorLogger {
        void LogInfo(string message, string source);

        void LogError(string message, string source);

        void LogErrorWithPath(string message, string source, string path);
    }
}
