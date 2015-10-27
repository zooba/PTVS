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

// Guids.cs
// MUST match guids.h
using System;

namespace Microsoft.PythonTools
{
    static class GuidList
    {
        public const string guidPythonToolsPkgString =    "6dbd7c1e-1f1b-496d-ac7c-c55dae66c783";
        public const string guidPythonToolsCmdSetString = "bdfa79d2-2cd2-474a-a82a-ce8694116825";
        public const string guidPythonProjectString = "888888A0-9F3D-457C-B088-3A5042F75D52";
        public const string guidPythonLanguageService = "bf96a6ce-574f-3259-98be-503a3ad636dd";
        public const string guidLoadedProjectInterpreterFactoryProviderString = "ADA20FE6-F50C-4ABC-A6F4-ED15EAF5A2FC";
        public const string guidPythonInteractiveWindow = "08A1BF1D-7967-4AFC-8253-1790862AB8C9";

        public static readonly Guid guidPythonToolsPackage = new Guid(guidPythonToolsPkgString);
        public static readonly Guid guidPythonToolsCmdSet = new Guid(guidPythonToolsCmdSetString);
        public static readonly Guid guidPythonProjectGuid = new Guid(guidPythonProjectString);
        public static readonly Guid guidPythonLanguageServiceGuid = new Guid(guidPythonLanguageService);
        public static readonly Guid guidCSharpProjectPacakge = new Guid("FAE04EC1-301F-11D3-BF4B-00C04F79EFBC");
        public static readonly Guid guidPythonInteractiveWindowGuid = new Guid(guidPythonInteractiveWindow);

        public static readonly Guid guidVenusCmdId = new Guid("c7547851-4e3a-4e5b-9173-fa6e9c8bd82c");
        public static readonly Guid guidWebPackgeCmdId = new Guid("822e3603-e573-47d2-acf0-520e4ce641c2");
        public static readonly Guid guidWebPackageGuid = new Guid("d9a342d1-a429-4059-808a-e55ee6351f7f");
        public static readonly Guid guidWebAppCmdId = new Guid("CB26E292-901A-419c-B79D-49BD45C43929");
        public static readonly Guid guidEureka = new Guid("30947ebe-9147-45f9-96cf-401bfc671a82");  //  Microsoft.VisualStudio.Web.Eureka.dll package, includes page inspector
        
        public static readonly Guid guidOfficeSharePointCmdSet = new Guid("d26c976c-8ee8-4ec4-8746-f5f7702a17c5");
    };
}
