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
using System.IO;
using System.Net.Security;
using System.Security.Authentication;

namespace Microsoft.PythonTools.Debugger.Transports {
    internal class TcpsTransport : TcpTransport {
        public override Stream Connect(Uri uri, bool requireAuthentication) {
            var rawStream = base.Connect(uri, requireAuthentication);
            try {
                var sslStream = new SslStream(rawStream, false, (sender, cert, chain, errs) => {
                    if (errs == SslPolicyErrors.None || !requireAuthentication) {
                        return true;
                    }

                    string errText = string.Format("Could not establish secure connection to {0} because of the following SSL issues:\n\n", uri);
                    if ((errs & SslPolicyErrors.RemoteCertificateNotAvailable) != 0) {
                        errText += "- no remote certificate provided\n";
                    }
                    if ((errs & SslPolicyErrors.RemoteCertificateNameMismatch) != 0) {
                        errText += "- remote certificate name does not match hostname\n";
                    }
                    if ((errs & SslPolicyErrors.RemoteCertificateChainErrors) != 0) {
                        errText += "- remote certificate is not trusted\n";
                    }

                    throw new AuthenticationException(errText);
                });

                sslStream.AuthenticateAsClient(uri.Host);
                rawStream = null;
                return sslStream;
            } catch (IOException ex) {
                throw new ConnectionException(ConnErrorMessages.RemoteNetworkError, ex);
            } finally {
                if (rawStream != null) {
                    rawStream.Dispose();
                }
            }
        }
    }
}
