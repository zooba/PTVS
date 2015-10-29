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


using System.Text;

namespace Microsoft.PythonTools.Analysis.Parsing.Ast {
    public class SliceExpression : Expression {
        private Expression _sliceStart;
        private Expression _sliceStop;
        private Expression _sliceStep;
        private bool _stepProvided;

        public Expression SliceStart {
            get { return _sliceStart; }
            set { ThrowIfFrozen(); _sliceStart = value; }
        }

        public Expression SliceStop {
            get { return _sliceStop; }
            set { ThrowIfFrozen(); _sliceStop = value; }
        }

        public Expression SliceStep {
            get { return _sliceStep; }
            set { ThrowIfFrozen(); _sliceStep = value; }
        }

        /// <summary>
        /// True if the user provided a step parameter (either providing an explicit parameter
        /// or providing an empty step parameter) false if only start and stop were provided.
        /// </summary>
        public bool StepProvided {
            get { return _stepProvided; }
            set { ThrowIfFrozen(); _stepProvided = value; }
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                if (_sliceStart != null) {
                    _sliceStart.Walk(walker);
                }
                if (_sliceStop != null) {
                    _sliceStop.Walk(walker);
                }
                if (_sliceStep != null) {
                    _sliceStep.Walk(walker);
                }
            }
            walker.PostWalk(this);
        }
    }
}
