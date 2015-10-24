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


namespace Microsoft.PythonTools.Parsing.Ast {
    internal static class NodeAttributes {
#if DEBUG
        class AttributeKey {
            public readonly string Key;

            public AttributeKey(string key) {
                Key = key;
            }

            public override string ToString() {
                return Key;
            }
        }

        static object MakeKey(string key) {
            return new AttributeKey(key);
        }
#else
        static object MakeKey(string key) {
            return new object();
        }
#endif

            /// <summary>
            /// Value is a string which precedes a token in the node.
            /// </summary>
            public static readonly object PrecedingWhiteSpace = MakeKey("PrecedingWhiteSpace");
        /// <summary>
        /// Value is a string which follows a token in the node.
        /// </summary>
        public static readonly object TrailingWhiteSpace = MakeKey("TrailingWhiteSpace");
        /// <summary>
        /// Value is a string which precedes a second token in the node.
        /// </summary>
        public static readonly object SecondPrecedingWhiteSpace = MakeKey("SecondPrecedingWhiteSpace");

        /// <summary>
        /// Value is a string which precedes a third token in the node.
        /// </summary>
        public static readonly object ThirdPreceedingWhiteSpace = MakeKey("ThirdPreceedingWhiteSpace");

        /// <summary>
        /// Value is a string which precedes a fourth token in the node.
        /// </summary>
        public static readonly object FourthPrecedingWhiteSpace = MakeKey("FourthPrecedingWhiteSpace");

        /// <summary>
        /// Value is a string which precedes a fifth token in the node.
        /// </summary>
        public static readonly object FifthPrecedingWhiteSpace = MakeKey("FifthPrecedingWhiteSpace");

        /// <summary>
        /// Value is an array of strings which precedes items in the node.
        /// </summary>
        public static readonly object ListWhiteSpace = MakeKey("ListWhiteSpace");

        /// <summary>
        /// Value is an array of strings which precedes items names in the node.
        /// </summary>
        public static readonly object NamesWhiteSpace = MakeKey("NamesWhiteSpace");

        /// <summary>
        /// Value is a string which is the name as it appeared verbatim in the
        /// source code (for mangled name).
        /// </summary>
        public static readonly object VerbatimImage = MakeKey("VerbatimImage");

        /// <summary>
        /// Value is a string which represents extra node specific verbatim text.
        /// </summary>
        public static readonly object ExtraVerbatimText = MakeKey("ExtraVerbatimText");

        /// <summary>
        /// The tuple expression was constructed without parenthesis.  The value
        /// doesn't matter, only the presence of the metadata indicates the
        /// value is set.
        /// </summary>
        public static readonly object IsAltFormValue = MakeKey("IsAltFormValue");

        /// <summary>
        /// Provides an array of strings which are used for verbatim names when
        /// multiple names are involved (e.g. del, global, import, etc...)
        /// </summary>
        public static readonly object VerbatimNames = MakeKey("VerbatimNames");

        /// <summary>
        /// The node is missing a closing grouping (close paren, close brace,
        /// close bracket).
        /// </summary>
        public static readonly object ErrorMissingCloseGrouping = MakeKey("ErrorMissingCloseGrouping");

        /// <summary>
        /// The node is incomplete.  Where the node ends is on a node-by-node
        /// basis but it's well defined for each individual node.
        /// </summary>
        public static readonly object ErrorIncompleteNode = MakeKey("ErrorIncompleteNode");

        public static readonly object VariableReference = MakeKey("VariableReference");

        public static readonly object Variable = MakeKey("Variable");
        /// <summary>
        /// Value is a string which follows a token in the node.
        /// </summary>
        public static readonly object Comment = MakeKey("Comment");

        public static void AddVariableReference(this Node node, PythonAst ast, bool bindNames, object reference) {
            if (bindNames) {
                ast.SetAttribute(node, VariableReference, reference);
            }
        }

        public static void AddVariable(this Parameter node, PythonAst ast, bool bindNames, PythonVariable variable) {
            if (bindNames) {
                ast.SetAttribute(node, Variable, variable);
            }
        }

        public static string GetPrecedingWhiteSpace(this Node node, PythonAst ast) {
            return GetWhiteSpace(node, ast, NodeAttributes.PrecedingWhiteSpace);
        }

