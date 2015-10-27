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

using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class RelativeModuleName : ModuleName {
        private readonly int _dotCount;

        public RelativeModuleName(NameExpression[] names, int dotCount)
            : base(names) {
            _dotCount = dotCount;
        }

        public override string MakeString() {
            return new string('.', DotCount) + base.MakeString();
        }

        public int DotCount {
            get {
                return _dotCount;
            }
        }
    }
}
