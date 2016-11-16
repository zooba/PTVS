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

using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Construction;

namespace Microsoft.PythonTools.Project.ImportWizard {
    abstract class ProjectCustomization {
        public abstract string DisplayName {
            get;
        }

        public override string ToString() {
            return DisplayName;
        }

        public abstract void Process(
            ProjectRootElement project,
            Dictionary<string, ProjectPropertyGroupElement> groups
        );

        protected static void AddOrSetProperty(ProjectRootElement project, string name, string value) {
            bool anySet = false;
            foreach (var prop in project.Properties.Where(p => p.Name == name)) {
                prop.Value = value;
                anySet = true;
            }

            if (!anySet) {
                project.AddProperty(name, value);
            }
        }

        protected static void AddOrSetProperty(ProjectPropertyGroupElement group, string name, string value) {
            bool anySet = false;
            foreach (var prop in group.Properties.Where(p => p.Name == name)) {
                prop.Value = value;
                anySet = true;
            }

            if (!anySet) {
                group.AddProperty(name, value);
            }
        }
    }

    class DefaultProjectCustomization : ProjectCustomization {
        public static readonly ProjectCustomization Instance = new DefaultProjectCustomization();

        private DefaultProjectCustomization() { }

        public override string DisplayName {
            get {
                return Strings.ImportWizardDefaultProjectCustomization;
            }
        }

        public override void Process(
            ProjectRootElement project,
            Dictionary<string, ProjectPropertyGroupElement> groups
        ) {
            project.AddImport(@"$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)\Python Tools\Microsoft.PythonTools.targets");
        }
    }

    class BottleProjectCustomization : ProjectCustomization {
        public static readonly ProjectCustomization Instance = new BottleProjectCustomization();

        private BottleProjectCustomization() { }

        public override string DisplayName {
            get {
                return Strings.ImportWizardBottleProjectCustomization;
            }
        }

        public override void Process(
            ProjectRootElement project,
            Dictionary<string, ProjectPropertyGroupElement> groups
        ) {
            ProjectPropertyGroupElement globals;
            if (!groups.TryGetValue("Globals", out globals)) {
                globals = project.AddPropertyGroup();
            }

            AddOrSetProperty(globals, "ProjectTypeGuids", "{e614c764-6d9e-4607-9337-b7073809a0bd};{1b580a1a-fdb3-4b32-83e1-6407eb2722e6};{349c5851-65df-11da-9384-00065b846f21};{888888a0-9f3d-457c-b088-3a5042f75d52}");
            AddOrSetProperty(globals, "LaunchProvider", PythonConstants.WebLauncherName);
            AddOrSetProperty(globals, "WebBrowserUrl", "http://localhost");
            AddOrSetProperty(globals, "PythonDebugWebServerCommandArguments", "--debug $(CommandLineArguments)");
            AddOrSetProperty(globals, "PythonWsgiHandler", "{StartupModule}.wsgi_app()");

            project.AddImport(@"$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)\Python Tools\Microsoft.PythonTools.Web.targets");
        }
    }

    class DjangoProjectCustomization : ProjectCustomization {
        public static readonly ProjectCustomization Instance = new DjangoProjectCustomization();

        private DjangoProjectCustomization() { }

        public override string DisplayName {
            get {
                return Strings.ImportWizardDjangoProjectCustomization;
            }
        }

        public override void Process(
            ProjectRootElement project,
            Dictionary<string, ProjectPropertyGroupElement> groups
        ) {
            ProjectPropertyGroupElement globals;
            if (!groups.TryGetValue("Globals", out globals)) {
                globals = project.AddPropertyGroup();
            }

            AddOrSetProperty(globals, "StartupFile", "manage.py");
            AddOrSetProperty(globals, "ProjectTypeGuids", "{5F0BE9CA-D677-4A4D-8806-6076C0FAAD37};{349c5851-65df-11da-9384-00065b846f21};{888888a0-9f3d-457c-b088-3a5042f75d52}");
            AddOrSetProperty(globals, "LaunchProvider", "Django launcher");
            AddOrSetProperty(globals, "WebBrowserUrl", "http://localhost");

            project.AddImport(@"$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)\Python Tools\Microsoft.PythonTools.Django.targets");
        }
    }

    class FlaskProjectCustomization : ProjectCustomization {
        public static readonly ProjectCustomization Instance = new FlaskProjectCustomization();

        private FlaskProjectCustomization() { }

        public override string DisplayName {
            get {
                return Strings.ImportWizardFlaskProjectCustomization;
            }
        }

        public override void Process(
            ProjectRootElement project,
            Dictionary<string, ProjectPropertyGroupElement> groups
        ) {
            ProjectPropertyGroupElement globals;
            if (!groups.TryGetValue("Globals", out globals)) {
                globals = project.AddPropertyGroup();
            }

            AddOrSetProperty(globals, "ProjectTypeGuids", "{789894c7-04a9-4a11-a6b5-3f4435165112};{1b580a1a-fdb3-4b32-83e1-6407eb2722e6};{349c5851-65df-11da-9384-00065b846f21};{888888a0-9f3d-457c-b088-3a5042f75d52}");
            AddOrSetProperty(globals, "LaunchProvider", PythonConstants.WebLauncherName);
            AddOrSetProperty(globals, "WebBrowserUrl", "http://localhost");
            AddOrSetProperty(globals, "PythonWsgiHandler", "{StartupModule}.wsgi_app");

            project.AddImport(@"$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)\Python Tools\Microsoft.PythonTools.Web.targets");
        }
    }

    class GenericWebProjectCustomization : ProjectCustomization {
        public static readonly ProjectCustomization Instance = new GenericWebProjectCustomization();

        private GenericWebProjectCustomization() { }

        public override string DisplayName {
            get {
                return Strings.ImportWizardGenericWebProjectCustomization;
            }
        }

        public override void Process(
            ProjectRootElement project,
            Dictionary<string, ProjectPropertyGroupElement> groups
        ) {
            ProjectPropertyGroupElement globals;
            if (!groups.TryGetValue("Globals", out globals)) {
                globals = project.AddPropertyGroup();
            }

            AddOrSetProperty(globals, "ProjectTypeGuids", "{1b580a1a-fdb3-4b32-83e1-6407eb2722e6};{349c5851-65df-11da-9384-00065b846f21};{888888a0-9f3d-457c-b088-3a5042f75d52}");
            AddOrSetProperty(globals, "LaunchProvider", PythonConstants.WebLauncherName);
            AddOrSetProperty(globals, "WebBrowserUrl", "http://localhost");

            project.AddImport(@"$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)\Python Tools\Microsoft.PythonTools.Web.targets");
        }
    }

    class UwpProjectCustomization : ProjectCustomization {
        public static readonly ProjectCustomization Instance = new UwpProjectCustomization();

        private UwpProjectCustomization() { }

        public override string DisplayName {
            get {
                return Strings.ImportWizardUwpProjectCustomization;
            }
        }

        public override void Process(
            ProjectRootElement project,
            Dictionary<string, ProjectPropertyGroupElement> groups
        ) {
            ProjectPropertyGroupElement globals;
            if (!groups.TryGetValue("Globals", out globals)) {
                globals = project.AddPropertyGroup();
            }

            AddOrSetProperty(globals, "ProjectTypeGuids", "{2b557614-1a2b-4903-b9df-ed20d7b63f3a};{888888A0-9F3D-457C-B088-3A5042F75D52}");
        }
    }
}
