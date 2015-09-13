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

namespace Microsoft.PythonTools.Parsing {
    class CollectingErrorSink : ErrorSink {
        private readonly List<ErrorResult> _errors;

        public CollectingErrorSink(List<ErrorResult> errors) {
            _errors = errors;
        }

        public override void Add(string message, IndexSpan span, int errorCode, Severity severity) {
            _errors.Add(new ErrorResult(message, span, severity));
        }
    }
}
