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
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public abstract class ComprehensionIterator : Node {
    }

    public class ComprehensionIf : ComprehensionIterator {
        private Expression _test;

        public ComprehensionIf() { }

        public Expression Test {
            get { return _test; }
            set { ThrowIfFrozen(); _test = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _test?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }

    public class ComprehensionFor : ComprehensionIterator {
        private Expression _lhs, _list;

        public ComprehensionFor() { }

        public Expression Left {
            get { return _lhs; }
            set { ThrowIfFrozen(); _lhs = value; }
        }

        public Expression List {
            get { return _list; }
            set { ThrowIfFrozen(); _list = value; }
        }
        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _lhs?.Walk(walker);
                _list?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }

    public abstract class Comprehension : Expression {
        private IList<ComprehensionIterator> _iterators;

        public IList<ComprehensionIterator> Iterators {
            get { return _iterators; }
            set { ThrowIfFrozen(); _iterators = value; }
        }

        protected override void OnFreeze() {
            base.OnFreeze();
            _iterators = FreezeList(_iterators);
        }

        public abstract override void Walk(PythonWalker walker);
    }

    public sealed class ListComprehension : Comprehension {
        private Expression _item;

        public Expression Item {
            get { return _item; }
            set { ThrowIfFrozen(); _item = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_item != null) {
                    _item.Walk(walker);
                }
                if (Iterators != null) {
                    foreach (ComprehensionIterator ci in Iterators) {
                        ci.Walk(walker);
                    }
                }
            }
            walker.PostWalk(this);
        }

        internal override string CheckName => "list comprehension";
    }

    public sealed class SetComprehension : Comprehension {
        private Expression _item;

        public Expression Item {
            get { return _item; }
            set { ThrowIfFrozen(); _item = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_item != null) {
                    _item.Walk(walker);
                }
                if (Iterators != null) {
                    foreach (ComprehensionIterator ci in Iterators) {
                        ci.Walk(walker);
                    }
                }
            }
            walker.PostWalk(this);
        }

        internal override string CheckName => "set comprehension";
    }

    public sealed class DictionaryComprehension : Comprehension {
        private Expression _key, _value;

        public Expression Key {
            get { return _key; }
            set { ThrowIfFrozen(); _key = value; }
        }

        public Expression Value {
            get { return _value; }
            set { ThrowIfFrozen(); _value = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                _key?.Walk(walker);
                _value?.Walk(walker);

                if (Iterators != null) {
                    foreach (ComprehensionIterator ci in Iterators) {
                        ci.Walk(walker);
                    }
                }
            }
            walker.PostWalk(this);
        }

        internal override string CheckName => "set comprehension";
    }
}
