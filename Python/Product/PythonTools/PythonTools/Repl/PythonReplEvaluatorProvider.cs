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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.Project;
using Microsoft.PythonTools.InteractiveWindow;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudioTools;

namespace Microsoft.PythonTools.Repl {
    [Export(typeof(IInteractiveEvaluatorProvider))]
    sealed class PythonReplEvaluatorProvider : IInteractiveEvaluatorProvider, IDisposable {
        private readonly IInterpreterRegistryService _interpreterService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IVsSolution _solution;
        private readonly SolutionEventsListener _solutionEvents;

        private const string _prefix = "E915ECDA-2F45-4398-9E07-15A877137F44";

        [ImportingConstructor]
        public PythonReplEvaluatorProvider(
            [Import] IInterpreterRegistryService interpreterService,
            [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider
        ) {
            Debug.Assert(interpreterService != null);
            _interpreterService = interpreterService;
            _serviceProvider = serviceProvider;
            _solution = (IVsSolution)_serviceProvider.GetService(typeof(SVsSolution));
            _solutionEvents = new SolutionEventsListener(_solution);
            _solutionEvents.ProjectLoaded += ProjectChanged;
            _solutionEvents.ProjectClosing += ProjectChanged;
            _solutionEvents.ProjectRenamed += ProjectChanged;
            _solutionEvents.SolutionOpened += SolutionChanged;
            _solutionEvents.SolutionClosed += SolutionChanged;
            _solutionEvents.StartListeningForChanges();
        }

        private void SolutionChanged(object sender, EventArgs e) {
            EvaluatorsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ProjectChanged(object sender, ProjectEventArgs e) {
            EvaluatorsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() {
            _solutionEvents.Dispose();
        }

        public event EventHandler EvaluatorsChanged;

        public IEnumerable<KeyValuePair<string, string>> GetEvaluators() {
            foreach (var interpreter in _interpreterService.Configurations) {
                yield return new KeyValuePair<string, string>(
                    interpreter.Description,
                    GetEvaluatorId(interpreter)
                );
            }

            var solution = _serviceProvider.GetService(typeof(SVsSolution)) as IVsSolution;
            if (solution != null) {
                foreach (var project in solution.EnumerateLoadedPythonProjects()) {
                    if (project.IsClosed || project.IsClosing) {
                        continue;
                    }
                    yield return new KeyValuePair<string, string>(
                        Strings.ReplProjectProjectCaption.FormatUI(project.Caption),
                        GetEvaluatorId(project)
                    );
                }
            }
        }

        internal static string GetEvaluatorId(InterpreterConfiguration config) {
            return "{0};env;{1};{2}".FormatInvariant(
                _prefix,
                config.Description,
                config.Id
            );
        }

        internal static string GetEvaluatorId(PythonProjectNode project) {
            return "{0};project;{1};{2}".FormatInvariant(
                _prefix,
                project.Caption,
                project.GetMkDocument()
            );
        }

        internal static string GetTemporaryId(string key, InterpreterConfiguration config) {
            return GetEvaluatorId(config) + ";" + key;
        }


        public IInteractiveEvaluator GetEvaluator(string evaluatorId) {
            if (string.IsNullOrEmpty(evaluatorId)) {
                return null;
            }

            // Max out at 10 splits to protect against malicious IDs
            var bits = evaluatorId.Split(new[] { ';' }, 10);

            if (bits.Length < 2 || !bits[0].Equals(_prefix, StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            if (bits[1].Equals("env", StringComparison.OrdinalIgnoreCase)) {
                return GetEnvironmentEvaluator(bits.Skip(2).ToArray());
            }

            if (bits[1].Equals("project", StringComparison.OrdinalIgnoreCase)) {
                return GetProjectEvaluator(bits.Skip(2).ToArray());
            }

            return null;
        }

        private IInteractiveEvaluator GetEnvironmentEvaluator(IReadOnlyList<string> args) {
            var config = _interpreterService.FindConfiguration(args.ElementAtOrDefault(1));

            var eval = new PythonInteractiveEvaluator(_serviceProvider) {
                DisplayName = args.ElementAtOrDefault(0),
                Configuration = new LaunchConfiguration(config)
            };

            return eval;
        }

        private IInteractiveEvaluator GetProjectEvaluator(IReadOnlyList<string> args) {
            var project = args.ElementAtOrDefault(1);

            var eval = new PythonInteractiveEvaluator(_serviceProvider) {
                DisplayName = args.ElementAtOrDefault(0),
                ProjectMoniker = project
            };

            eval.UpdatePropertiesFromProjectMoniker();

            return eval;
        }
    }

}
