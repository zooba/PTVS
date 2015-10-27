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
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudioTools;

namespace TestUtilities.Python {
    public class PythonTestData {
#if DEBUG
        const string Configuration = "Debug";
#else
        const string Configuration = "Release";
#endif

        const string BinariesInSourceTree = "BuildOutput\\" + Configuration + AssemblyVersionInfo.VSVersion + "\\raw\\binaries";
        const string BinariesInTestDrop = "binaries";
        const string BinariesInReleaseDrop = "raw\\binaries";
        const string BinariesLandmark = "Microsoft.PythonTools.Analysis.dll";

        const string TestDataInSourceTree = "Python\\Tests\\TestData";
        const string TestDataInTestDrop = "binaries\\TestData";
        const string TestDataInReleaseDrop = "raw\\binaries\\TestData";
        const string TestDataLandmark = "testdata.root";

        private static string FindDirectoryFromLandmark(string root, string directory, string landmark = null) {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(directory)) {
                return null;
            }

            var path = CommonUtils.GetAbsoluteDirectoryPath(root, directory);
            if (landmark != null) {
                return File.Exists(CommonUtils.GetAbsoluteFilePath(path, landmark)) ? path : null;
            }
            return Directory.Exists(path) ? path : null;
        }

        private static string GetRootDir() {
            var dir = CommonUtils.GetParent((typeof(TestData)).Assembly.Location);
            while (!string.IsNullOrEmpty(dir) &&
                Directory.Exists(dir) &&
                !File.Exists(CommonUtils.GetAbsoluteFilePath(dir, "build.root"))) {
                dir = CommonUtils.GetParent(dir);
            }
            return dir ?? "";
        }

        public static string GetBinariesSourcePath(string subPath = null) {
            var binSource = Environment.GetEnvironmentVariable("PTVS_BINARIES_SOURCE");
            var drop = Environment.GetEnvironmentVariable("PTVS_DROP") ??
                CommonUtils.GetParent(CommonUtils.GetParent(typeof(TestData).Assembly.Location));

            if (string.IsNullOrEmpty(binSource)) {
                binSource = FindDirectoryFromLandmark(GetRootDir(), BinariesInSourceTree, BinariesLandmark)
                    ?? FindDirectoryFromLandmark(drop, BinariesInTestDrop, BinariesLandmark)
                    ?? FindDirectoryFromLandmark(drop, BinariesInReleaseDrop, BinariesLandmark);
            }

            if (!string.IsNullOrEmpty(subPath)) {
                return CommonUtils.GetAbsoluteFilePath(binSource, subPath);
            }
            return binSource;
        }

        public static string GetTestDataSourcePath(string subPath = null) {
            var testDataSource = Environment.GetEnvironmentVariable("PTVS_TESTDATA_SOURCE");
            var drop = Environment.GetEnvironmentVariable("PTVS_DROP") ??
                CommonUtils.GetParent(CommonUtils.GetParent(typeof(TestData).Assembly.Location));

            if (string.IsNullOrEmpty(testDataSource)) {
                testDataSource = FindDirectoryFromLandmark(GetRootDir(), TestDataInSourceTree, TestDataLandmark)
                    ?? FindDirectoryFromLandmark(drop, TestDataInTestDrop, TestDataLandmark)
                    ?? FindDirectoryFromLandmark(drop, TestDataInReleaseDrop, TestDataLandmark);
            }

            if (!string.IsNullOrEmpty(subPath)) {
                return CommonUtils.GetAbsoluteFilePath(testDataSource, subPath);
            }
            return testDataSource;
        }

        public static void Deploy(bool includeTestData = true) {
            var binSource = GetBinariesSourcePath();
            Debug.Assert(Directory.Exists(binSource), "Unable to find binaries at " + (binSource ?? "(null)"));

            Trace.TraceInformation("Copying binaries from {0}", binSource);

            FileUtils.CopyDirectory(binSource, TestData.GetPath());

            if (includeTestData) {
                var testDataSource = GetTestDataSourcePath();
                Debug.Assert(Directory.Exists(testDataSource), "Unable to find test data at " + (testDataSource ?? "(null)"));
                FileUtils.CopyDirectory(testDataSource, TestData.GetPath("TestData"));
            }
        }
    }
}
