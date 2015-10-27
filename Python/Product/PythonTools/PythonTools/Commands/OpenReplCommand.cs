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
using System.Linq;
using Microsoft.PythonTools.Interpreter;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudio.InteractiveWindow.Shell;

namespace Microsoft.PythonTools.Commands {
    /// <summary>
    /// Provides the command for starting the Python REPL window.
    /// </summary>
    class OpenReplCommand : Command {
        private readonly int _cmdId;
        private readonly IPythonInterpreterFactory _factory;
        private readonly IServiceProvider _serviceProvider;

        public OpenReplCommand(IServiceProvider serviceProvider, int cmdId, IPythonInterpreterFactory factory) {
            _serviceProvider = serviceProvider;
            _cmdId = cmdId;
            _factory = factory;
        }

        public override void DoCommand(object sender, EventArgs e) {
            // _factory is never null, but if a specific factory or command line
            // is passed as an argument, use that instead.
            var factory = _factory;
            var oe = e as OleMenuCmdEventArgs;
            if (oe != null) {
                IPythonInterpreterFactory asFactory;
                string args;
                if ((asFactory = oe.InValue as IPythonInterpreterFactory) != null) {
                    factory = asFactory;
                } else if (!string.IsNullOrEmpty(args = oe.InValue as string)) {
                    string description;
                    var parse = _serviceProvider.GetService(typeof(SVsParseCommandLine)) as IVsParseCommandLine;
                    if (ErrorHandler.Succeeded(parse.ParseCommandTail(args, -1)) &&
                        ErrorHandler.Succeeded(parse.EvaluateSwitches("e,env,environment:")) &&
                        ErrorHandler.Succeeded(parse.GetSwitchValue(0, out description)) &&
                        !string.IsNullOrEmpty(description)
                    ) {
                        var service = _serviceProvider.GetComponentModel().GetService<IInterpreterOptionsService>();
                        asFactory = service.Interpreters.FirstOrDefault(
                            // Descriptions are localized strings, hence CCIC
                            f => description.Equals(f.Description, StringComparison.CurrentCultureIgnoreCase)
                        );
                        if (asFactory != null) {
                            factory = asFactory;
                        }
                    }
                }
            }

            // These commands are project-insensitive, so pass null for project.
            var window = (ToolWindowPane)ExecuteInReplCommand.EnsureReplWindow(_serviceProvider, factory, null);

            ((IVsInteractiveWindow)window).Show(true);
        }

        public override EventHandler BeforeQueryStatus {
            get {
                return QueryStatusMethod;
            }
        }

        private void QueryStatusMethod(object sender, EventArgs args) {
            var oleMenu = (OleMenuCommand)sender;

            oleMenu.ParametersDescription = "e,env,environment:";

            if (_factory == null) {
                oleMenu.Visible = false;
                oleMenu.Enabled = false;
                oleMenu.Supported = false;
            } else {
                oleMenu.Visible = true;
                oleMenu.Enabled = true;
                oleMenu.Supported = true;
                oleMenu.Text = Description;
            }
        }

        public string Description {
            get {
                return _factory.Description + " Interactive";
            }
        }
        
        public override int CommandId {
            get { return (int)_cmdId; }
        }
    }
}
