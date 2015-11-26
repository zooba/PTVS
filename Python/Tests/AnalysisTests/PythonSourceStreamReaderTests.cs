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

        private static string Shorten(string str) {
            if ((str?.Length ?? 0) < 30) {
                return str;
            }
            return str.Substring(0, 20) + "..." + str.Substring(str.Length - 10);
        }

        [TestMethod, Priority(0)]
        public void VeryLongLines() {
            var lls = new LongLineStream(100, 25229, "\r\n");
            using (var reader = new PythonSourceStreamReader(lls, true)) {
                int lineCount = 0;
                string line;
                while ((line = reader.ReadLine()) != null) {
                    var actual = lineCount.ToString() + ": " + Shorten(line).Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0");
                    Assert.AreEqual(25229, line.Length, actual);
                    Assert.AreEqual("Lorem ipsum", line.Substring(0, 11), actual);
                    Assert.AreEqual("\r\n", line.Substring(line.Length - 2), actual);
                    lineCount += 1;
                }
            }
        }


        class LongLineStream : Stream {
            private long _length, _charsPerLine, _read;
            private const string Characters = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.";
            private readonly IEnumerable<byte> _bytes;
            private IEnumerator<byte> _characters;

            public LongLineStream(int lineCount, int charsPerLine, string newLine) {
                _length = lineCount * charsPerLine;
                _charsPerLine = charsPerLine;
                _read = 0;
                var encoded = Encoding.ASCII.GetBytes(Characters);
                var nlBytes = Encoding.ASCII.GetBytes(newLine);
                _bytes = Enumerable.Repeat(encoded, charsPerLine / encoded.Length + 1)
                    .SelectMany(b => b)
                    .Take(charsPerLine - nlBytes.Length)
                    .Concat(nlBytes);
                _characters = _bytes.GetEnumerator();
            }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position {
                get { return _read; }
                set { _read = value; }
            }
            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count) {
                int read = 0;
                for (int i = offset; i < buffer.Length && read < count && _read < _length; ++i) {
                    if (!_characters.MoveNext()) {
                        _characters = _bytes.GetEnumerator();
                        _characters.MoveNext();
                    }
                    buffer[i] = _characters.Current;
                    _read += 1;
                    read += 1;
                }
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin) {
                switch (origin) {
                    case SeekOrigin.Begin:
                        _read = offset;
                        break;
                    case SeekOrigin.Current:
                        _read += offset;
                        break;
                    case SeekOrigin.End:
                        _read = Length + offset;
                        break;
                    default:
                        break;
                }
                return _read;
            }

            public override void SetLength(long value) { _length = value; }

            public override void Write(byte[] buffer, int offset, int count) {
                throw new NotSupportedException();
            }
        }
    }
}