        public static string GetPrecedingWhiteSpaceDefaultEmpty(this Node node, PythonAst ast) {
            return GetWhiteSpace(node, ast, NodeAttributes.PrecedingWhiteSpace, string.Empty);
        }

        public static string GetComment(this Node node, PythonAst ast) {
            return GetWhiteSpace(node, ast, Comment, null);
        }

        internal static string GetWhiteSpace(Node node, PythonAst ast, object kind, string defaultValue = " ") {
            object whitespace;
            if (ast.TryGetAttribute(node, kind, out whitespace)) {
                return (string)whitespace;
            } else {
                return defaultValue;
            }
        }

        public static string GetSecondWhiteSpace(this Node node, PythonAst ast) {
            return GetWhiteSpace(node, ast, NodeAttributes.SecondPrecedingWhiteSpace);
        }

        public static string GetSecondWhiteSpaceDefaultNull(this Node node, PythonAst ast) {
            return GetWhiteSpace(node, ast, NodeAttributes.SecondPrecedingWhiteSpace, null);
        }

        public static string GetThirdWhiteSpace(this Node node, PythonAst ast) {
            return GetWhiteSpace(node, ast, NodeAttributes.ThirdPreceedingWhiteSpace);
        }

        public static string GetThirdWhiteSpaceDefaultNull(this Node node, PythonAst ast) {
            return GetWhiteSpace(node, ast, NodeAttributes.ThirdPreceedingWhiteSpace, null);
        }

        public static string GetFourthWhiteSpace(this Node node, PythonAst ast) {
            return GetWhiteSpace(node, ast, NodeAttributes.FourthPrecedingWhiteSpace);
        }

        public static string GetFourthWhiteSpaceDefaultNull(this Node node, PythonAst ast) {
            return GetWhiteSpace(node, ast, NodeAttributes.FourthPrecedingWhiteSpace, null);
        }

        public static string GetFifthWhiteSpace(this Node node, PythonAst ast) {
            return GetWhiteSpace(node, ast, NodeAttributes.FifthPrecedingWhiteSpace);
        }

        public static string GetExtraVerbatimText(this Node node, PythonAst ast) {
            return GetWhiteSpace(node, ast, NodeAttributes.ExtraVerbatimText, null);
        }

        public static bool IsAltForm(this Node node, PythonAst ast) {
            object dummy;
            if (ast.TryGetAttribute(node, NodeAttributes.IsAltFormValue, out dummy)) {
                return true;
            } else {
                return false;
            }
        }

        public static bool IsMissingCloseGrouping(this Node node, PythonAst ast) {
            object dummy;
            if (ast.TryGetAttribute(node, NodeAttributes.ErrorMissingCloseGrouping, out dummy)) {
                return true;
            } else {
                return false;
            }
        }

        public static bool IsIncompleteNode(this Node node, PythonAst ast) {
            object dummy;
            if (ast.TryGetAttribute(node, NodeAttributes.ErrorIncompleteNode, out dummy)) {
                return true;
            } else {
                return false;
            }
        }

        public static string[] GetListWhiteSpace(this Node node, PythonAst ast) {
            object whitespace;
            if (ast.TryGetAttribute(node, NodeAttributes.ListWhiteSpace, out whitespace)) {
                return (string[])whitespace;
            } else {
                return null;
            }
        }

        public static string[] GetNamesWhiteSpace(this Node node, PythonAst ast) {
            object whitespace;
            if (ast.TryGetAttribute(node, NodeAttributes.NamesWhiteSpace, out whitespace)) {
                return (string[])whitespace;
            } else {
                return null;
            }
        }

        public static string[] GetVerbatimNames(this Node node, PythonAst ast) {
            object names;
            if (ast.TryGetAttribute(node, NodeAttributes.VerbatimNames, out names)) {
                return (string[])names;
            } else {
                return null;
            }
        }

        public static string GetVerbatimImage(this Node node, PythonAst ast) {
            object image;
            if (ast.TryGetAttribute(node, NodeAttributes.VerbatimImage, out image)) {
                return (string)image;
            } else {
                return null;
            }
        }
    }
}
