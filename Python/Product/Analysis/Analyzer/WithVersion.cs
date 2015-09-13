using System;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    public sealed class WithVersion<T> where T : class {
        private T _value;
        private int _version;
        private TaskCompletionSource<int> _newVersion;

        public WithVersion() {
            _value = default(T);
            _version = 0;
            _newVersion = null;
        }

        public WithVersion(T value) {
            _value = value;
            _version = 1;
            _newVersion = null;
        }

        public void SetValue(T value) {
            TaskCompletionSource<int> tcs;
            int version;
            lock (this) {
                _value = value;
                version = _version + 1;
                _version = version;
                tcs = _newVersion;
                _newVersion = null;
            }
            tcs?.SetResult(version);
            NewVersion?.Invoke(this, EventArgs.Empty);
        }

        public T Get() {
            if (_version > 0) {
                return _value;
            }
            throw new InvalidOperationException("No value available");
        }

        public Task<T> GetAsync() {
            if (_version > 0) {
                return Task.FromResult(_value);
            }
            return GetNewVersionAsync();
        }

        public async Task<T> GetNewVersionAsync() {
            TaskCompletionSource<int> tcs;
            lock (this) {
                tcs = _newVersion;
                if (tcs == null) {
                    _newVersion = tcs = new TaskCompletionSource<int>();
                }
            }
            await tcs.Task;
            return _value;
        }

        public event EventHandler NewVersion;
    }
}
