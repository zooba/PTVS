﻿// Python Tools for Visual Studio
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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Common.Infrastructure {
    public static class EnumerableExtensions {
        public static IEnumerable<T> MaybeEnumerate<T>(this IEnumerable<T> source) {
            return source ?? Enumerable.Empty<T>();
        }

        private static T Identity<T>(T source) {
            return source;
        }

        public static IEnumerable<T> SelectMany<T>(this IEnumerable<IEnumerable<T>> source) {
            return source.SelectMany(Identity);
        }

        public static IEnumerable<T> Ordered<T>(this IEnumerable<T> source) {
            return source.OrderBy(Identity);
        }

        public static IEnumerable<T> Except<T>(this IEnumerable<T> source, T value) {
            return source.Where(v => {
                try {
                    return !v.Equals(value);
                } catch (NullReferenceException) {
                    return false;
                }
            });
        }

        private static TKey GetKey<TKey, TValue>(KeyValuePair<TKey, TValue> source) {
            return source.Key;
        }

        public static IEnumerable<TKey> Keys<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> source) {
            return source.Select(GetKey);
        }
    }
}
