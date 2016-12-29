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
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Parsing.Ast;

namespace Microsoft.PythonTools.Interpreter.Ast {
    class AstPythonType : IPythonType, IMemberContainer, ILocatedMember {
        private readonly Dictionary<string, IMember> _members;

        private static readonly IPythonModule NoDeclModule = new AstPythonModule();

        public AstPythonType(string name) {
            _members = new Dictionary<string, IMember>();
            Name = name;
            DeclaringModule = NoDeclModule;
            Mro = Array.Empty<IPythonType>();
            Locations = Array.Empty<LocationInfo>();
        }

        public AstPythonType(
            PythonAst ast,
            IPythonModule declModule,
            ClassDefinition def,
            string doc,
            LocationInfo loc,
            IEnumerable<IPythonType> mro
        ) {
            _members = new Dictionary<string, IMember>();

            Name = def.Name;
            Documentation = doc;
            DeclaringModule = declModule;
            Mro = mro.MaybeEnumerate().ToArray();
            Locations = new[] { loc };
        }

        internal void AddMembers(IEnumerable<KeyValuePair<string, IMember>> members, bool overwrite) {
            foreach (var kv in members) {
                if (!overwrite) {
                    IMember existing;
                    if (_members.TryGetValue(kv.Key, out existing)) {
                        continue;
                    }
                }
                _members[kv.Key] = kv.Value;
            }
        }

        public string Name { get; }
        public string Documentation { get; }
        public IPythonModule DeclaringModule {get;}
        public IList<IPythonType> Mro { get; }
        public bool IsBuiltin => true;
        public PythonMemberType MemberType => PythonMemberType.Class;
        public BuiltinTypeId TypeId => BuiltinTypeId.Type;

        public IEnumerable<LocationInfo> Locations { get; }

        public IMember GetMember(IModuleContext context, string name) {
            IMember member;
            _members.TryGetValue(name, out member);
            return member;
        }

        public IPythonFunction GetConstructors() => GetMember(null, "__init__") as IPythonFunction;

        public IEnumerable<string> GetMemberNames(IModuleContext moduleContext) {
            return _members.Keys.ToArray();
        }
    }
}
