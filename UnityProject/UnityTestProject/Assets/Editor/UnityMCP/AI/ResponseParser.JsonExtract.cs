#nullable enable

using System;
using UnityMCP.Generators;

namespace UnityMCP.AI
{
    public static partial class ResponseParser
    {
        /// <summary>
        /// 从模型输出中提取 JSON 文本（场景操控 <c>unity-ops</c>、意图路由等通用逻辑）：
        /// 优先 <c>```json</c> 代码块；否则整段花括号对象；否则从文中切出包含关键字段的平衡括号对象。
        /// </summary>
        public static string? ExtractJsonFromModelOutput(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var stripped = ThinkBlockRegex.Replace(content, "").Trim();
            var block = ExtractJsonBlock(stripped);
            if (!string.IsNullOrEmpty(block))
                return block.Trim();

            var t = stripped.Trim();
            if (t.StartsWith("{", StringComparison.Ordinal) && t.EndsWith("}", StringComparison.Ordinal))
                return t;

            return TryExtractBalancedJsonContainingKey(stripped, "unityOpsVersion")
                   ?? TryExtractBalancedJsonContainingKey(stripped, "assetOpsVersion")
                   ?? TryExtractBalancedJsonContainingKey(stripped, "operations")
                   ?? TryExtractBalancedJsonContainingKey(stripped, "assetDeleteIntent")
                   ?? TryExtractBalancedJsonContainingKey(stripped, "assetPaths");
        }

        private static string? TryExtractBalancedJsonContainingKey(string content, string key)
        {
            var needle = "\"" + key + "\"";
            var idx = content.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            var start = content.LastIndexOf('{', idx);
            if (start < 0)
                return null;

            var end = PrefabJsonSanitizer.FindMatchingClosingBrace(content, start);
            if (end < 0)
                return null;

            return content.Substring(start, end - start + 1).Trim();
        }
    }
}
