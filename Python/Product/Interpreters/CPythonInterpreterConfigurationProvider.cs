﻿/* ****************************************************************************
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
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Analysis.Parsing;
using Microsoft.VisualStudioTools;
using Microsoft.Win32;

namespace Microsoft.PythonTools.Interpreter {
    [Export(typeof(IInterpreterConfigurationProvider))]
    class CPythonInterpreterConfigurationProvider : IInterpreterConfigurationProvider {
        private readonly List<InterpreterConfiguration> _interpreters;
        const string PythonPath = "Software\\Python";
        const string PythonCorePath = "Software\\Python\\PythonCore";

        public CPythonInterpreterConfigurationProvider() {
            _interpreters = new List<InterpreterConfiguration>();
        }

        public void Initialize() {
            DiscoverInterpreterFactories();

            StartWatching(RegistryHive.CurrentUser, RegistryView.Default);
            StartWatching(RegistryHive.LocalMachine, RegistryView.Registry32);
            if (Environment.Is64BitOperatingSystem) {
                StartWatching(RegistryHive.LocalMachine, RegistryView.Registry64);
            }
        }

        private void StartWatching(RegistryHive hive, RegistryView view, int retries = 5) {
            var tag = RegistryWatcher.Instance.TryAdd(
                hive, view, PythonCorePath, Registry_PythonCorePath_Changed,
                recursive: true, notifyValueChange: true, notifyKeyChange: true
            ) ??
            RegistryWatcher.Instance.TryAdd(
                hive, view, PythonPath, Registry_PythonPath_Changed,
                recursive: false, notifyValueChange: false, notifyKeyChange: true
            ) ??
            RegistryWatcher.Instance.TryAdd(
                hive, view, "Software", Registry_Software_Changed,
                recursive: false, notifyValueChange: false, notifyKeyChange: true
            );

            if (tag == null && retries > 0) {
                Trace.TraceWarning("Failed to watch registry. Retrying {0} more times", retries);
                Thread.Sleep(100);
                StartWatching(hive, view, retries - 1);
            } else if (tag == null) {
                Trace.TraceError("Failed to watch registry");
            }
        }

        #region Registry Watching

        private static bool Exists(RegistryChangedEventArgs e) {
            using (var root = RegistryKey.OpenBaseKey(e.Hive, e.View))
            using (var key = root.OpenSubKey(e.Key)) {
                return key != null;
            }
        }

        private void Registry_PythonCorePath_Changed(object sender, RegistryChangedEventArgs e) {
            if (!Exists(e)) {
                // PythonCore key no longer exists, so go back to watching
                // Python.
                e.CancelWatcher = true;
                StartWatching(e.Hive, e.View);
            } else {
                DiscoverInterpreterFactories();
            }
        }

        private void Registry_PythonPath_Changed(object sender, RegistryChangedEventArgs e) {
            if (Exists(e)) {
                if (RegistryWatcher.Instance.TryAdd(
                    e.Hive, e.View, PythonCorePath, Registry_PythonCorePath_Changed,
                    recursive: true, notifyValueChange: true, notifyKeyChange: true
                ) != null) {
                    // PythonCore key now exists, so start watching it,
                    // discover any interpreters, and cancel this watcher.
                    e.CancelWatcher = true;
                    DiscoverInterpreterFactories();
                }
            } else {
                // Python key no longer exists, so go back to watching
                // Software.
                e.CancelWatcher = true;
                StartWatching(e.Hive, e.View);
            }
        }

        private void Registry_Software_Changed(object sender, RegistryChangedEventArgs e) {
            Registry_PythonPath_Changed(sender, e);
            if (e.CancelWatcher) {
                // PythonCore key also exists and is now being watched, so just
                // return.
                return;
            }

            if (RegistryWatcher.Instance.TryAdd(
                e.Hive, e.View, PythonPath, Registry_PythonPath_Changed,
                recursive: false, notifyValueChange: false, notifyKeyChange: true
            ) != null) {
                // Python exists, but not PythonCore, so watch Python until
                // PythonCore is created.
                e.CancelWatcher = true;
            }
        }

        #endregion

        private static bool TryParsePythonVersion(
            string spec,
            out PythonLanguageVersion? version,
            out ProcessorArchitecture? arch
        ) {
            version = null;
            arch = null;

            if (string.IsNullOrEmpty(spec) || spec.Length < 3) {
                return false;
            }

            var m = Regex.Match(spec, @"^(?<ver>[23]\.[0-9]+)(?<suffix>.*)$");
            if (!m.Success) {
                return false;
            }

            Version ver;
            if (!Version.TryParse(m.Groups["ver"].Value, out ver)) {
                return false;
            }
            version = ver.ToLanguageVersion();

            if (m.Groups["suffix"].Value == "-32") {
                arch = ProcessorArchitecture.X86;
            }

            return true;
        }

        private bool RegisterInterpreters(HashSet<string> registeredPaths, RegistryKey python, ProcessorArchitecture? arch) {
            bool anyAdded = false;

            string[] subKeyNames = null;
            for (int retries = 5; subKeyNames == null && retries > 0; --retries) {
                try {
                    subKeyNames = python.GetSubKeyNames();
                } catch (IOException) {
                    // Registry changed while enumerating subkeys. Give it a
                    // short period to settle down and try again.
                    // We are almost certainly being called from a background
                    // thread, so sleeping here is fine.
                    Thread.Sleep(100);
                }
            }
            if (subKeyNames == null) {
                return false;
            }

            foreach (var key in subKeyNames) {
                PythonLanguageVersion? version;
                ProcessorArchitecture? arch2;
                if (!TryParsePythonVersion(key, out version, out arch2)) {
                    continue;
                }
                var installPath = python.OpenSubKey(key + "\\InstallPath");
                if (installPath == null) {
                    continue;
                }
                var basePathObj = installPath.GetValue("");
                if (basePathObj == null) {
                    // http://pytools.codeplex.com/discussions/301384
                    // messed up install, we don't know where it lives, we can't use it.
                    continue;
                }
                string basePath = basePathObj.ToString();
                if (!CommonUtils.IsValidPath(basePath)) {
                    // Invalid path in registry
                    continue;
                }
                if (!registeredPaths.Add(basePath)) {
                    // registered in both HCKU and HKLM
                    continue;
                }

                var actualArch = arch ?? arch2;
                if (!actualArch.HasValue) {
                    actualArch = NativeMethods.GetBinaryType(Path.Combine(basePath, CPythonInterpreterFactoryConstants.ConsoleExecutable));
                }

                var id = string.Format("{0}-{1}",
                    actualArch == ProcessorArchitecture.Amd64
                    ? CPythonInterpreterFactoryConstants.Guid64
                    : CPythonInterpreterFactoryConstants.Guid32,
                    version.Value.ToVersion()
                );

                var description = string.Format("{0} {1}",
                    actualArch == ProcessorArchitecture.Amd64
                    ? CPythonInterpreterFactoryConstants.Description64
                    : CPythonInterpreterFactoryConstants.Description32,
                    version.Value.ToVersion()
                );

                var config = new InterpreterConfiguration(
                    id,
                    description,
                    basePath,
                    Path.Combine(basePath, CPythonInterpreterFactoryConstants.ConsoleExecutable),
                    Path.Combine(basePath, CPythonInterpreterFactoryConstants.WindowsExecutable),
                    Enumerable.Empty<string>(),
                    CPythonInterpreterFactoryConstants.PathEnvironmentVariableName,
                    actualArch ?? ProcessorArchitecture.None,
                    version ?? PythonLanguageVersion.None,
                    InterpreterUIMode.Normal | InterpreterUIMode.CannotBeConfigured
                );

                lock (_interpreters) {
                    if (!_interpreters.Contains(config)) {
                        // TODO: Property determine sys.path
                        config.SysPath = new[] {
                            Path.Combine(basePath, CPythonInterpreterFactoryConstants.LibrarySubPath)
                        };

                        _interpreters.Add(config);
                        anyAdded = true;
                    }
                }
            }

            return anyAdded;
        }

        private void DiscoverInterpreterFactories() {
            bool anyAdded = false;
            HashSet<string> registeredPaths = new HashSet<string>();
            var arch = Environment.Is64BitOperatingSystem ? null : (ProcessorArchitecture?)ProcessorArchitecture.X86;
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
            using (var python = baseKey.OpenSubKey(PythonCorePath)) {
                if (python != null) {
                    anyAdded |= RegisterInterpreters(registeredPaths, python, arch);
                }
            }

            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            using (var python = baseKey.OpenSubKey(PythonCorePath)) {
                if (python != null) {
                    anyAdded |= RegisterInterpreters(registeredPaths, python, ProcessorArchitecture.X86);
                }
            }

            if (Environment.Is64BitOperatingSystem) {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var python64 = baseKey.OpenSubKey(PythonCorePath)) {
                    if (python64 != null) {
                        anyAdded |= RegisterInterpreters(registeredPaths, python64, ProcessorArchitecture.Amd64);
                    }
                }
            }

            if (anyAdded) {
                OnInterpretersChanged();
            }
        }

        public IReadOnlyList<InterpreterConfiguration> GetInterpreters() {
            lock (_interpreters) {
                return _interpreters.ToList();
            }
        }

        public event EventHandler InterpretersChanged;

        private void OnInterpretersChanged() {
            InterpretersChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
