using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.PythonTools.Analysis.Analyzer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Analysis2Tests {
    [TestClass]
    public class PythonSourceStreamReaderTests {
        private PythonSourceStreamReader Read(string text, bool throwOnInvalidChar = true) {
            return Read(Encoding.ASCII.GetBytes(text), throwOnInvalidChar);
        }

        private PythonSourceStreamReader Read(byte[] buffer, bool throwOnInvalidChar = true) {
            return new PythonSourceStreamReader(new MemoryStream(buffer), throwOnInvalidChar);
        }

        private byte[] ToUTF8Sig(string text) {
            var enc = new UTF8Encoding(true);
            return enc.GetPreamble().Concat(enc.GetBytes(text)).ToArray();
        }

        private string EscapeNewline(string text) {
            return text.Replace("\r", "\\r").Replace("\n", "\\n");
        }

        [TestMethod, Priority(0)]
        public void ReadLines() {
            using (var reader = Read("line 1\r\nline 2\r\nline 3")) {
                Assert.AreEqual("line 1\r\n", reader.ReadLine());
                Assert.AreEqual("line 2\r\n", reader.ReadLine());
                Assert.AreEqual("line 3", reader.ReadLine());
                Assert.IsNull(reader.ReadLine());
            }
        }

        [TestMethod, Priority(0)]
        public void BufferBoundaries() {
            var sb = new StringBuilder();
            while (sb.Length < PythonSourceStreamReader.BufferFillChunkSize) {
                sb.Append("012345678 ");
            }

            var line1 = sb.ToString(0, PythonSourceStreamReader.BufferFillChunkSize - 1) + "\r\n";
            using (var reader = Read(line1 + "line 2\r\n" + line1 + "line 3")) {
                Assert.AreEqual(EscapeNewline(line1), EscapeNewline(reader.ReadLine()));
                Assert.AreEqual("line 2\\r\\n", EscapeNewline(reader.ReadLine()));
                Assert.AreEqual(EscapeNewline(line1), EscapeNewline(reader.ReadLine()));
                Assert.AreEqual("line 3", reader.ReadLine());
                Assert.IsNull(reader.ReadLine());
            }

            line1 = sb.ToString(0, PythonSourceStreamReader.ReadLineBufferSize - 1) + "\r\n";
            using (var reader = Read(line1 + line1 + line1)) {
                Assert.AreEqual(EscapeNewline(line1), EscapeNewline(reader.ReadLine()));
                Assert.AreEqual(EscapeNewline(line1), EscapeNewline(reader.ReadLine()));
                Assert.AreEqual(EscapeNewline(line1), EscapeNewline(reader.ReadLine()));
                Assert.IsNull(reader.ReadLine());
            }

            using (var reader = Read(line1 + line1 + line1)) {
                Assert.AreEqual(line1 + line1 + line1, reader.ReadToEnd());
            }
        }

        [TestMethod, Priority(0)]
        public void DetectEncoding() {
            using (var reader = Read("no encoding")) {
                Assert.AreEqual("utf-8", reader.Encoding.WebName);
                Assert.IsTrue(string.IsNullOrEmpty(reader.CodingComment), reader.CodingComment ?? "(null)");
            }

            using (var reader = Read(ToUTF8Sig("with BOM"))) {
                Assert.AreEqual("utf-8", reader.Encoding.WebName);
                Assert.AreEqual("\uFEFF", reader.CodingComment);
            }

            using (var reader = Read(ToUTF8Sig("# coding=utf-8-sig\rnext line"))) {
                Assert.AreEqual("utf-8", reader.Encoding.WebName);
                Assert.AreEqual("\uFEFF", reader.CodingComment);
                Assert.AreEqual("# coding=utf-8-sig\\r", EscapeNewline(reader.ReadLine()));
                Assert.AreEqual("next line", reader.ReadLine());
            }

            using (var reader = Read("# coding=utf-8\r\nnext line")) {
                Assert.AreEqual("utf-8", reader.Encoding.WebName);
                Assert.AreEqual("# coding=utf-8\\r\\n", EscapeNewline(reader.CodingComment));
                Assert.AreEqual("# coding=utf-8\\r\\n", EscapeNewline(reader.ReadLine()));
                Assert.AreEqual("next line", reader.ReadLine());
            }
        }
    }
}
