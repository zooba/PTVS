﻿// Visual Studio Shared Project
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
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudioTools.Project;
using Microsoft.VisualStudioTools.Project.Automation;
using VsShellUtil = Microsoft.VisualStudio.Shell.VsShellUtilities;

namespace Microsoft.VisualStudioTools {
    static class VsExtensions {
#if FALSE
        internal static ITrackingSpan CreateTrackingSpan(this IIntellisenseSession session, ITextBuffer buffer) {
            var triggerPoint = session.GetTriggerPoint(buffer);
            var position = session.GetTriggerPoint(buffer).GetPosition(session.TextView.TextSnapshot);

            var snapshot = buffer.CurrentSnapshot;
            if (position == snapshot.Length) {
                return snapshot.CreateTrackingSpan(position, 0, SpanTrackingMode.EdgeInclusive);
            } else {
                return snapshot.CreateTrackingSpan(position, 1, SpanTrackingMode.EdgeInclusive);
            }
        }
#endif
        internal static EnvDTE.Project GetProject(this IVsHierarchy hierarchy) {
            object project;

            int hr = hierarchy.GetProperty(
                VSConstants.VSITEMID_ROOT,
                (int)__VSHPROPID.VSHPROPID_ExtObject,
                out project
            );

            Debug.Assert(ErrorHandler.Succeeded(hr), string.Format("unexpected HR={0:X08}", hr));
            ErrorHandler.ThrowOnFailure(hr);

            return (project as EnvDTE.Project);
        }

        public static CommonProjectNode GetCommonProject(this EnvDTE.Project project) {
            OAProject oaProj = project as OAProject;
            if (oaProj != null) {
                var common = oaProj.Project as CommonProjectNode;
                if (common != null) {
                    return common;
                }
            }
            return null;
        }

        public static string GetRootCanonicalName(this IVsProject project) {
            return ((IVsHierarchy)project).GetRootCanonicalName();
        }

        public static string GetRootCanonicalName(this IVsHierarchy heirarchy) {
            string path;
            ErrorHandler.ThrowOnFailure(heirarchy.GetCanonicalName(VSConstants.VSITEMID_ROOT, out path));
            return path;
        }

        internal static T[] Append<T>(this T[] list, T item) {
            T[] res = new T[list.Length + 1];
            list.CopyTo(res, 0);
            res[res.Length - 1] = item;
            return res;
        }

        internal static IClipboardService GetClipboardService(this IServiceProvider serviceProvider) {
            return (IClipboardService)serviceProvider.GetService(typeof(IClipboardService));
        }

        internal static UIThreadBase GetUIThread(this IServiceProvider serviceProvider) {
            var uiThread = (UIThreadBase)serviceProvider.GetService(typeof(UIThreadBase));
            if (uiThread == null) {
                Trace.TraceWarning("Returning NoOpUIThread instance from GetUIThread");
#if DEBUG
                var shell = (IVsShell)serviceProvider.GetService(typeof(SVsShell));
                object shutdownStarted;
                if (shell != null &&
                    ErrorHandler.Succeeded(shell.GetProperty((int)__VSSPROPID6.VSSPROPID_ShutdownStarted, out shutdownStarted)) &&
                    !(bool)shutdownStarted) {
                    Debug.Fail("No UIThread service but shell is not shutting down");
                }
#endif
                return new NoOpUIThread();
            }
            return uiThread;
        }

        [Conditional("DEBUG")]
        // Available on serviceProvider so we can avoid the GetUIThread call on release builds
        public static void MustBeCalledFromUIThread(this IServiceProvider serviceProvider, string message = "Invalid cross-thread call") {
            serviceProvider.GetUIThread().MustBeCalledFromUIThread(message);
        }

        [Conditional("DEBUG")]
        // Available on serviceProvider so we can avoid the GetUIThread call on release builds
        public static void MustNotBeCalledFromUIThread(this IServiceProvider serviceProvider, string message = "Invalid cross-thread call") {
            serviceProvider.GetUIThread().MustNotBeCalledFromUIThread(message);
        }

        [Conditional("DEBUG")]
        public static void MustBeCalledFromUIThread(this UIThreadBase self, string message = "Invalid cross-thread call") {
            Debug.Assert(self is MockUIThreadBase || !self.InvokeRequired, message);
        }

        [Conditional("DEBUG")]
        public static void MustNotBeCalledFromUIThread(this UIThreadBase self, string message = "Invalid cross-thread call") {
            Debug.Assert(self is MockUIThreadBase || self.InvokeRequired, message);
        }


        /// <summary>
        /// Use the line ending of the first line for the line endings.  
        /// If we have no line endings (single line file) just use Environment.NewLine
        /// </summary>
        public static string GetNewLineText(ITextSnapshot snapshot) {
            // https://nodejstools.codeplex.com/workitem/1670 : override the GetNewLineCharacter as VS always returns '\r\n'
            // check on each format as the user could have changed line endings (manually or through advanced save options) since
            // the file was opened.
            if (snapshot.LineCount > 0 && snapshot.GetLineFromPosition(0).LineBreakLength > 0) {
                return snapshot.GetLineFromPosition(0).GetLineBreakText();
            } else {
                return Environment.NewLine;
            }
        }
    }
}
