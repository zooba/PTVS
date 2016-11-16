﻿// Python Tools for Visual Studio
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
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.Telemetry;

namespace Microsoft.PythonTools.Logging {
    /// <summary>
    /// Implements telemetry recording in Visual Studio environment
    /// </summary>
    [Export(typeof(IPythonToolsLogger))]
    internal sealed class VsTelemetryLogger : IPythonToolsLogger {
        private readonly Lazy<TelemetrySession> _session = new Lazy<TelemetrySession>(() => TelemetryService.DefaultSession);

        private const string EventPrefix = "vs/python/";
        private const string PropertyPrefix = "VS.Python.";

        public void LogEvent(PythonLogEvent logEvent, object argument) {
            var session = _session.Value;
            // No session is not a fatal error
            if (session == null) {
                return;
            }

            // Never send events when users have not opted in.
            if (!session.IsOptedIn) {
                return;
            }

            var evt = new TelemetryEvent(EventPrefix + logEvent.ToString());
            var props = PythonToolsLoggerData.AsDictionary(argument);
            if (props != null) {
                foreach (var kv in props) {
                    evt.Properties[PropertyPrefix + kv.Key] = kv.Value;
                }
            } else if (argument != null) {
                evt.Properties[PropertyPrefix + "Value"] = argument;
            }

            session.PostEvent(evt);
        }
    }
}
