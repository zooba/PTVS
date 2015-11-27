// Visual Studio Shared Project
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
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Projection;

namespace TestUtilities.Mocks {
    public class MockMappingPoint : IMappingPoint {
        private readonly ITextView _view;
        private readonly ITrackingPoint _trackingPoint;

        public MockMappingPoint(ITextView view, ITrackingPoint trackingPoint) {
            _view = view;
            _trackingPoint = trackingPoint;
        }

        public ITextBuffer AnchorBuffer => _trackingPoint.TextBuffer;

        public IBufferGraph BufferGraph => _view.BufferGraph;

        public SnapshotPoint? GetInsertionPoint(Predicate<ITextBuffer> match) {
            return BufferGraph.MapDownToInsertionPoint(
                _trackingPoint.GetPoint(AnchorBuffer.CurrentSnapshot),
                PointTrackingMode.Positive,
                s => match(s.TextBuffer)
            );
        }

        public SnapshotPoint? GetPoint(Predicate<ITextBuffer> match, PositionAffinity affinity) {
            return BufferGraph.MapDownToFirstMatch(
                _trackingPoint.GetPoint(AnchorBuffer.CurrentSnapshot),
                PointTrackingMode.Positive,
                s => match(s.TextBuffer),
                affinity
            );
        }

        public SnapshotPoint? GetPoint(ITextSnapshot targetSnapshot, PositionAffinity affinity) {
            try {
                return _trackingPoint.GetPoint(targetSnapshot);
            } catch (ArgumentException) {
                return null;
            }
        }

        public SnapshotPoint? GetPoint(ITextBuffer targetBuffer, PositionAffinity affinity) {
            return GetPoint(targetBuffer.CurrentSnapshot, affinity);
        }
    }
}
