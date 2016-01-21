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

    public class LaunchRequest : Request {
        public LaunchRequest() : base("launch") { }

        public string Code {
            get { return (string)Arguments["code"]; }
            set { Arguments["code"] = value; }
        }

        public string ScriptPath {
            get { return (string)Arguments["scriptPath"]; }
            set { Arguments["scriptPath"] = value; }
        }

        public string ModuleName {
            get { return (string)Arguments["moduleName"]; }
            set { Arguments["moduleName"] = value; }
        }

        public string ProcessPath {
            get { return (string)Arguments["processPath"]; }
            set { Arguments["processPath"] = value; }
        }

        public string ExtraArguments {
            get { return (string)Arguments["extraArguments"]; }
            set { Arguments["extraArguments"] = value; }
        }

        public int MaximumResultLength {
            get { return (int)Arguments["maximumResultLength"]; }
            set { Arguments["maximumResultLength"] = value; }
        }

        public bool IncludeMembers {
            get { return (bool)Arguments["includeMembers"]; }
            set { Arguments["includeMembers"] = value; }
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

        public int MaximumLength {
            get { return (int)Arguments["maximumLength"]; }
            set { Arguments["maximumLength"] = value; }
        }

        public bool AllowHooks {
            get { return (bool)Arguments["allowHooks"]; }
            set { Arguments["allowHooks"] = value; }
        }
    }

    public class ScopesRequest : Request {
        public ScopesRequest() : base("scopes") {
        }

        public int FrameId {
            get { return (int)Arguments["frameId"]; }
            set { Arguments["frameId"] = value; }
        }
    }

    public class VariablesRequest : Request {
        public VariablesRequest(int variableId) : base("variables") {
            VariablesReference = variableId;
        }

        public int VariablesReference {
            get { return (int)Arguments["variablesReference"]; }
            set { Arguments["variablesReference"] = value; }
        }

        public int MaximumLength {
            get { return (int)Arguments["maximumLength"]; }
            set { Arguments["maximumLength"] = value; }
        }
    }

    public class SetModuleRequest : Request {
        public SetModuleRequest(string module) : base("setModule") {
            Module = module;
        }

        public string Module {
            get { return (string)Arguments["module"]; }
            set { Arguments["module"] = value; }
        }
    }

    public class SourceRequest : Request {
        public SourceRequest() : base("source") {
        }

        public int SourceReference {
            get { return (int)Arguments["sourceReference"]; }
            set { Arguments["sourceReference"] = value; }
        }
    }
}
