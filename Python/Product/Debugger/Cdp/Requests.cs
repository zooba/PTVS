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

using System.Collections.Generic;

namespace Microsoft.PythonTools.Cdp {
    public abstract class Request {
        internal Data _data;

        protected Request(string command) {
            _data = new Data { command = command };
        }

        protected IDictionary<string, object> Arguments => _data.arguments;

        internal class Data {
            public string type = "request";
            public string command;
            public int seq;
            public Dictionary<string, object> arguments = new Dictionary<string, object>();
        }
    }

    public class InitializeRequest : Request {
        public InitializeRequest() : base("initialize") {
            LinesStartAtOne = true;
            ColumnsStartAtOne = true;
            PathFormat = "path";
        }

        public bool LinesStartAtOne {
            get { return (bool)Arguments["linesStartAt1"]; }
            set { Arguments["linesStartAt1"] = value; }
        }

        public bool ColumnsStartAtOne {
            get { return (bool)Arguments["columnsStartAt1"]; }
            set { Arguments["columnsStartAt1"] = value; }
        }

        public string PathFormat {
            get { return (string)Arguments["pathFormat"]; }
            set { Arguments["pathFormat"] = value; }
        }
    }

    public class EvaluateRequest : Request {
        public EvaluateRequest(string expression, int frameId = -1) : base("evaluate") {
            Expression = expression;
            FrameId = frameId;
        }

        public string Expression {
            get { return (string)Arguments["expression"]; }
            set { Arguments["expression"] = value; }
        }

        public int FrameId {
            get { return (int)Arguments["frameId"]; }
            set { Arguments["frameId"] = value; }
        }
    }

}
