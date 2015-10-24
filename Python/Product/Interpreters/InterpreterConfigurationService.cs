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
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Primitives;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PythonTools.Interpreter.Properties;
using Microsoft.Win32;

namespace Microsoft.PythonTools.Interpreter {
    [Export(typeof(IInterpreterConfigurationService))]
    sealed class InterpreterConfigurationService : IInterpreterConfigurationService, IDisposable {
        // The second is a static registry entry for the local machine and/or
        // the current user (HKCU takes precedence), intended for being set by
        // other installers.
        private const string ProvidersRegKey = @"Software\Microsoft\PythonTools\" +
            AssemblyVersionInfo.VSVersion + @"\InterpreterFactories";

        private const string DefaultInterpreterRegKey = @"Software\Microsoft\PythonTools\Interpreters";
        private const string DefaultInterpreterSetting = "DefaultInterpreter";

        private IErrorLogger[] _loggers;

        private IInterpreterConfigurationProvider[] _providers;

        private readonly object _suppressInterpretersChangedLock = new object();
        private int _suppressInterpretersChanged;
        private bool _raiseInterpretersChanged;

        InterpreterConfiguration _defaultInterpreter;
        InterpreterConfiguration _noInterpretersValue;

        sealed class LockInfo : IDisposable {
            public int _lockCount;
            public readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

            public void Dispose() {
                _lock.Dispose();
            }
        }
        private Dictionary<InterpreterConfiguration, Dictionary<object, LockInfo>> _locks;
        private readonly object _locksLock = new object();

        [ImportingConstructor]
        public InterpreterConfigurationService(
            [ImportMany] IEnumerable<IErrorLogger> loggers,
            [ImportMany] IEnumerable<IInterpreterConfigurationProvider> providers
        ) {
            _loggers = loggers.ToArray();
            _providers = providers.ToArray();

            Initialize();
        }

        public void Dispose() {
            lock (_locksLock) {
                if (_locks != null) {
                    foreach (var dict in _locks.Values) {
                        foreach (var li in dict.Values) {
                            li.Dispose();
                        }
                    }
                    _locks = null;
                }
            }

            foreach (var provider in _providers.OfType<IDisposable>()) {
                provider.Dispose();
            }
        }

        private void InitializeDefaultInterpreterWatcher() {
            if (RegistryWatcher.Instance.TryAdd(
                RegistryHive.CurrentUser, RegistryView.Registry32, DefaultInterpreterRegKey,
                DefaultInterpreterRegistry_Changed,
                recursive: false, notifyValueChange: true, notifyKeyChange: false
            ) == null) {
                // DefaultInterpreterOptions subkey does not exist yet,
                // so create it and then start the watcher.
                SaveDefaultInterpreter();

                RegistryWatcher.Instance.Add(
                    RegistryHive.CurrentUser, RegistryView.Registry32, DefaultInterpreterRegKey,
                    DefaultInterpreterRegistry_Changed,
                    recursive: false, notifyValueChange: true, notifyKeyChange: false
                );
            }
        }

        private void Initialize() {
            BeginSuppressInterpretersChangedEvent();
            try {
                foreach (var provider in _providers) {
                    provider.InterpretersChanged += Provider_InterpretersChanged;
                    provider.Initialize();
                }

                LoadDefaultInterpreter(suppressChangeEvent: true);
                InitializeDefaultInterpreterWatcher();
            } finally {
                EndSuppressInterpretersChangedEvent();
            }
        }

        private void Provider_InterpretersChanged(object sender, EventArgs e) {
            lock (_suppressInterpretersChangedLock) {
                if (_suppressInterpretersChanged > 0) {
                    _raiseInterpretersChanged = true;
                    return;
                }
            }

            // May have removed the default interpreter, so select a new default
            if (FindInterpreter(DefaultInterpreter) == null) {
                DefaultInterpreter = Interpreters.LastOrDefault(c => c.UIMode.CanBeAutoDefault());
            }

            OnInterpretersChanged();
        }

        private void OnInterpretersChanged() {
            try {
                BeginSuppressInterpretersChangedEvent();
                for (bool repeat = true; repeat; repeat = _raiseInterpretersChanged, _raiseInterpretersChanged = false) {
                    var evt = InterpretersChanged;
                    if (evt != null) {
                        evt(this, EventArgs.Empty);
                    }
                }
            } finally {
                EndSuppressInterpretersChangedEvent();
            }
        }

        private void LogInformation(string message) {
            foreach (var log in _loggers) {
                log.LogInfo(message, Resources.ProductName);
            }
        }

        private void LogException(
            string message,
            string path,
            Exception ex,
            IEnumerable<object> data = null
        ) {
            var fullMessage = string.Format("{1}:{0}{2}{0}{3}",
                Environment.NewLine,
                message,
                ex,
                data == null ? string.Empty : string.Join(Environment.NewLine, data)
            ).Trim();

            if (string.IsNullOrEmpty(path)) {
                foreach (var log in _loggers) {
                    log.LogError(fullMessage, Resources.ProductName);
                }
            } else {
                foreach (var log in _loggers) {
                    log.LogErrorWithPath(fullMessage, Resources.ProductName, path);
                }
            }
        }

