using System;
using System.Runtime.Serialization;

namespace Microsoft.PythonTools.Analysis.Parsing {
    [Serializable]
    public class ParseErrorException : Exception {
        public ParseErrorException() { }
        public ParseErrorException(string message, SourceLocation location) : base(message) {
            Data["Location"] = location;
        }
        public ParseErrorException(string message, Exception inner) : base(message, inner) { }

        public ParseErrorException(string message, Exception inner, SourceLocation location) : base(message, inner) {
            Data["Location"] = location;
        }
        protected ParseErrorException(SerializationInfo info, StreamingContext context) : base(info, context) { }

        public SourceLocation Location {
            get { return (SourceLocation)Data["Location"]; }
        }
    }
}
