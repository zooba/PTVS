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
using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class DottedName : Node {
        private readonly NameExpression[] _names;

        public DottedName(NameExpression[] names) {
            _names = names;
        }

        public IList<NameExpression> Names {
            get { return _names; }
        }

        public virtual string MakeString() {
            if (_names.Length == 0) return String.Empty;

            StringBuilder ret = new StringBuilder(_names[0].Name);
            for (int i = 1; i < _names.Length; i++) {
                ret.Append('.');
                ret.Append(_names[i].Name);
            }
            return ret.ToString();
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                ;
            }
            walker.PostWalk(this);
        }

    }
}
