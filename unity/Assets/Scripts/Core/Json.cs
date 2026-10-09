using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Eonward.Core
{
    /// <summary>
    /// Minimal JSON reader/writer. Objects are Dictionary&lt;string, object&gt;, arrays are
    /// List&lt;object&gt;, numbers are double. Kept dependency-free so the core compiles the
    /// same in Unity (IL2CPP, no reflection) and in the plain .NET test project.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.SkipWhitespace();
            var value = p.ReadValue();
            p.SkipWhitespace();
            if (!p.AtEnd) throw new FormatException($"Unexpected data at position {p.Position}.");
            return value;
        }

        sealed class Parser
        {
            readonly string s;
            int i;

            public Parser(string text) { s = text ?? throw new FormatException("No JSON text."); }
            public int Position => i;
            public bool AtEnd => i >= s.Length;

            public void SkipWhitespace()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            char Peek() => i < s.Length ? s[i] : throw new FormatException("Unexpected end of JSON.");

            void Expect(char c)
            {
                if (Peek() != c) throw new FormatException($"Expected '{c}' at position {i}.");
                i++;
            }

            public object ReadValue()
            {
                SkipWhitespace();
                char c = Peek();
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': ReadLiteral("true"); return true;
                    case 'f': ReadLiteral("false"); return false;
                    case 'n': ReadLiteral("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw new FormatException($"Unexpected character '{c}' at position {i}.");
                }
            }

            void ReadLiteral(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                    throw new FormatException($"Invalid literal at position {i}.");
                i += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var obj = new Dictionary<string, object>();
                Expect('{');
                SkipWhitespace();
                if (Peek() == '}') { i++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    string key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    obj[key] = ReadValue();
                    SkipWhitespace();
                    char c = Peek();
                    i++;
                    if (c == '}') return obj;
                    if (c != ',') throw new FormatException($"Expected ',' or '}}' at position {i - 1}.");
                }
            }

            List<object> ReadArray()
            {
                var list = new List<object>();
                Expect('[');
                SkipWhitespace();
                if (Peek() == ']') { i++; return list; }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipWhitespace();
                    char c = Peek();
                    i++;
                    if (c == ']') return list;
                    if (c != ',') throw new FormatException($"Expected ',' or ']' at position {i - 1}.");
                }
            }

            string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    char c = Peek();
                    i++;
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = Peek();
                    i++;
                    switch (e)
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
                            if (i + 4 > s.Length) throw new FormatException("Bad unicode escape.");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw new FormatException($"Bad escape '\\{e}' at position {i}.");
                    }
                }
            }

            double ReadNumber()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                var token = s.Substring(start, i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    throw new FormatException($"Bad number '{token}' at position {start}.");
                return d;
            }
        }

        // MARK: - Writing

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case string str: WriteString(sb, str); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: WriteNumber(sb, d); break;
                case float f: WriteNumber(sb, f); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> obj:
                {
                    sb.Append('{');
                    bool first = true;
                    var keys = new List<string>(obj.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    foreach (var k in keys)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, k);
                        sb.Append(':');
                        WriteValue(sb, obj[k]);
                    }
                    sb.Append('}');
                    break;
                }
                case System.Collections.IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                }
                default: throw new FormatException($"Cannot write {value.GetType().Name} as JSON.");
            }
        }

        static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new FormatException("JSON cannot store NaN or infinity.");
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string str)
        {
            sb.Append('"');
            foreach (char c in str)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>Typed accessors over a parsed JSON object; missing or wrong-typed fields throw
    /// <see cref="FormatException"/> with the field path, so bad content/saves fail clearly.</summary>
    public sealed class JObj
    {
        readonly Dictionary<string, object> d;
        readonly string path;

        public JObj(object value, string path = "$")
        {
            d = value as Dictionary<string, object> ?? throw new FormatException($"{path} must be an object.");
            this.path = path;
        }

        public bool Has(string key) => d.TryGetValue(key, out var v) && v != null;

        object Get(string key) =>
            d.TryGetValue(key, out var v) && v != null ? v : throw new FormatException($"{path}.{key} is missing.");

        public string Str(string key) => Get(key) as string ?? throw new FormatException($"{path}.{key} must be a string.");
        public string OptStr(string key) => Has(key) ? Str(key) : null;

        public double Num(string key) => Get(key) is double n ? n : throw new FormatException($"{path}.{key} must be a number.");
        public double? OptNum(string key) => Has(key) ? Num(key) : (double?)null;
        public int Int(string key) => (int)Math.Round(Num(key));
        public int? OptInt(string key) => Has(key) ? Int(key) : (int?)null;

        public bool Bool(string key) => Get(key) is bool b ? b : throw new FormatException($"{path}.{key} must be true/false.");
        public bool OptBool(string key, bool fallback) => Has(key) ? Bool(key) : fallback;

        public JObj Obj(string key) => new JObj(Get(key), $"{path}.{key}");
        public JObj OptObj(string key) => Has(key) ? Obj(key) : null;

        public List<object> Arr(string key) => Get(key) as List<object> ?? throw new FormatException($"{path}.{key} must be an array.");
        public List<object> OptArr(string key) => Has(key) ? Arr(key) : new List<object>();

        public List<JObj> Objs(string key)
        {
            var result = new List<JObj>();
            var arr = OptArr(key);
            for (int i = 0; i < arr.Count; i++) result.Add(new JObj(arr[i], $"{path}.{key}[{i}]"));
            return result;
        }

        public List<string> Strs(string key)
        {
            var result = new List<string>();
            foreach (var v in OptArr(key)) result.Add(v as string ?? throw new FormatException($"{path}.{key} must contain strings."));
            return result;
        }

        public IEnumerable<string> Keys => d.Keys;
        public Dictionary<string, object> Raw => d;
    }
}
