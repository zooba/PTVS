/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.Win32;

namespace Microsoft.PythonTools.Interpreter {
    public sealed class InterpreterConfiguration : IEquatable<InterpreterConfiguration> {
        private const string DescriptionKey = "Description";
        private const string PrefixPathKey = "PrefixPath";
        private const string InterpreterPathKey = "InterpreterPath";
        private const string WindowsPathKey = "WindowsInterpreterPath";
        private const string SysPathKey = "SysPath";
        private const string ArchitectureKey = "Architecture";
        private const string VersionKey = "Version";
        private const string PathEnvVarKey = "PathEnvironmentVariable";

        private string _key;
        private string _name;
        private string _prefixPath;
        private string _interpreterPath;
        private string _windowsInterpreterPath;
        private string[] _sysPath;
        private string _pathEnvironmentVariable;
        private ProcessorArchitecture _architecture;
        private PythonLanguageVersion _version;
        private InterpreterUIMode _uiMode;

        private static string AsString(ProcessorArchitecture arch) {
            if (arch == ProcessorArchitecture.Amd64) {
                return "x64";
            } else if (arch == ProcessorArchitecture.Arm) {
                return "ARM";
            }
            return "x86";
        }

        private static string AsString(PythonLanguageVersion version) {
            return version.ToString();
        }

        private static ProcessorArchitecture AsArchitecture(string str) {
            if (str == "x64") {
                return ProcessorArchitecture.Amd64;
            } else if (str == "ARM") {
                return ProcessorArchitecture.Arm;
            }
            return ProcessorArchitecture.X86;
        }

        private static PythonLanguageVersion AsVersion(string str) {
            try {
                return (PythonLanguageVersion)Enum.Parse(typeof(PythonLanguageVersion), str, true);
            } catch (FormatException) {
            } catch (InvalidCastException) {
            }
            return PythonLanguageVersion.None;
        }

        public InterpreterConfiguration() { }

        /// <summary>
        /// <para>Constructs a new interpreter configuration based on the
        /// provided values.</para>
        /// <para>No validation is performed on the parameters.</para>
        /// <para>If winPath is null or empty,
        /// <see cref="WindowsInterpreterPath"/> will be set to path.</para>
        /// <para>If libraryPath is null or empty and prefixPath is a valid
        /// file system path, <see cref="LibraryPath"/> will be set to
        /// prefixPath plus "Lib".</para>
        /// </summary>
        public InterpreterConfiguration(
            string key,
            string name,
            string prefixPath,
            string path,
            string winPath,
            IEnumerable<string> sysPath,
            string pathVar,
            ProcessorArchitecture arch,
            PythonLanguageVersion version,
            InterpreterUIMode uiMode
        ) {
            _key = key;
            _name = name;
            _prefixPath = prefixPath;
            _interpreterPath = path;
            _windowsInterpreterPath = string.IsNullOrEmpty(winPath) ? path : winPath;
            _sysPath = sysPath.ToArray();
            _pathEnvironmentVariable = pathVar;
            _architecture = arch;
            _version = version;
            Debug.Assert(string.IsNullOrEmpty(_interpreterPath) || !string.IsNullOrEmpty(_prefixPath),
                "Anyone providing an interpreter should also specify the prefix path");
            _uiMode = uiMode;
        }

        public string Name {
            get { return _name; }
            set { _name = value; }
        }

        public string Key {
            get { return _key; }
            set { _key = value; }
        }

        /// <summary>
        /// Returns the prefix path of the Python installation. All files
        /// related to the installation should be underneath this path.
        /// </summary>
        public string PrefixPath {
            get { return _prefixPath; }
            set { _prefixPath = value; }
        }

        /// <summary>
        /// Returns the path to the interpreter executable for launching Python
        /// applications.
        /// </summary>
        public string InterpreterPath {
            get { return _interpreterPath; }
            set { _interpreterPath = value; }
        }

