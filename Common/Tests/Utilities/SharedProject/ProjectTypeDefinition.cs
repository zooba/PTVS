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


namespace TestUtilities.SharedProject {
    /// <summary>
    /// Defines a project type definition, an instance of this gets exported:
    /// 
    /// [Export]
    /// [ProjectExtension(".njsproj")]                            // required
    /// [ProjectTypeGuid("577B58BB-F149-4B31-B005-FC17C8F4CE7C")] // required
    /// [CodeExtension(".js")]                                    // required
    /// [SampleCode("console.log('hi');")]                        // optional
    /// internal static ProjectTypeDefinition ProjectTypeDefinition = new ProjectTypeDefinition();
    /// </summary>
    public sealed class ProjectTypeDefinition {
    }
}
