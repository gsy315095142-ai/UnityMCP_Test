#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace UnityMCP.AI
{
    /// <summary>
    /// 从 OpenAI 兼容的 <c>/v1/chat/completions</c> 响应体中提取助手正文。
    /// Unity <see cref="JsonUtility"/> 无法处理 <c>message.content</c> 为数组（多模态）或非标准字段等情况。
    /// </summary>
    public static class OpenAiCompatibleResponseParser
    {
        /// <summary>优先顺序：首个 choice → message.content（字符串或数组内 text）→ choices 段内 text → reasoning_content。</summary>
        public static string ExtractAssistantText(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
                return "";

            var slice = SliceFirstChoiceObject(responseJson);
            if (!string.IsNullOrEmpty(slice))
            {
                var msg = LocateJsonObjectValue(slice, "message");
                if (!string.IsNullOrEmpty(msg))
                {
                    var fromMsg = ExtractContentFromAssistantMessage(msg);
                    if (!string.IsNullOrWhiteSpace(fromMsg))
                        return fromMsg;
                }
            }

            // 兜底：全文中第一个助手段落里的 "text":"..."
            var fallbackTexts = ExtractAllTextFieldsAfterChoices(responseJson);
            if (fallbackTexts.Count > 0)
                return string.Join("", fallbackTexts);

            var reasoning = FindTopLevelStringField(responseJson, "reasoning_content");
            if (!string.IsNullOrWhiteSpace(reasoning))
                return reasoning;

            return "";
        }

        public static int TryParseTotalTokens(string responseJson)
        {
            var m = Regex.Match(responseJson, @"""total_tokens""\s*:\s*(\d+)", RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
        }

        private static string? SliceFirstChoiceObject(string json)
        {
            var idx = json.IndexOf("\"choices\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var b = json.IndexOf('[', idx);
            if (b < 0) return null;
            var startObj = json.IndexOf('{', b);
            if (startObj < 0) return null;
            var end = FindMatchingClosingBrace(json, startObj);
            if (end < 0) return null;
            return json.Substring(startObj, end - startObj + 1);
        }

        private static int FindMatchingClosingBrace(string s, int openIdx)
        {
            if (openIdx < 0 || openIdx >= s.Length || s[openIdx] != '{')
                return -1;
            var depth = 0;
            for (var i = openIdx; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }

        /// <summary>在对象字面量内找到键 <paramref name="key"/> 对应的 JSON 对象 {...} 子串（不含外层键名）。</summary>
        private static string? LocateJsonObjectValue(string objectJson, string key)
        {
            var needle = $"\"{key}\"";
            var k = objectJson.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (k < 0) return null;
            var colon = objectJson.IndexOf(':', k + needle.Length);
            if (colon < 0) return null;
            var i = colon + 1;
            while (i < objectJson.Length && char.IsWhiteSpace(objectJson[i])) i++;
            if (i >= objectJson.Length || objectJson[i] != '{') return null;
            var end = FindMatchingClosingBrace(objectJson, i);
            if (end < 0) return null;
            return objectJson.Substring(i, end - i + 1);
        }

        private static string ExtractContentFromAssistantMessage(string messageJson)
        {
            var key = "\"content\"";
            var k = messageJson.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (k < 0) return "";

            var colon = messageJson.IndexOf(':', k + key.Length);
            if (colon < 0) return "";
            var i = colon + 1;
            while (i < messageJson.Length && char.IsWhiteSpace(messageJson[i])) i++;
            if (i >= messageJson.Length) return "";

            if (messageJson[i] == '"')
            {
                var s = ReadJsonStringValue(messageJson, i);
                return s ?? "";
            }

            if (messageJson[i] == '[')
                return ConcatenateTextPartsInContentArray(messageJson, i);

            if (messageJson[i] == '{')
            {
                var endBrace = FindMatchingClosingBrace(messageJson, i);
                if (endBrace > i)
                {
                    var obj = messageJson.Substring(i, endBrace - i + 1);
                    var m = Regex.Match(obj, @"""text""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.Singleline);
                    if (m.Success)
                        return UnescapeJsonString(m.Groups[1].Value);
                }
            }

            return "";
        }

        private static string ConcatenateTextPartsInContentArray(string json, int arrayStart)
        {
            var end = FindMatchingClosingBracket(json, arrayStart);
            if (end < 0) return "";
            var inner = json.Substring(arrayStart, end - arrayStart + 1);
            var parts = new List<string>();
            foreach (Match m in Regex.Matches(inner, @"""text""\s*:\s*""((?:\\.|[^""\\])*)"""))
            {
                if (m.Success)
                    parts.Add(UnescapeJsonString(m.Groups[1].Value));
            }

            return parts.Count > 0 ? string.Join("", parts) : "";
        }

        private static int FindMatchingClosingBracket(string s, int openIdx)
        {
            if (openIdx < 0 || openIdx >= s.Length || s[openIdx] != '[')
                return -1;
            var depth = 0;
            var inString = false;
            var escape = false;
            for (var i = openIdx; i < s.Length; i++)
            {
                var c = s[i];
                if (inString)
                {
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }

        private static List<string> ExtractAllTextFieldsAfterChoices(string json)
        {
            var list = new List<string>();
            var idx = json.IndexOf("\"choices\"", StringComparison.OrdinalIgnoreCase);
            var tail = idx >= 0 ? json.Substring(idx) : json;
            foreach (Match m in Regex.Matches(tail, @"""text""\s*:\s*""((?:\\.|[^""\\])*)"""))
            {
                if (m.Success)
                    list.Add(UnescapeJsonString(m.Groups[1].Value));
            }

            return list;
        }

        private static string? FindTopLevelStringField(string json, string field)
        {
            var pattern = $"\"{Regex.Escape(field)}\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
            var m = Regex.Match(json, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return m.Success ? UnescapeJsonString(m.Groups[1].Value) : null;
        }

        private static string? ReadJsonStringValue(string json, int openQuoteIdx)
        {
            if (openQuoteIdx < 0 || openQuoteIdx >= json.Length || json[openQuoteIdx] != '"')
                return null;
            var sb = new StringBuilder();
            for (var i = openQuoteIdx + 1; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '"') return sb.ToString();
                if (c == '\\' && i + 1 < json.Length)
                {
                    i++;
                    var e = json[i];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }

            return null;
        }

        private static string UnescapeJsonString(string s)
        {
            return s
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }
    }
}
