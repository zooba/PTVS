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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using Microsoft.PythonTools.Infrastructure;

namespace Microsoft.PythonTools.Logging {
    /// <summary>
    /// Keeps track of logged events and makes them available for display in the diagnostics window.
    /// </summary>
    [Export(typeof(IPythonToolsLogger))]
    [Export(typeof(InMemoryLogger))]
    class InMemoryLogger : IPythonToolsLogger {
        private int _installedInterpreters, _installedV2, _installedV3;
        private int _debugLaunchCount, _normalLaunchCount;
        private List<PackageInfo> _seenPackages = new List<PackageInfo>();
        private List<AnalysisInfo> _analysisInfo = new List<AnalysisInfo>();
        private List<string> _analysisAbnormalities = new List<string>();

        #region IPythonToolsLogger Members

        public void LogEvent(PythonLogEvent logEvent, object argument) {
            var dictArgument = argument as IDictionary<string, object>;

            switch (logEvent) {
                case PythonLogEvent.Launch:
                    if ((int)argument != 0) {
                        _debugLaunchCount++;
                    } else {
                        _normalLaunchCount++;
                    }
                    break;
                case PythonLogEvent.InstalledInterpreters:
                    _installedInterpreters = (int)dictArgument["Total"];
                    _installedV2 = (int)dictArgument["2x"];
                    _installedV3 = (int)dictArgument["3x"];
                    break;
                case PythonLogEvent.PythonPackage:
                    _seenPackages.Add(argument as PackageInfo);
                    break;
                case PythonLogEvent.AnalysisCompleted:
                    _analysisInfo.Add(argument as AnalysisInfo);
                    break;
                case PythonLogEvent.AnalysisExitedAbnormally:
                    _analysisAbnormalities.Add(DateTime.Now + " Abnormal exit: " + argument);
                    break;
                case PythonLogEvent.AnalysisOperationCancelled:
                    _analysisAbnormalities.Add(DateTime.Now + " Operation Cancelled");
                    break;
                case PythonLogEvent.AnalysisOperationFailed:
                    _analysisAbnormalities.Add(DateTime.Now + " Operation Failed " + argument);
                    break;
            }
        }

        #endregion

        public override string ToString() {
            StringBuilder res = new StringBuilder();
            res.AppendLine("Installed Interpreters: " + _installedInterpreters);
            res.AppendLine("    v2.x: " + _installedV2);
            res.AppendLine("    v3.x: " + _installedV3);
            res.AppendLine("Debug Launches: " + _debugLaunchCount);
            res.AppendLine("Normal Launches: " + _normalLaunchCount);
            res.AppendLine();

            if (_seenPackages.Any(p => p != null)) {
                res.AppendLine("Seen Packages:");
                foreach (var package in _seenPackages) {
                    if (package != null) {
                        res.AppendLine("    " + package.Name);
                    }
                }
                res.AppendLine();
            }

            if (_analysisInfo.Any(a => a != null)) {
                res.AppendLine("Completion DB analyses:");
                foreach (var analysis in _analysisInfo) {
                    if (analysis != null) {
                        res.AppendLine("    {0} - {1}s".FormatInvariant(analysis.InterpreterId, analysis.AnalysisSeconds));
                    }
                }
            }

            if (_analysisAbnormalities.Any()) {
                res.AppendFormat("Analysis abnormalities ({0}):", _analysisAbnormalities.Count);
                res.AppendLine();
                foreach (var abnormalExit in _analysisAbnormalities) {
                    res.AppendLine(abnormalExit);
                }
                res.AppendLine();
            }

            return res.ToString();
        }
    }
}
