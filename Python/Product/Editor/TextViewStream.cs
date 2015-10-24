using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.PythonTools.Editor {
    class TextViewStream : Stream {
        private readonly IEnumerator<ITextSnapshot> _snapshots;
        private IEnumerator<ITextSnapshotLine> _lines;
        private readonly Encoding _encoding;
        private readonly string _bufferSeparator;
        private MemoryStream _buffer;

        private long _position;
        private readonly long _length;

        public TextViewStream(
            ITextView graph,
            Encoding encoding,
            string contentType,
            string bufferSeparator
        ) {
            var snapshotList = graph.BufferGraph
                .GetTextBuffers(b => b.ContentType.IsOfType(contentType))
                .Select(b => b.CurrentSnapshot)
                .ToList();

            _length = snapshotList.Sum(s => s.Length + (_bufferSeparator?.Length ?? 0));

            _snapshots = snapshotList.GetEnumerator();
            _lines = (_snapshots.MoveNext() ? _snapshots.Current.Lines : Enumerable.Empty<ITextSnapshotLine>())
                .GetEnumerator();
            _encoding = encoding;
            _bufferSeparator = bufferSeparator;
        }

        public override void Close() {
            base.Close();
            if (_lines != null) {
                _lines.Dispose();
            }
            _snapshots.Dispose();
        }

        private void ReadIntoBuffer() {
            if (_buffer != null && _buffer.Length - _buffer.Position >= 4096) {
                return;
            }

            long tell;
            if (_buffer == null) {
                _buffer = new MemoryStream();
                tell = 0;
            } else {
                tell = _buffer.Position;
            }
            using (var writer = new StreamWriter(_buffer, _encoding, 4096, true)) {
                while (_buffer.Length < 16384) {
                    if (!_lines.MoveNext()) {
                        if (!_snapshots.MoveNext()) {
                            break;
                        }
                        if (!string.IsNullOrEmpty(_bufferSeparator)) {
                            writer.Write(_bufferSeparator);
                        }
                        _lines.Dispose();
                        _lines = _snapshots.Current.Lines.GetEnumerator();
                    }
                    writer.Write(_lines.Current.GetTextIncludingLineBreak());
                }
            }
            _buffer.Seek(tell, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count) {
            ReadIntoBuffer();
            int readBytes = _buffer.Read(buffer, offset, count);
            _position += readBytes;
            return readBytes;
        }

        public override long Length => _length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;


        public override long Position {
            get {
                return _position;
            }
            set {
                throw new NotSupportedException();
            }
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) {
            throw new NotSupportedException();
        }

        public override void SetLength(long value) {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) {
            throw new NotSupportedException();
        }
    }
}
