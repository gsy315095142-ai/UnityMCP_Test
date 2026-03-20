#nullable enable

using System;
using System.Text.RegularExpressions;

namespace UnityMCP.AI
{
    /// <summary>
    /// 将 AI 返回的预制体 JSON 规范化，提高 JsonUtility 与后续解析的成功率（Phase 2-B）。
    /// </summary>
    public static class PrefabJsonSanitizer
    {
        private static readonly Regex TrailingCommaRegex = new(@",(\s*[\]}])", RegexOptions.Compiled);

        /// <summary>
        /// 去 BOM、去尾逗号、补全别名、将组件的 properties 对象转为 propertiesJson 字符串等。
        /// </summary>
        public static string Sanitize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;

            var s = json.Trim();
            if (s.Length > 0 && s[0] == '\uFEFF')
                s = s.Substring(1);

            s = RemoveTrailingCommasRepeated(s);
            s = ApplyTopLevelKeyNormalization(s);
            s = FixCommonPascalGameObjectKeys(s);
            s = TransformComponentPropertiesToJsonString(s);
            s = RemoveTrailingCommasRepeated(s);
            return s;
        }

        private static string RemoveTrailingCommasRepeated(string json)
        {
            var prev = json;
            for (var i = 0; i < 8; i++)
            {
                var next = TrailingCommaRegex.Replace(prev, "$1");
                if (next == prev) break;
                prev = next;
            }

            return prev;
        }

        private static string ApplyTopLevelKeyNormalization(string json)
        {
            var s = json;
            s = Regex.Replace(s, @"(?i)""prefabname""\s*:", "\"prefabName\":");
            s = Regex.Replace(s, @"(?i)""rootobject""\s*:", "\"rootObject\":");

            if (!s.Contains("\"rootObject\"") &&
                Regex.IsMatch(s, @"(?i)""root""\s*:\s*\{"))
            {
                var m = Regex.Match(s, @"(?i)""root""\s*:");
                if (m.Success)
                    s = s.Substring(0, m.Index) + "\"rootObject\":" + s.Substring(m.Index + m.Length);
            }

            return s;
        }

        /// <summary>
        /// JsonUtility 的组件字段为 propertiesJson（字符串）。将 AI 常用的 ""properties"": { ... } 转为该形式。
        /// </summary>
        public static string TransformComponentPropertiesToJsonString(string json)
        {
            var s = json;
            for (var guard = 0; guard < 256; guard++)
            {
                var m = Regex.Match(s, @"""properties""\s*:\s*\{", RegexOptions.IgnoreCase);
                if (!m.Success) break;

                var open = m.Index + m.Length - 1;
                var close = FindMatchingClosingBrace(s, open);
                if (close < 0) break;

                var innerObject = s.Substring(open, close - open + 1);
                var embedded = EscapeForJsonStringValue(innerObject);
                var replacement = "\"propertiesJson\":\"" + embedded + "\"";
                s = s.Substring(0, m.Index) + replacement + s.Substring(close + 1);
            }

            return s;
        }

        private static string FixCommonPascalGameObjectKeys(string json)
        {
            var s = json;
            var pairs = new (string From, string To)[]
            {
                ("Name", "name"), ("Tag", "tag"), ("Layer", "layer"), ("Active", "active"),
                ("Position", "position"), ("Rotation", "rotation"), ("Scale", "scale"),
                ("Components", "components"), ("Children", "children")
            };

            foreach (var (from, to) in pairs)
                s = Regex.Replace(s, $"\"{from}\"\\s*:", $"\"{to}\":");

            return s;
        }

        private static string EscapeForJsonStringValue(string raw)
        {
            return raw
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        /// <summary>
        /// 从第一个 '{' 起匹配成对的括号，忽略字符串内的括号。
        /// </summary>
        public static int FindMatchingClosingBrace(string s, int openIdx)
        {
            if (openIdx < 0 || openIdx >= s.Length || s[openIdx] != '{')
                return -1;

            var depth = 0;
            var inString = false;
            var escape = false;

            for (var i = openIdx; i < s.Length; i++)
            {
                var c = s[i];
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (inString)
                {
                    if (c == '\\') escape = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{':
                        depth++;
                        break;
                    case '}':
                        depth--;
                        if (depth == 0)
                            return i;
                        break;
                }
            }

            return -1;
        }
    }
}
