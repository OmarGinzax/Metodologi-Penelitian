using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RPGStrategy.AI.PureLLM
{
    /// <summary>
    /// Parser JSON minimal tanpa dependensi eksternal (project tidak memakai Newtonsoft).
    /// Mendukung object, array, string, number, bool, dan null. Dipakai untuk:
    ///   1. Membaca respons mentah LLM (toleran terhadap markdown fence/teks pembuka).
    ///   2. Membaca payload respons OpenAI/Ollama.
    /// </summary>
    public static class JsonUtil
    {
        public static string Escape(string value)
        {
            if (value == null) return string.Empty;

            var sb = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Mengambil objek JSON pertama yang seimbang dari teks mentah LLM.
        /// Toleran terhadap ```json fences, kalimat pembuka, dan teks penutup.
        /// Mengembalikan null jika tidak ada objek JSON.
        /// </summary>
        public static string ExtractFirstJsonObject(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            int start = raw.IndexOf('{');
            if (start < 0) return null;

            int depth = 0;
            bool inString = false;
            bool escape = false;

            for (int i = start; i < raw.Length; i++)
            {
                char c = raw[i];

                if (inString)
                {
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') { inString = true; continue; }
                if (c == '{') { depth++; continue; }
                if (c == '}')
                {
                    depth--;
                    if (depth == 0) return raw.Substring(start, i - start + 1);
                }
            }

            return null;
        }

        /// <summary>Parse teks JSON menjadi object graph (Dictionary/List/string/double/bool/null).</summary>
        public static object Parse(string json)
        {
            if (json == null) throw new FormatException("JSON is null.");
            int index = 0;
            object value = ParseValue(json, ref index);
            SkipWhitespace(json, ref index);
            if (index < json.Length)
                throw new FormatException("Unexpected trailing JSON content at index " + index + ".");
            return value;
        }

        public static Dictionary<string, object> AsObject(object value)
            => value as Dictionary<string, object>;

        public static List<object> AsArray(object value)
            => value as List<object>;

        public static string GetString(IDictionary<string, object> obj, string key, string fallback = null)
        {
            if (obj == null || key == null) return fallback;
            object v;
            if (!obj.TryGetValue(key, out v) || v == null) return fallback;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static int GetInt(IDictionary<string, object> obj, string key, int fallback = 0)
        {
            if (obj == null || key == null) return fallback;
            object v;
            if (!obj.TryGetValue(key, out v) || v == null) return fallback;

            if (v is double) return (int)Math.Round((double)v);
            if (v is long) return (int)(long)v;
            if (v is int) return (int)v;
            if (v is string)
            {
                int parsed;
                if (int.TryParse((string)v, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    return parsed;
            }
            return fallback;
        }

        public static bool GetBool(IDictionary<string, object> obj, string key, bool fallback = false)
        {
            if (obj == null || key == null) return fallback;
            object v;
            if (!obj.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v;
            if (v is string)
            {
                bool parsed;
                if (bool.TryParse((string)v, out parsed)) return parsed;
            }
            return fallback;
        }

        public static bool HasKey(IDictionary<string, object> obj, string key)
            => obj != null && key != null && obj.ContainsKey(key);

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) throw new FormatException("Unexpected end of JSON.");

            char c = json[index];
            switch (c)
            {
                case '{': return ParseObject(json, ref index);
                case '[': return ParseArray(json, ref index);
                case '"': return ParseString(json, ref index);
                case 't':
                    Expect(json, ref index, "true");
                    return true;
                case 'f':
                    Expect(json, ref index, "false");
                    return false;
                case 'n':
                    Expect(json, ref index, "null");
                    return null;
                default:
                    return ParseNumber(json, ref index);
            }
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            index++; // {
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == '}')
            {
                index++;
                return result;
            }

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != '"')
                    throw new FormatException("Expected JSON object key string at index " + index + ".");

                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != ':')
                    throw new FormatException("Expected ':' after JSON key '" + key + "'.");
                index++;
                result[key] = ParseValue(json, ref index);
                SkipWhitespace(json, ref index);

                if (index >= json.Length) throw new FormatException("Unterminated JSON object.");
                if (json[index] == ',') { index++; continue; }
                if (json[index] == '}') { index++; return result; }
                throw new FormatException("Expected ',' or '}' in JSON object at index " + index + ".");
            }

            throw new FormatException("Unterminated JSON object.");
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var result = new List<object>();
            index++; // [
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == ']')
            {
                index++;
                return result;
            }

            while (index < json.Length)
            {
                result.Add(ParseValue(json, ref index));
                SkipWhitespace(json, ref index);

                if (index >= json.Length) throw new FormatException("Unterminated JSON array.");
                if (json[index] == ',') { index++; continue; }
                if (json[index] == ']') { index++; return result; }
                throw new FormatException("Expected ',' or ']' in JSON array at index " + index + ".");
            }

            throw new FormatException("Unterminated JSON array.");
        }

        private static string ParseString(string json, ref int index)
        {
            index++; // opening quote
            var sb = new StringBuilder();

            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '"') return sb.ToString();

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (index >= json.Length) throw new FormatException("Unterminated JSON escape sequence.");
                char esc = json[index++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (index + 4 > json.Length) throw new FormatException("Invalid unicode escape.");
                        string hex = json.Substring(index, 4);
                        int code;
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            throw new FormatException("Invalid unicode escape '\\u" + hex + "'.");
                        sb.Append((char)code);
                        index += 4;
                        break;
                    default:
                        throw new FormatException("Invalid JSON escape '\\" + esc + "'.");
                }
            }

            throw new FormatException("Unterminated JSON string.");
        }

        private static double ParseNumber(string json, ref int index)
        {
            int start = index;
            if (index < json.Length && (json[index] == '-' || json[index] == '+')) index++;

            while (index < json.Length)
            {
                char c = json[index];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+')
                    index++;
                else
                    break;
            }

            string token = json.Substring(start, index - start);
            double value;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Invalid JSON number '" + token + "'.");
            return value;
        }

        private static void Expect(string json, ref int index, string token)
        {
            if (index + token.Length > json.Length ||
                string.CompareOrdinal(json, index, token, 0, token.Length) != 0)
            {
                throw new FormatException("Expected JSON token '" + token + "' at index " + index + ".");
            }
            index += token.Length;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length)
            {
                char c = json[index];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') index++;
                else break;
            }
        }
    }
}