        /// <summary>
        /// Returns the path to the interpreter executable for launching Python
        /// applications which are windows applications (pythonw.exe, ipyw.exe).
        /// </summary>
        public string WindowsInterpreterPath {
            get { return _windowsInterpreterPath; }
            set { _windowsInterpreterPath = value; }
        }

        /// <summary>
        /// The paths to the standard library associated with this interpreter.
        /// </summary>
        public IReadOnlyList<string> SysPath {
            get { return _sysPath; }
            set { _sysPath = value.ToArray(); }
        }

        /// <summary>
        /// Gets the environment variable which should be used to set sys.path.
        /// </summary>
        public string PathEnvironmentVariable {
            get { return _pathEnvironmentVariable; }
            set { _pathEnvironmentVariable = value; }
        }

        /// <summary>
        /// The architecture of the interpreter executable.
        /// </summary>
        public ProcessorArchitecture Architecture {
            get { return _architecture; }
            set { _architecture = value; }
        }

        /// <summary>
        /// The language version of the interpreter (e.g. 2.7).
        /// </summary>
        public PythonLanguageVersion Version {
            get { return _version; }
            set { _version = value; }
        }

        /// <summary>
        /// The UI behavior of the interpreter.
        /// </summary>
        /// <remarks>
        /// New in 2.2
        /// </remarks>
        public InterpreterUIMode UIMode {
            get { return _uiMode; }
            set { _uiMode = value; }
        }


        public void SaveToRegistry(RegistryKey interpretersKey) {
            using (var key = interpretersKey.CreateSubKey(Key, true)) {
                key.SetValue(DescriptionKey, Name);
                key.SetValue(PrefixPathKey, PrefixPath);
                key.SetValue(InterpreterPathKey, InterpreterPath);
                key.SetValue(WindowsPathKey, WindowsInterpreterPath);
                key.SetValue(SysPathKey, string.Join(";", SysPath));
                key.SetValue(ArchitectureKey, AsString(Architecture));
                key.SetValue(VersionKey, AsString(Version));
            }
        }

        public void LoadFromRegistry(RegistryKey interpretersKey) {
            using (var key = interpretersKey.OpenSubKey(Key)) {
                if (key == null) {
                    return;
                }

                if (string.IsNullOrEmpty(Name)) {
                    Name = key.GetValue(DescriptionKey) as string;
                }
                if (string.IsNullOrEmpty(PrefixPath)) {
                    PrefixPath = key.GetValue(PrefixPathKey) as string;
                }
                if (string.IsNullOrEmpty(InterpreterPath)) {
                    InterpreterPath = key.GetValue(InterpreterPathKey) as string;
                }
                if (string.IsNullOrEmpty(WindowsInterpreterPath)) {
                    WindowsInterpreterPath = key.GetValue(WindowsPathKey) as string;
                }
                if (SysPath.Count == 0) {
                    SysPath = (key.GetValue(SysPathKey) as string ?? "").Split(';');
                }
                if (Architecture == ProcessorArchitecture.None) {
                    Architecture = AsArchitecture(key.GetValue(ArchitectureKey) as string);
                }
                if (Version == PythonLanguageVersion.None) {
                    Version = AsVersion(key.GetValue(VersionKey) as string);
                }
            }
        }

        public static IEnumerable<InterpreterConfiguration> LoadAllFromRegistry(RegistryKey interpretersKey) {
            foreach (var keyName in interpretersKey.GetSubKeyNames()) {
                var c = new InterpreterConfiguration { Key = keyName };
                c.LoadFromRegistry(interpretersKey);
                yield return c;
            }
        }

        public override bool Equals(object obj) {
            return Equals(obj as InterpreterConfiguration);
        }

        public override int GetHashCode() {
            return Name.GetHashCode();
        }

        public bool Equals(InterpreterConfiguration other) {
            return other != null && Name == other.Name;
        }
    }
}
