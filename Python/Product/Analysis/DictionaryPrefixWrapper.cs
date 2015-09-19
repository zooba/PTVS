using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.PythonTools.Analysis {
    class DictionaryPrefixWrapper<TValue> : IReadOnlyDictionary<string, TValue> {
        private readonly IReadOnlyDictionary<string, TValue> _dict;
        private readonly string _prefix;
        private readonly char[] _excludeKeysContaining;
        private int _count = -1;

        public DictionaryPrefixWrapper(
            IReadOnlyDictionary<string, TValue> dict,
            string prefix,
            char[] excludeKeysContaining) {
            _dict = dict;
            _prefix = prefix ?? string.Empty;
            _excludeKeysContaining = excludeKeysContaining ?? new char[0];
        }

        public TValue this[string key] {
            get {
                if (key.IndexOfAny(_excludeKeysContaining) >= 0) {
                    throw new KeyNotFoundException(key);
                }
                try {
                    return _dict[_prefix + key];
                } catch (KeyNotFoundException ex) {
                    throw new KeyNotFoundException(key, ex);
                }
            }
        }

        private bool KeyMatches(string key) {
            return key.Length > _prefix.Length &&
                key.StartsWith(_prefix) &&
                key.IndexOfAny(_excludeKeysContaining, _prefix.Length + 1) < 0;
        }

        private bool KeyMatches(KeyValuePair<string, TValue> keyValue) {
            return keyValue.Key.Length > _prefix.Length &&
                keyValue.Key.StartsWith(_prefix) &&
                keyValue.Key.IndexOfAny(_excludeKeysContaining, _prefix.Length + 1) < 0;
        }

        public int Count {
            get {
                if (_count < 0) {
                    _count = _dict.Keys.Count(KeyMatches);
                }
                return _count;
            }
        }

        public IEnumerable<string> Keys {
            get {
                return _dict.Where(KeyMatches).Select(kv => kv.Key.Substring(_prefix.Length));
            }
        }

        public IEnumerable<TValue> Values {
            get {
                return _dict.Where(KeyMatches).Select(kv => kv.Value);
            }
        }

        public bool ContainsKey(string key) {
            return key.IndexOfAny(_excludeKeysContaining) < 0 && _dict.ContainsKey(_prefix + key);
        }

        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() {
            return _dict.Where(KeyMatches).GetEnumerator();
        }

        public bool TryGetValue(string key, out TValue value) {
            if (key.IndexOfAny(_excludeKeysContaining) < 0) {
                value = default(TValue);
                return false;
            }
            return _dict.TryGetValue(_prefix + key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }
    }
}