        // Used for testing.
        internal IInterpreterConfigurationProvider[] SetProviders(IInterpreterConfigurationProvider[] providers) {
            var oldProviders = _providers;
            _providers = providers;
            foreach (var p in oldProviders) {
                p.InterpretersChanged -= Provider_InterpretersChanged;
            }
            foreach (var p in providers) {
                p.InterpretersChanged += Provider_InterpretersChanged;
            }
            Provider_InterpretersChanged(this, EventArgs.Empty);
            return oldProviders;
        }

        public IEnumerable<InterpreterConfiguration> Interpreters {
            get {
                List<IReadOnlyList<InterpreterConfiguration>> configs;

                lock (_providers) {
                    configs = _providers.Select(p => p.GetInterpreters()).ToList();
                }
                return configs.SelectMany(c => c);
            }
        }

        public InterpreterConfiguration FindInterpreter(InterpreterConfiguration config) {
            return Interpreters.FirstOrDefault(c => c == config);
        }

        public InterpreterConfiguration FindInterpreter(string key) {
            return Interpreters.FirstOrDefault(c => c.Key == key);
        }

        public event EventHandler InterpretersChanged;

        private void DefaultInterpreterRegistry_Changed(object sender, RegistryChangedEventArgs e) {
            try {
                LoadDefaultInterpreter();
            } catch (Exception ex) {
                LogException("Exception updating default interpreter", null, ex);
            }
        }

        private void LoadDefaultInterpreter(bool suppressChangeEvent = false) {
            string key = null;

            using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)) {
                using (var subkey = hkcu.OpenSubKey(DefaultInterpreterRegKey))
                using (var subkey2 = subkey != null ? null : hklm.OpenSubKey(DefaultInterpreterRegKey)) {
                    var sk = subkey ?? subkey2;
                    if (sk != null) {
                        key = sk.GetValue(DefaultInterpreterSetting, "") as string;
                    }
                }
            }

            var newDefault = FindInterpreter(key) ??
                Interpreters.LastOrDefault(c => c.UIMode.CanBeAutoDefault());

            if (suppressChangeEvent) {
                _defaultInterpreter = newDefault;
            } else {
                DefaultInterpreter = newDefault;
            }
        }

        private void SaveDefaultInterpreter() {
            using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
            using (var subkey = hkcu.CreateSubKey(DefaultInterpreterRegKey)) {
                if (_defaultInterpreter == null) {
                    subkey.SetValue(DefaultInterpreterSetting, "");
                } else {
                    subkey.SetValue(DefaultInterpreterSetting, _defaultInterpreter.Key);
                }
            }
        }

        public InterpreterConfiguration DefaultInterpreter {
            get {
                return _defaultInterpreter ?? null;
            }
            set {
                var newDefault = value;
                if (newDefault != _defaultInterpreter) {
                    _defaultInterpreter = newDefault;
                    SaveDefaultInterpreter();

                    var evt = DefaultInterpreterChanged;
                    if (evt != null) {
                        evt(this, EventArgs.Empty);
                    }
                }
            }
        }

        public event EventHandler DefaultInterpreterChanged;

        public void BeginSuppressInterpretersChangedEvent() {
            lock (_suppressInterpretersChangedLock) {
                _suppressInterpretersChanged += 1;
            }
        }

        public void EndSuppressInterpretersChangedEvent() {
            bool shouldRaiseEvent = false;
            lock (_suppressInterpretersChangedLock) {
                _suppressInterpretersChanged -= 1;

                if (_suppressInterpretersChanged == 0 && _raiseInterpretersChanged) {
                    shouldRaiseEvent = true;
                    _raiseInterpretersChanged = false;
                }
            }

            if (shouldRaiseEvent) {
                OnInterpretersChanged();
            }
        }

        public IEnumerable<IInterpreterConfigurationProvider> KnownProviders {
            get {
                return _providers;
            }
        }

        public async Task<object> LockInterpreterAsync(InterpreterConfiguration config, object moniker, TimeSpan timeout) {
            LockInfo info;
            Dictionary<object, LockInfo> locks;

            lock (_locksLock) {
                if (_locks == null) {
                    _locks = new Dictionary<InterpreterConfiguration, Dictionary<object, LockInfo>>();
                }

                if (!_locks.TryGetValue(config, out locks)) {
                    _locks[config] = locks = new Dictionary<object, LockInfo>();
                }

                if (!locks.TryGetValue(moniker, out info)) {
                    locks[moniker] = info = new LockInfo();
                }
            }

            Interlocked.Increment(ref info._lockCount);
            bool result = false;
            try {
                result = await info._lock.WaitAsync(timeout.TotalDays > 1 ? Timeout.InfiniteTimeSpan : timeout);
                return result ? (object)info : null;
            } finally {
                if (!result) {
                    Interlocked.Decrement(ref info._lockCount);
                }
            }
        }

        public bool IsInterpreterLocked(InterpreterConfiguration config, object moniker) {
            LockInfo info;
            Dictionary<object, LockInfo> locks;

            lock (_locksLock) {
                return _locks != null &&
                    _locks.TryGetValue(config, out locks) &&
                    locks.TryGetValue(moniker, out info) &&
                    info._lockCount > 0;
            }
        }

        public bool UnlockInterpreter(object cookie) {
            var info = cookie as LockInfo;
            if (info == null) {
                throw new ArgumentException("cookie was not returned from a call to LockInterpreterAsync");
            }

            bool res = Interlocked.Decrement(ref info._lockCount) == 0;
            info._lock.Release();
            return res;
        }
    }
}
