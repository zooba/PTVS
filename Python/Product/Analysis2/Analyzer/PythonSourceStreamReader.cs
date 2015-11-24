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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Analysis.Analyzer {
    sealed class PythonSourceStreamReader : TextReader {
        private readonly Stream _stream;
        private readonly bool _throwOnInvalidChar;
        private MemoryStream _buffer;
        private Encoding _encoding;
        private string _codingComment;
        private string _line;
        private int _lineIndex;
        private bool _eof;

        internal const int BufferFillChunkSize = 4096;
        internal const int ReadLineBufferSize = 128;

        private static readonly Regex _codingRegex = new Regex("coding[:=]\\s*([-\\w.]+)", RegexOptions.Compiled);

        public PythonSourceStreamReader(Stream stream, bool throwOnInvalidChar) {
            _stream = stream;
            _throwOnInvalidChar = throwOnInvalidChar;
        }

        public string CodingComment {
            get {
                if (_encoding == null) {
                    ReadEncodingAsync().GetAwaiter().GetResult();
                }
                return _codingComment;
            }
        }

        public Encoding Encoding {
            get {
                if (_encoding == null) {
                    ReadEncodingAsync().GetAwaiter().GetResult();
                }
                return _encoding;
            }
        }

        public async Task<Encoding> GetEncodingAsync() {
            await ReadEncodingAsync();
            return _encoding;
        }

        private async Task FillBufferAsync(int bytesRequested = 0) {
            if (bytesRequested <= 0) {
                bytesRequested = BufferFillChunkSize;
            }
            if (_buffer != null && _buffer.Length - _buffer.Position > bytesRequested) {
                return;
            }

            var bytes = new byte[BufferFillChunkSize];
            int read = 0;
            var newBuffer = new MemoryStream();
            var oldBuffer = _buffer;
            if (oldBuffer != null) {
                while ((read = oldBuffer.Read(bytes, 0, bytes.Length)) > 0) {
                    newBuffer.Write(bytes, 0, read);
                }
                oldBuffer.Close();
            }

            while ((newBuffer.Length < BufferFillChunkSize * 4 || newBuffer.Length < bytesRequested)&&
                (read = await _stream.ReadAsync(bytes, 0, bytes.Length)) > 0) {
                newBuffer.Write(bytes, 0, read);
            }
            if (newBuffer.Length > 0) {
                newBuffer.Seek(0, SeekOrigin.Begin);
                _buffer = newBuffer;
            } else {
                _buffer = null;
                _eof = true;
            }
        }

        private int FindNextLine(byte[] buffer, int start, int end) {
            int slashR = Array.IndexOf(buffer, (byte)0x0D, start), slashN = Array.IndexOf(buffer, (byte)0x0A, start);
            int eol = slashR;
            if (slashR >= 0) {
                if (slashN >= 0 && (slashN < slashR || slashN == slashR + 1)) {
                    return slashN + 1;
                }
                return slashR + 1;
            } else if (slashN >= 0) {
                return slashN + 1;
            }
            return -1;
        }

        private async Task ReadEncodingAsync() {
            if (_encoding != null) {
                return;
            }

            await FillBufferAsync();

            var oldPos = _buffer.Position;
            try {
                var firstLine = new byte[ReadLineBufferSize];
                int read = _buffer.Read(firstLine, 0, firstLine.Length);

                if (read < 3) {
                    // No BOM or coding comment, so assume UTF-8
                    return;
                }

                if (firstLine[0] == 0xEF && firstLine[1] == 0xBB && firstLine[2] == 0xBF) {
                    _encoding = new UTF8Encoding(true, _throwOnInvalidChar);
                    _codingComment = "\uFEFF";
                    return;
                }

                int eol = FindNextLine(firstLine, 0, read);
                if (eol < 0) {
                    eol = read;
                }
                string text;
                try {
                    text = Encoding.ASCII.GetString(firstLine, 0, eol);
                } catch (EncoderFallbackException) {
                    return;
                }

                var match = _codingRegex.Match(text);
                if (match.Success) {
                    _encoding = CodecsInfo.GetEncoding(match.Groups[1].Value, _throwOnInvalidChar);
                    _codingComment = text;
                }
            } finally {
                _buffer.Seek(oldPos, SeekOrigin.Begin);

                if (_encoding == null) {
                    // Encoding was not set, so set the default
                    _encoding = new UTF8Encoding(false, _throwOnInvalidChar);
                }

                var preamble = _encoding.GetPreamble();
                if (preamble != null && preamble.Length > 0) {
                    var buffer = new byte[preamble.Length];
                    int read = _buffer.Read(buffer, 0, buffer.Length);
                    if (read != buffer.Length || !preamble.SequenceEqual(buffer)) {
                        _buffer.Seek(oldPos, SeekOrigin.Begin);
                    }
                }
            }
        }

        private async Task GetLineAsync() {
            if (_line != null && _lineIndex < _line.Length) {
                return;
            }

            await FillBufferAsync();
            if (_eof) {
                return;
            }
            await ReadEncodingAsync();

            var buffer = new byte[ReadLineBufferSize];
            var bufferPos = _buffer.Position;
            int read = _buffer.Read(buffer, 0, buffer.Length);
            int totalRead = read;
            int eol = FindNextLine(buffer, 0, read);
            while (eol < 0 || eol == buffer.Length) {
                var oldBuffer = buffer;
                buffer = new byte[oldBuffer.Length * 2];
                Array.Copy(oldBuffer, buffer, totalRead);
                read = _buffer.Read(buffer, totalRead, buffer.Length - totalRead);
                totalRead += read;
                if (read == 0) {
                    await FillBufferAsync(buffer.Length - totalRead);
                    if (_eof) {
                        break;
                    }
                    read = _buffer.Read(buffer, totalRead, buffer.Length - totalRead);
                    totalRead += read;
                    if (read == 0) {
                        break;
                    }
                }
                eol = FindNextLine(buffer, 0, totalRead);
            }

            if (eol >= 0) {
                _buffer.Seek(bufferPos + eol, SeekOrigin.Begin);
                _line = _encoding.GetString(buffer, 0, eol);
                _lineIndex = 0;
            } else {
                Debug.Assert(_eof, "Should be at end of file");
                _line = _encoding.GetString(buffer, 0, totalRead);
            }
        }

        public override int Read() {
            if (_eof) {
                return -1;
            }
            GetLineAsync().GetAwaiter().GetResult();
            return _line[_lineIndex++];
        }

        public override int Peek() {
            return -1;
        }

        public override string ReadToEnd() {
            return ReadToEndAsync().GetAwaiter().GetResult();
        }

        public override string ReadLine() {
            return ReadLineAsync().GetAwaiter().GetResult();
        }

        public override async Task<string> ReadToEndAsync() {
            var sb = new StringBuilder();
            while (true) {
                await GetLineAsync();
                if (_line == null) {
                    break;
                }
                sb.Append(_line);
                _line = null;
            }
            return sb.ToString();
        }

        public override async Task<string> ReadLineAsync() {
            await GetLineAsync();
            try {
                return _lineIndex > 0 ? _line.Substring(_lineIndex) : _line;
            } finally {
                _line = null;
            }
        }

        static class CodecsInfo {
            static readonly Dictionary<string, EncodingInfoWrapper> _codecs = MakeCodecsDict();

            public static Encoding GetEncoding(string name, bool throwOnInvalidChar) {
                EncodingInfoWrapper info;
                if (!_codecs.TryGetValue(NormalizeEncodingName(name), out info)) {
                    return null;
                }

                if (!throwOnInvalidChar) {
                    var enc = (Encoding)info.GetEncoding().Clone();
                    enc.DecoderFallback = new DecoderFallbackReplace();
                    return enc;
                }

                return info.GetEncoding();
            }

            internal static string NormalizeEncodingName(string name) {
                if (name == null) {
                    return null;
                }
                return name.ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            }

            class DecoderFallbackReplace : DecoderFallback {
                public override int MaxCharCount => 1;
                public override DecoderFallbackBuffer CreateFallbackBuffer() => new DecoderFallbackReplaceBuffer();

                class DecoderFallbackReplaceBuffer : DecoderFallbackBuffer {
                    int _maxRemaining, _remaining;

                    public override int Remaining => _remaining;

                    public override bool Fallback(byte[] bytesUnknown, int index) {
                        _remaining = _maxRemaining = bytesUnknown.Length;
                        return true;
                    }

                    public override char GetNextChar() {
                        if (_remaining > 0) {
                            _remaining -= 1;
                            return '?';
                        }
                        return '\0';
                    }

                    public override bool MovePrevious() {
                        if (_remaining < _maxRemaining) {
                            _remaining += 1;
                            return true;
                        }
                        return false;
                    }
                }
            }

            class EncodingInfoWrapper {
                private readonly EncodingInfo _info;
                private readonly Encoding _encoding;

                private EncodingInfoWrapper(EncodingInfo info, Encoding encoding) {
                    _info = info;
                    _encoding = encoding;
                }

                public Encoding GetEncoding() {
                    return _encoding ?? _info.GetEncoding();
                }

                public static implicit operator EncodingInfoWrapper(EncodingInfo info) {
                    return new EncodingInfoWrapper(info, null);
                }

                public static implicit operator EncodingInfoWrapper(Encoding encoding) {
                    return new EncodingInfoWrapper(null, encoding);
                }
            }

            

            private static Dictionary<string, EncodingInfoWrapper> MakeCodecsDict() {
                var d = new Dictionary<string, EncodingInfoWrapper>();

                d["utf_8_sig"] = new UTF8Encoding(true, true);
                d["utf_8"] = d["utf8"] = d["u8"] = new UTF8Encoding(false, true);
                d["utf_16_le"] = d["utf_16le"] = new UnicodeEncoding(false, false, true);
                d["utf16"] = new UnicodeEncoding(false, true, true);
                d["utf_16_be"] = d["utf_16be"] = new UnicodeEncoding(true, false, true);

                EncodingInfo[] encs = Encoding.GetEncodings();
                for (int i = 0; i < encs.Length; i++) {
                    string normalizedName = NormalizeEncodingName(encs[i].Name);

                    // setup well-known mappings, for everything
                    // else we'll store as lower case w/ _
                    switch (normalizedName) {
                        case "us_ascii":
                            d["cp" + encs[i].CodePage.ToString()] =
                                d[normalizedName] =
                                d["us"] =
                                d["ascii"] =
                                d["646"] =
                                d["us_ascii"] =
                                d["ansi_x3.4_1968"] = 
                                d["ansi_x3_4_1968"] = 
                                d["ansi_x3.4_1986"] = 
                                d["cp367"] = 
                                d["csascii"] = 
                                d["ibm367"] =
                                d["iso646_us"] = 
                                d["iso_646.irv_1991"] = 
                                d["iso_ir_6"] = encs[i];
                            continue;
                        case "iso_8859_1":
                            d["iso_ir_100"] = 
                                d["iso_8859_1_1987"] = 
                                d["iso_8859_1"] = 
                                d["iso8859"] = 
                                d["ibm819"] = 
                                d["csisolatin1"] = 
                                d["8859"] = 
                                d["latin_1"] =
                                d["latin1"] = 
                                d["iso 8859_1"] = 
                                d["iso8859_1"] = 
                                d["cp819"] = 
                                d["819"] = 
                                d["latin"] = 
                                d["latin1"] = 
                                d["l1"] = encs[i];
                            break;
                        case "utf_7":
                            d["u7"] = d["unicode-1-1-utf-7"] = encs[i];
                            break;
                        case "utf_8":
                            continue;
                        case "utf_16":
                            break;
                        case "unicodefffe": // big endian unicode
                            break;
                        case "gb2312":
                            d["x_mac_simp_chinese"] = 
                                d["936"] = 
                                d["ms936"] = 
                                d["chinese"] = 
                                d["csiso58gb231280"] = 
                                d["euc_cn"] = 
                                d["euccn"] = 
                                d["eucgb2312_cn"] = 
                                d["gb2312_1980"] =
                                d["gb2312_80"] = 
                                d["iso_ir_58"] = 
                                d["gbk"] = encs[i];
                            break;
                        case "big5":
                            d["x_mac_trad_chinese"] = 
                                d["big5_tw"] = 
                                d["csbig5"] = encs[i];
                            break;
                        case "cp950":
                            d["ms950"] = 
                                d["hkscs"] = 
                                d["big5_hkscs"] = encs[i];
                            break;
                        case "ibm037":
                            d["cp037"] = 
                                d["csibm037"] = 
                                d["ebcdic_cp_ca"] = 
                                d["ebcdic_cp_nl"] = 
                                d["ebcdic_cp_us"] = 
                                d["ebcdic_cp_wt"] = 
                                d["ibm039"] = encs[i];
                            break;
                        case "gb18030":
                            d["gb18030_2000"] = encs[i];
                            break;
                    }

                    switch (encs[i].CodePage) {
                        case 500: d["csibm500"] = 
                            d["ebcdic_cp_be"] = 
                            d["ebcdic_cp_ch"] = encs[i]; break;
                        case 1026: d["csibm1026"] = encs[i]; break;
                        case 1140: d["ibm1140"] = encs[i]; break;
                        case 850: d["cspc850multilingual"] = encs[i]; break;
                        case 852: d["cspcp852"] = encs[i]; break;
                        case 855: d["csibm855"] = encs[i]; break;
                        case 857: d["csibm857"] = encs[i]; break;
                        case 858: d["csibm858"] = d["ibm858"] = encs[i]; break;
                        case 861: d["csibm861"] = d["cp_is"] = encs[i]; break;
                        case 862: d["cspc862latinhebrew"] = encs[i]; break;
                        case 863: d["csibm863"] = encs[i]; break;
                        case 864: d["csibm864"] = encs[i]; break;
                        case 865: d["csibm865"] = encs[i]; break;
                        case 866: d["csibm866"] = encs[i]; break;
                        case 869: d["csibm869"] = d["cp_gr"] = encs[i]; break;
                        case 932: d["csshiftjis"] = 
                                d["shiftjis"] = 
                                d["sjis"] = 
                                d["s_jis"] = 
                                d["shiftjis2004"] = 
                                d["sjis_2004"] = 
                                d["s_jis_2004"] = 
                                d["x_mac_japanese"] = 
                                d["mskanji"] = 
                                d["ms_kanji"] = encs[i];
                            break;
                        case 949: d["uhc"] = d["ms949"] = encs[i]; break;
                        case 51949: d["euckr"] = 
                                d["korean"] = 
                                d["ksc5601"] = 
                                d["ks_c_5601"] = 
                                d["ks_c_5601_1987"] = 
                                d["ksx1001"] = 
                                d["ks_x_1001"] = 
                                d["x_mac_korean"] = encs[i];
                            break;
                        case 52936: d["hz"] = 
                                d["hzgb"] = 
                                d["hz_gb"] = encs[i];
                            break;
                        case 50220: d["iso2022_jp"] = d["iso2022jp"] = encs[i]; break;
                        case 50221: d["iso2022_jp_1"] =
                                d["iso2022jp_1"] = 
                                d["iso_2022_jp_1"] = encs[i];
                            break;
                        case 50222: d["iso2022_jp_2"] = 
                                d["iso2022jp_2"] = 
                                d["iso_2022_jp_2"] = encs[i];
                            break;
                        case 50225: d["csiso2022kr"] = 
                                d["iso2022kr"] = 
                                d["iso_2022_kr"] = encs[i];
                            break;
                        case 28603: d["iso8859_13"] = 
                                d["iso_8859_13"] = 
                                d["l7"] = 
                                d["latin7"] = encs[i];
                            break;
                        case 28605: d["iso8859_15"] = 
                                d["l9"] = 
                                d["latin9"] = encs[i];
                            break;
                        case 28592: d["csisolatin2"] = 
                                d["iso_8859_2_1987"] = 
                                d["iso_ir_101"] = 
                                d["l2"] = 
                                d["latin2"] = encs[i];
                            break;
                        case 28593: d["csisolatin3"] = 
                                d["iso_8859_3_1988"] = 
                                d["iso_ir_109"] = 
                                d["l3"] = 
                                d["latin3"] = encs[i];
                            break;
                        case 28594: d["csisolatin4"] = 
                                d["iso_8859_4_1988"] = 
                                d["iso_ir_110"] = 
                                d["l4"] = 
                                d["latin4"] = encs[i];
                            break;
                        case 28595: d["csisolatincyrillic"] = 
                                d["cyrillic"] = 
                                d["iso_8859_5_1988"] = 
                                d["iso_ir_144"] = encs[i];
                            break;
                        case 28596: d["arabic"] = 
                                d["asmo_708"] = 
                                d["csisolatinarabic"] = 
                                d["ecma_114"] = 
                                d["iso_8859_6_1987"] = 
                                d["iso_ir_127"] = encs[i];
                            break;
                        case 28597: d["csisolatingreek"] = 
                                d["ecma_118"] = 
                                d["elot_928"] = 
                                d["greek"] = 
                                d["greek8"] = 
                                d["iso_8859_7_1987"] = 
                                d["iso_ir_126"] = encs[i];
                            break;
                        case 28598: d["csisolatinhebrew"] = 
                                d["hebrew"] = 
                                d["iso_8859_8_1988"] = 
                                d["iso_ir_138"] = encs[i];
                            break;
                        case 28599: d["csisolatin5"] = 
                                d["iso_8859_9_1989"] = 
                                d["iso_ir_148"] = 
                                d["l5"] = 
                                d["latin5"] = encs[i];
                            break;
                        case 1361: d["ms1361"] = encs[i]; break;
                        case 20866: d["cskoi8r"] = encs[i]; break;
                        case 10006: d["macgreek"] = d["mac_greek"] = encs[i]; break;
                        case 10007: d["mac_cyrillic"] = d["maccyrillic"] = encs[i]; break;
                        case 10079: d["maciceland"] = d["mac_iceland"] = encs[i]; break;
                        case 10081: d["macturkish"] = d["mac_turkish"] = encs[i]; break;
                        case 10010: d["mac_romanian"] = encs[i]; break;
                        case 10004: d["mac_arabic"] = encs[i]; break;
                        case 10082: d["mac_croatian"] = encs[i]; break;
                    }

                    // publish under normalized name (all lower cases, -s replaced with _s)
                    d[normalizedName] = encs[i];
                    // publish under Windows code page as well...                
                    d["windows-" + encs[i].GetEncoding().WindowsCodePage.ToString()] = encs[i];
                    // publish under code page number as well...
                    d["cp" + encs[i].CodePage.ToString()] = d[encs[i].CodePage.ToString()] = encs[i];
                }

#if DEBUG
                // all codecs should be stored in lowercase because we only look up from lowercase strings
                foreach (KeyValuePair<string, EncodingInfoWrapper> kvp in d) {
                    Debug.Assert(kvp.Key.ToLowerInvariant() == kvp.Key);
                }
#endif
                return d;
            }
        }
    }
}
