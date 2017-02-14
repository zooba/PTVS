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
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.PythonTools.Debugger.Transports;
using Microsoft.PythonTools.DkmDebugger;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;

namespace Microsoft.PythonTools.Debugger.Remote {

    [ComVisible(true)]
    [Guid(Guids.RemoteDebugPortSupplierCLSID)]
    public class PythonRemoteDebugPortSupplier : IDebugPortSupplier2, IDebugPortSupplierDescription2 {
        public const string PortSupplierId = "{FEB76325-D127-4E02-B59D-B16D93D46CF5}";
        public static readonly Guid PortSupplierGuid = new Guid(PortSupplierId);

        // Qualifier for our transport has one of the following formats:
        //
        //   tcp[s]://[secret@]hostname[:port]
        //   ws[s]://[secret@]hostname[:port][/path]
        //   [secret@]hostname[:port]
        //
        // 'tcp' and 'tcps' are for connecting directly to ptvsd; 'ws' and 'wss' are for connecting through WebSocketProxy.
        // The versions ending with '...s' use SSL to secure the connection. If no scheme is specified, 'tcp' is the default.
        // If port is not specified, it defaults to 5678 for 'tcp' and 'tcps', 80 for 'ws' and 443 for 'wss'.
        public int AddPort(IDebugPortRequest2 pRequest, out IDebugPort2 ppPort) {
            ppPort = null;

            string name;
            pRequest.GetPortName(out name);

            // Support old-style 'hostname:port' format, as well.
            if (!name.Contains("://")) {
                name = "tcp://" + name;
            }

            var uri = new Uri(name, UriKind.Absolute);
            var transport = DebuggerTransportFactory.Get(uri);
            if (transport == null) {
                MessageBox.Show(
                    Strings.RemoteUnrecognizedDebuggingTransport.FormatUI(uri.Scheme),
                    Strings.ProductTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return VSConstants.E_FAIL;
            }

            var validationError = transport.Validate(uri);
            if (validationError != null) {
                return validationError.HResult;
            }

            var port = new PythonRemoteDebugPort(this, pRequest, uri);

            // Validate connection early. Debugger automation (DTE) objects are not consistent in error checking from this
            // point on, so errors reported from EnumProcesses and further calls may be ignored and treated as successes
            // (with empty result). Reporting an error here works around that.
            IEnumDebugProcesses2 processes;
            int hr = port.EnumProcesses(out processes);
            if (hr < 0) {
                return hr;
            }

            ppPort = port;
            return VSConstants.S_OK;
        }

        public int CanAddPort() {
            return VSConstants.S_OK; // S_OK = true, S_FALSE = false
        }

        public int EnumPorts(out IEnumDebugPorts2 ppEnum) {
            ppEnum = null;
            return VSConstants.S_OK;
        }

        public int GetPort(ref Guid guidPort, out IDebugPort2 ppPort) {
            ppPort = null;
            return VSConstants.E_NOTIMPL;
        }

        public int GetPortSupplierId(out Guid pguidPortSupplier) {
            pguidPortSupplier = PortSupplierGuid;
            return VSConstants.S_OK;
        }

        public int GetPortSupplierName(out string pbstrName) {
            // TODO: Localization - leave untranslated for now, unsure of impact
            pbstrName = "Python remote (ptvsd)";
            return VSConstants.S_OK;
        }

        public int RemovePort(IDebugPort2 pPort) {
            return VSConstants.S_OK;
        }

        public int GetDescription(enum_PORT_SUPPLIER_DESCRIPTION_FLAGS[] pdwFlags, out string pbstrText) {
            pbstrText = Strings.RemoteDebugPortSupplierDescription;
            return VSConstants.S_OK;
        }
    }
}
