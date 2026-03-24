#nullable enable

using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityMCP.AI
{
    /// <summary>
    /// AI 响应解析器（核心：正则与通用 JSON 围栏提取）。
    /// </summary>
    public static partial class ResponseParser
    {
        private static readonly Regex CodeBlockRegex = new(
            @"```(?:csharp|cs)\s*\n?([\s\S]*?)```",
            RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>
        /// 无语言标记的围栏，仅在内容像 C# 时使用，避免误吞 JSON。
        /// </summary>
        private static readonly Regex PlainCodeFenceRegex = new(
            @"```\s*\n?([\s\S]*?)```",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex ClassNameRegex = new(
            @"(?:public|internal)\s+(?:partial\s+)?class\s+(\w+)",
            RegexOptions.Compiled);

        private static readonly Regex NamespaceRegex = new(
            @"namespace\s+([\w.]+)",
            RegexOptions.Compiled);

        private static readonly Regex JsonBlockRegex = new(
            @"```(?:json)?\s*\n?([\s\S]*?)```",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex ThinkBlockRegex = new(
            @"<think>[\s\S]*?</think>",
            RegexOptions.Compiled);

        /// <summary>
        /// 移除 AI 思考过程（&lt;think&gt;...&lt;/think&gt; 块）
        /// </summary>
        public static string StripThinkBlocks(string content)
        {
            return ThinkBlockRegex.Replace(content, "").Trim();
        }

        /// <summary>
        /// 从 Markdown 中提取 JSON 代码块
        /// </summary>
        private static string? ExtractJsonBlock(string content)
        {
            var matches = JsonBlockRegex.Matches(content);
            if (matches.Count == 0)
                return null;

            string? bestMatch = null;
            var maxLength = 0;

            foreach (Match match in matches)
            {
                var json = match.Groups[1].Value.Trim();
                if (json.Length > maxLength)
                {
                    maxLength = json.Length;
                    bestMatch = json;
                }
            }

            return bestMatch;
        }
    }
}
