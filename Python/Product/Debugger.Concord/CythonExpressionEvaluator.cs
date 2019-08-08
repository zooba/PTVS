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
// MERCHANTABILITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.CallStack;
using Microsoft.VisualStudio.Debugger.Evaluation;

namespace Microsoft.PythonTools.Debugger.Concord {
    class CythonExpressionEvaluator : ExpressionEvaluator {
        private const uint Timeout = 200;

        private readonly DkmStackWalkFrame _originalFrame;

        public CythonExpressionEvaluator(DkmStackWalkFrame frame) {
            _originalFrame = frame;
        }

        private DkmInspectionContext GetCppContext(DkmInspectionContext inspectionContext, DkmStackWalkFrame frame) {
            return DkmInspectionContext.Create(
                inspectionContext.InspectionSession,
                frame.Process.GetNativeRuntimeInstance(),
                frame.Thread,
                Timeout,
                DkmEvaluationFlags.TreatAsExpression | DkmEvaluationFlags.NoSideEffects,
                DkmFuncEvalFlags.None,
                inspectionContext.Radix,
                CppExpressionEvaluator.CppLanguage,
                null
            );
        }

        public override void EvaluateExpression(DkmInspectionContext inspectionContext, DkmWorkList workList, DkmLanguageExpression expression, DkmStackWalkFrame stackFrame, DkmCompletionRoutine<DkmEvaluateExpressionAsyncResult> completionRoutine) {
            GetCppContext(inspectionContext, stackFrame)
                .EvaluateExpression(workList, expression, stackFrame, completionRoutine);
        }

        public override void GetChildren(DkmEvaluationResult result, DkmWorkList workList, int initialRequestSize, DkmInspectionContext inspectionContext, DkmCompletionRoutine<DkmGetChildrenAsyncResult> completionRoutine) {
            result.GetChildren(workList, initialRequestSize, GetCppContext(inspectionContext, result.StackFrame), completionRoutine);
        }

        public override void GetFrameLocals(DkmInspectionContext inspectionContext, DkmWorkList workList, DkmStackWalkFrame stackFrame, DkmCompletionRoutine<DkmGetFrameLocalsAsyncResult> completionRoutine) {
            GetCppContext(inspectionContext, stackFrame)
                .GetFrameLocals(workList, _originalFrame, new GetFrameLocalsEvent(
                    inspectionContext,
                    stackFrame,
                    workList,
                    completionRoutine
                ).OnComplete);
        }

        private class GetFrameLocalsEvent {
            private readonly DkmInspectionContext _inspectionContext;
            private readonly DkmStackWalkFrame _frame;
            private readonly DkmWorkList _workList;
            private readonly DkmCompletionRoutine<DkmGetFrameLocalsAsyncResult> _completionRoutine;

            public GetFrameLocalsEvent(
                DkmInspectionContext inspectionContext,
                DkmStackWalkFrame frame,
                DkmWorkList workList,
                DkmCompletionRoutine<DkmGetFrameLocalsAsyncResult> completionRoutine
            ) {
                _inspectionContext = inspectionContext;
                _frame = frame;
                _workList = workList;
                _completionRoutine = completionRoutine;
            }

            public void OnComplete(DkmGetFrameLocalsAsyncResult result) {
                try {
                    ErrorHandler.ThrowOnFailure(result.ErrorCode);
                    result.EnumContext.GetItems(_workList, 0, result.EnumContext.Count, OnResults);
                } catch (Exception ex) {
                    _completionRoutine(DkmGetFrameLocalsAsyncResult.CreateErrorResult(ex));
                }
            }

            private void OnResults(DkmEvaluationEnumAsyncResult result) {
                try {
                    ErrorHandler.ThrowOnFailure(result.ErrorCode);

                    var items = new List<DkmEvaluationResult>();
                    foreach (var item in result.Items) {
                        if (item.Name.StartsWith("__pyx_v_")) {
                            var name = item.Name.Substring(8);
                            if (item is DkmFailedEvaluationResult failedItem) {
                                items.Add(DkmFailedEvaluationResult.Create(
                                    _inspectionContext,
                                    _frame,
                                    name,
                                    item.FullName,
                                    failedItem.ErrorMessage,
                                    failedItem.Flags,
                                    null
                                ));
                            } else if (item is DkmSuccessEvaluationResult successItem) {
                                items.Add(DkmSuccessEvaluationResult.Create(
                                    _inspectionContext,
                                    _frame,
                                    name,
                                    item.FullName,
                                    successItem.Flags,
                                    successItem.Value,
                                    successItem.EditableValue,
                                    successItem.Type,
                                    successItem.Category,
                                    successItem.Access,
                                    successItem.StorageType,
                                    successItem.TypeModifierFlags,
                                    successItem.Address,
                                    null,
                                    null,
                                    null
                                )
                                );
                            } else {
                                items.Add(item);
                            }
                        }
                    }

                    var res = new DkmGetFrameLocalsAsyncResult(
                        DkmEvaluationResultEnumContext.Create(items.Count, _frame, _inspectionContext, new EvaluationResults {
                            Results = items.ToArray()
                        })
                    );
                    _completionRoutine(res);
                } catch (Exception ex) {
                    _completionRoutine(DkmGetFrameLocalsAsyncResult.CreateErrorResult(ex));
                }

            }
        }

        public override void GetItems(DkmEvaluationResultEnumContext enumContext, DkmWorkList workList, int startIndex, int count, DkmCompletionRoutine<DkmEvaluationEnumAsyncResult> completionRoutine) {
            var evalResults = enumContext.GetDataItem<EvaluationResults>();
            if (evalResults != null) {
                try {
                    var result = evalResults.Results.Skip(startIndex).Take(count).ToArray();
                    completionRoutine(new DkmEvaluationEnumAsyncResult(result));
                } catch (Exception ex) {
                    completionRoutine(DkmEvaluationEnumAsyncResult.CreateErrorResult(ex));
                }
            } else {
                enumContext.GetItems(workList, startIndex, count, completionRoutine);
            }
        }

        public override string GetUnderlyingString(DkmEvaluationResult result) {
            return result.GetUnderlyingString();
        }

        public override void OnAsyncBreakComplete(DkmThread thread) {
            // pass
        }

        public override void SetValueAsString(DkmEvaluationResult result, string value, int timeout, out string errorText) {
            result.SetValueAsString(value, timeout, out errorText);
        }
    }
}
