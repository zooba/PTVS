using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.PythonTools.Parsing {
    /// <summary>
    /// Provides helper functions for encoding support (PEP 263).
    /// http://www.python.org/dev/peps/pep-0263/
    /// </summary>
    static class PythonEncoding {
        private static Encoding _utf8throwing;
        private static readonly Encoding _defaultEncoding = new UTF8Encoding(false);
        private static readonly Regex _codingRegex = new Regex("coding[:=]\\s*([-\\w.]+)", RegexOptions.Compiled);

        /// <summary>
        /// Returns the Encoding that a Python file is written in.  This inspects the BOM and looks for a #coding line
        /// in the provided stream.
        /// 
        /// Returns null if the encoding could not be detected for any reason.
        /// </summary>
        public static Encoding GetEncodingFromStream(Stream stream) {
            return GetStreamReaderWithEncoding(stream, PythonLanguageVersion.None, ErrorSink.Null).CurrentEncoding;
        }

        public static StreamReader GetStreamReaderWithEncoding(Stream stream, PythonLanguageVersion languageVersion, ErrorSink errors) {
            // A BOM or encoding comment can override the default
            Encoding encoding = _defaultEncoding;
            if (languageVersion.Is2x()) {
                encoding = PythonAsciiEncoding.Instance;
            }

            List<byte> readBytes = new List<byte>();
            try {
                byte[] bomBuffer = new byte[3];
                int bomRead = stream.Read(bomBuffer, 0, 3);
                int bytesRead = 0;
                bool isUtf8 = false;
                if (bomRead == 3 && (bomBuffer[0] == 0xef && bomBuffer[1] == 0xbb && bomBuffer[2] == 0xbf)) {
                    isUtf8 = true;
                    bytesRead = 3;
                    readBytes.AddRange(bomBuffer);
                } else {
                    for (int i = 0; i < bomRead; i++) {
                        readBytes.Add(bomBuffer[i]);
                    }
                }

                int lineLength;
                string line = ReadOneLine(readBytes, ref bytesRead, stream, out lineLength);

                bool? gotEncoding = false;
                string encodingName = null;
                // magic encoding must be on line 1 or 2
                int lineNo = 1;
                int encodingIndex = 0;
                if ((gotEncoding = TryGetEncoding(line, ref encoding, out encodingName, out encodingIndex)) == false) {
                    var prevLineLength = lineLength;
                    line = ReadOneLine(readBytes, ref bytesRead, stream, out lineLength);
                    lineNo = 2;
                    gotEncoding = TryGetEncoding(line, ref encoding, out encodingName, out encodingIndex);
                    encodingIndex += prevLineLength;
                }

                if ((gotEncoding == null || gotEncoding == true) && isUtf8 && encodingName != "utf-8") {
                    // we have both a BOM & an encoding type, throw an error
                    errors.Add(
                        "file has both Unicode marker and PEP-263 file encoding.  You must use \"utf-8\" as the encoding name when a BOM is present.",
                        new IndexSpan(encodingIndex, encodingName.Length),
                        ErrorCodes.SyntaxError,
                        Severity.FatalError
                    );
                    encoding = Encoding.UTF8;
                } else if (isUtf8) {
                    return new StreamReader(new PartiallyReadStream(readBytes, stream), UTF8Throwing);
                } else if (encoding == null) {
                    if (gotEncoding == null) {
                        // get line number information for the bytes we've read...
                        errors.Add(
                            String.Format("encoding problem: unknown encoding (line {0})", lineNo),
                            new IndexSpan(encodingIndex, encodingName.Length),
                            ErrorCodes.SyntaxError,
                            Severity.Error
                        );
                    }
                    return new StreamReader(new PartiallyReadStream(readBytes, stream), _defaultEncoding);
                }

                // re-read w/ the correct encoding type...
                return new StreamReader(new PartiallyReadStream(readBytes, stream), encoding);
            } catch (EncoderFallbackException ex) {
                errors.Add(ex.Message, new IndexSpan(ex.Index, 1), ErrorCodes.SyntaxError, Severity.FatalError);
                return new StreamReader(new PartiallyReadStream(readBytes, stream), encoding);
            }
        }

        private static int[] GetEncodingLineNumbers(IList<byte> readBytes) {
            int[] lineNos = new int[2];
            for (int i = 0, lineCount = 0; i < readBytes.Count && lineCount < 2; i++) {
                if (readBytes[i] == '\r') {
                    lineNos[lineCount++] = i;
                    if (i + 1 < readBytes.Count && readBytes[i + 1] == '\n') {
                        i++;
                    }
                } else if (readBytes[i] == '\n') {
                    lineNos[lineCount++] = i;
                }
            }
            return lineNos;
        }

        private static Encoding UTF8Throwing {
            get {
                if (_utf8throwing == null) {
                    var tmp = (Encoding)Encoding.UTF8.Clone();
                    tmp.DecoderFallback = new SourceNonStrictDecoderFallback();
                    _utf8throwing = tmp;
                }
                return _utf8throwing;
            }
        }

        /// <summary>
        /// Attempts to get the encoding from a # coding: line.  
        /// 
        /// Returns true if we successfully parse the encoding line and get the encoding, false if there's no encoding line, or
        /// null if the encoding line exists but the codec is unknown.
        /// </summary>
        internal static bool? TryGetEncoding(string line, ref Encoding enc, out string encName, out int index) {
            // encoding is "# coding: <encoding name>
            // minimum length is 18
            encName = null;
            index = 0;
            if (line.Length < 10) return false;
            if (line[0] != '#') return false;

            // we have magic comment line
            //int codingIndex;
            Match match;
            if (!(match = _codingRegex.Match(line)).Success) {
                return false;
            }

            // get the encoding string name
            index = match.Groups[1].Index;
            encName = match.Groups[1].Value;

            // and we have the magic ending as well...
            if (TryGetEncoding(encName, out enc)) {
                enc.DecoderFallback = new SourceNonStrictDecoderFallback();
                return true;
            }
            return null;
        }

        internal static bool TryGetEncoding(string name, out Encoding encoding) {
            name = NormalizeEncodingName(name);

            EncodingInfoWrapper encInfo;
            if (CodecsInfo.Codecs.TryGetValue(name, out encInfo)) {
                encoding = (Encoding)encInfo.GetEncoding().Clone();
                return true;
            }

            encoding = null;
            return false;
        }

        static class CodecsInfo {
            public static readonly Dictionary<string, EncodingInfoWrapper> Codecs = MakeCodecsDict();

            private static Dictionary<string, EncodingInfoWrapper> MakeCodecsDict() {
                Dictionary<string, EncodingInfoWrapper> d = new Dictionary<string, EncodingInfoWrapper>();
                EncodingInfo[] encs = Encoding.GetEncodings();
                for (int i = 0; i < encs.Length; i++) {
                    string normalizedName = NormalizeEncodingName(encs[i].Name);

                    // setup well-known mappings, for everything
                    // else we'll store as lower case w/ _                
                    switch (normalizedName) {
                        case "us_ascii":
                            d["cp" + encs[i].CodePage.ToString()] = d[normalizedName] = d["us"] = d["ascii"] = d["646"] = d["us_ascii"] =
                                d["ansi_x3.4_1968"] = d["ansi_x3_4_1968"] = d["ansi_x3.4_1986"] = d["cp367"] = d["csascii"] = d["ibm367"] =
                                d["iso646_us"] = d["iso_646.irv_1991"] = d["iso_ir_6"]
                                = new AsciiEncodingInfoWrapper();
                            continue;
                        case "iso_8859_1":
                            d["iso_ir_100"] = d["iso_8859_1_1987"] = d["iso_8859_1"] = d["iso8859"] = d["ibm819"] = d["csisolatin1"] = d["8859"] = d["latin_1"] =
                            d["latin1"] = d["iso 8859_1"] = d["iso8859_1"] = d["cp819"] = d["819"] = d["latin"] = d["latin1"] = d["l1"] = encs[i];
                            break;
                        case "utf_7":
                            d["u7"] = d["unicode-1-1-utf-7"] = encs[i];
                            break;
                        case "utf_8":
                            d["utf_8_sig"] = encs[i];
                            d["utf_8"] = d["utf8"] = d["u8"] = new EncodingInfoWrapper(encs[i], new byte[0]);
                            continue;
                        case "utf_16":
                            d["utf_16_le"] = d["utf_16le"] = new EncodingInfoWrapper(encs[i], new byte[0]);
                            d["utf16"] = new EncodingInfoWrapper(encs[i], encs[i].GetEncoding().GetPreamble());
                            break;
                        case "unicodefffe": // big endian unicode                    
                            // strip off the pre-amble, CPython doesn't include it.
                            d["utf_16_be"] = d["utf_16be"] = new EncodingInfoWrapper(encs[i], new byte[0]);
                            break;
                        case "gb2312":
                            d["x_mac_simp_chinese"] = d["936"] = d["ms936"] = d["chinese"] = d["csiso58gb231280"] = d["euc_cn"] = d["euccn"] = d["eucgb2312_cn"] = d["gb2312_1980"] =
                            d["gb2312_80"] = d["iso_ir_58"] = d["gbk"] = encs[i];
                            break;
                        case "big5":
                            d["x_mac_trad_chinese"] = d["big5_tw"] = d["csbig5"] = encs[i];
                            break;
                        case "cp950":
                            d["ms950"] = d["hkscs"] = d["big5_hkscs"] = encs[i];
                            break;
                        case "ibm037":
                            d["cp037"] = d["csibm037"] = d["ebcdic_cp_ca"] = d["ebcdic_cp_nl"] = d["ebcdic_cp_us"] = d["ebcdic_cp_wt"] = d["ibm039"] = encs[i];
                            break;
                        case "gb18030": d["gb18030_2000"] = encs[i]; break;
                    }

                    switch (encs[i].CodePage) {
                        case 500: d["csibm500"] = d["ebcdic_cp_be"] = d["ebcdic_cp_ch"] = encs[i]; break;
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
                        case 932: d["csshiftjis"] = d["shiftjis"] = d["sjis"] = d["s_jis"] = d["shiftjis2004"] = d["sjis_2004"] = d["s_jis_2004"] = d["x_mac_japanese"] = d["mskanji"] = d["ms_kanji"] = encs[i]; break;
                        case 949: d["uhc"] = d["ms949"] = encs[i]; break;
                        case 51949: d["euckr"] = d["korean"] = d["ksc5601"] = d["ks_c_5601"] = d["ks_c_5601_1987"] = d["ksx1001"] = d["ks_x_1001"] = d["x_mac_korean"] = encs[i]; break;
                        case 52936: d["hz"] = d["hzgb"] = d["hz_gb"] = encs[i]; break;
                        case 50220: d["iso2022_jp"] = d["iso2022jp"] = encs[i]; break;
                        case 50221: d["iso2022_jp_1"] = d["iso2022jp_1"] = d["iso_2022_jp_1"] = encs[i]; break;
                        case 50222: d["iso2022_jp_2"] = d["iso2022jp_2"] = d["iso_2022_jp_2"] = encs[i]; break;
                        case 50225: d["csiso2022kr"] = d["iso2022kr"] = d["iso_2022_kr"] = encs[i]; break;
                        case 28603: d["iso8859_13"] = d["iso_8859_13"] = d["l7"] = d["latin7"] = encs[i]; break;
                        case 28605: d["iso8859_15"] = d["l9"] = d["latin9"] = encs[i]; break;
                        case 28592: d["csisolatin2"] = d["iso_8859_2_1987"] = d["iso_ir_101"] = d["l2"] = d["latin2"] = encs[i]; break;
                        case 28593: d["csisolatin3"] = d["iso_8859_3_1988"] = d["iso_ir_109"] = d["l3"] = d["latin3"] = encs[i]; break;
                        case 28594: d["csisolatin4"] = d["iso_8859_4_1988"] = d["iso_ir_110"] = d["l4"] = d["latin4"] = encs[i]; break;
                        case 28595: d["csisolatincyrillic"] = d["cyrillic"] = d["iso_8859_5_1988"] = d["iso_ir_144"] = encs[i]; break;
                        case 28596: d["arabic"] = d["asmo_708"] = d["csisolatinarabic"] = d["ecma_114"] = d["iso_8859_6_1987"] = d["iso_ir_127"] = encs[i]; break;
                        case 28597: d["csisolatingreek"] = d["ecma_118"] = d["elot_928"] = d["greek"] = d["greek8"] = d["iso_8859_7_1987"] = d["iso_ir_126"] = encs[i]; break;
                        case 28598: d["csisolatinhebrew"] = d["hebrew"] = d["iso_8859_8_1988"] = d["iso_ir_138"] = encs[i]; break;
                        case 28599: d["csisolatin5"] = d["iso_8859_9_1989"] = d["iso_ir_148"] = d["l5"] = d["latin5"] = encs[i]; break;
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

        class EncodingInfoWrapper {
            private EncodingInfo _info;
            private Encoding _encoding;
            private byte[] _preamble;

            public EncodingInfoWrapper(Encoding enc) {
                _encoding = enc;
            }

            public EncodingInfoWrapper(EncodingInfo info) {
                _info = info;
            }

            public EncodingInfoWrapper(EncodingInfo info, byte[] preamble) {
                _info = info;
                _preamble = preamble;
            }

            public virtual Encoding GetEncoding() {
                if (_encoding != null) return _encoding;

                if (_preamble == null) {
                    return _info.GetEncoding();
                }

                return new EncodingWrapper(_info.GetEncoding(), _preamble);
            }

            public static implicit operator EncodingInfoWrapper(EncodingInfo info) {
                return new EncodingInfoWrapper(info);
            }
        }

        class AsciiEncodingInfoWrapper : EncodingInfoWrapper {
            public AsciiEncodingInfoWrapper()
                : base((EncodingInfo)null) {
            }

            public override Encoding GetEncoding() {
                return PythonAsciiEncoding.Instance;
            }
        }

        class EncodingWrapper : Encoding {
            private byte[] _preamble;
            private Encoding _encoding;

            public EncodingWrapper(Encoding encoding, byte[] preamable) {
                _preamble = preamable;
                _encoding = encoding;
            }

            private void SetEncoderFallback() {
                _encoding.EncoderFallback = EncoderFallback;
            }

            private void SetDecoderFallback() {
                _encoding.DecoderFallback = DecoderFallback;
            }

            public override int CodePage {
                get {
                    return _encoding.CodePage;
                }
            }

            public override string EncodingName {
                get {
                    return _encoding.EncodingName;
                }
            }

            public override string WebName {
                get {
                    return _encoding.WebName;
                }
            }

            public override int GetByteCount(char[] chars, int index, int count) {
                SetEncoderFallback();
                return _encoding.GetByteCount(chars, index, count);
            }

            public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) {
                SetEncoderFallback();
                return _encoding.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
            }

            public override int GetCharCount(byte[] bytes, int index, int count) {
                SetDecoderFallback();
                return _encoding.GetCharCount(bytes, index, count);
            }

            public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) {
                SetDecoderFallback();
                return _encoding.GetChars(bytes, byteIndex, byteCount, chars, charIndex);
            }

            public override int GetMaxByteCount(int charCount) {
                SetEncoderFallback();
                return _encoding.GetMaxByteCount(charCount);
            }

            public override int GetMaxCharCount(int byteCount) {
                SetDecoderFallback();
                return _encoding.GetMaxCharCount(byteCount);
            }

            public override byte[] GetPreamble() {
                return _preamble;
            }

            public override Encoder GetEncoder() {
                SetEncoderFallback();
                return _encoding.GetEncoder();
            }

            public override Decoder GetDecoder() {
                SetDecoderFallback();
                return _encoding.GetDecoder();
            }

            public override object Clone() {
                // need to call base.Clone to be marked as read/write
                EncodingWrapper res = (EncodingWrapper)base.Clone();
                res._encoding = (Encoding)_encoding.Clone();
                return res;
            }
        }

        internal static string NormalizeEncodingName(string name) {
            if (name == null) {
                return null;
            }
            return name.ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        }

        /// <summary>
        /// Reads one line keeping track of the # of bytes read and saving the bytes that were read
        /// </summary>
        private static string ReadOneLine(List<byte> previewedBytes, ref int curIndex, Stream reader, out int lineLength) {
            lineLength = 0;
            byte[] buffer = new byte[256];
            int bufferReadCount = reader.Read(buffer, 0, buffer.Length);
            for (int i = 0; i < bufferReadCount; i++) {
                previewedBytes.Add(buffer[i]);
            }

            int startIndex = curIndex;
            do {
                for (int i = curIndex; i < previewedBytes.Count; i++) {
                    bool foundEnd = false;

                    if (previewedBytes[i] == '\r') {
                        if (i + 1 < previewedBytes.Count) {
                            if (previewedBytes[i + 1] == '\n') {
                                lineLength = 2;
                                curIndex = i + 2;
                                foundEnd = true;
                            }
                        } else {
                            lineLength = 1;
                            curIndex = i + 1;
                            foundEnd = true;
                        }
                    } else if (previewedBytes[i] == '\n') {
                        lineLength = 1;
                        curIndex = i + 1;
                        foundEnd = true;
                    }

                    if (foundEnd) {
                        var bytes = new byte[i - startIndex];
                        previewedBytes.CopyTo(startIndex, bytes, 0, bytes.Length);
                        var res = _defaultEncoding.GetString(bytes);
                        lineLength += res.Length;
                        return res;
                    }
                }

                bufferReadCount = reader.Read(buffer, 0, buffer.Length);
                for (int i = 0; i < bufferReadCount; i++) {
                    previewedBytes.Add(buffer[i]);
                }
            } while (bufferReadCount != 0);

            // no new-line
            curIndex = previewedBytes.Count;
            var noNewlineRes = _defaultEncoding.GetString(previewedBytes.ToArray());
            lineLength = noNewlineRes.Length;
            return noNewlineRes;
        }


        /// <summary>
        /// Returns an Encoding object which raises a BadSourceException when
        /// invalid characters are encountered.
        /// </summary>
        public static Encoding DefaultEncoding {
            get {
                return PythonAsciiEncoding.SourceEncoding;
            }
        }

        /// <summary>
        /// Returns an Encoding object which will not provide any fallback for
        /// invalid characters.
        /// </summary>
        public static Encoding DefaultEncodingNoFallback {
            get {
                return PythonAsciiEncoding.SourceEncodingNoFallback;
            }
        }
    }
}
