#nullable enable

using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityMCP.AI
{
    /// <summary>
    /// 代码生成结果
    /// </summary>
    public class CodeGenerationResult
    {
        /// <summary>是否成功提取代码</summary>
        public bool Success { get; set; }

        /// <summary>提取的脚本名称（类名）</summary>
        public string ScriptName { get; set; } = "";

        /// <summary>提取的命名空间</summary>
        public string? Namespace { get; set; }

        /// <summary>完整的 C# 代码</summary>
        public string Code { get; set; } = "";

        /// <summary>错误信息（失败时）</summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// AI 响应解析器。
    /// 从 AI 返回的文本中提取代码块、类名等关键信息。
    /// </summary>
    public static class ResponseParser
    {
        private static readonly Regex CodeBlockRegex = new(
            @"```(?:csharp|cs)\s*\n([\s\S]*?)```",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex ClassNameRegex = new(
            @"(?:public|internal)\s+(?:partial\s+)?class\s+(\w+)",
            RegexOptions.Compiled);

        private static readonly Regex NamespaceRegex = new(
            @"namespace\s+([\w.]+)",
            RegexOptions.Compiled);

        private static readonly Regex ThinkBlockRegex = new(
            @"<think>[\s\S]*?</think>",
            RegexOptions.Compiled);

        /// <summary>
        /// 从 AI 响应中解析代码生成结果
        /// </summary>
        /// <param name="aiContent">AI 返回的原始文本</param>
        /// <returns>代码生成结果</returns>
        public static CodeGenerationResult ParseCodeResponse(string aiContent)
        {
            if (string.IsNullOrWhiteSpace(aiContent))
            {
                return new CodeGenerationResult
                {
                    Success = false,
                    Error = "AI 返回了空内容"
                };
            }

            var content = StripThinkBlocks(aiContent);

            var code = ExtractCodeBlock(content);
            if (string.IsNullOrEmpty(code))
            {
                code = TryExtractRawCode(content);
            }

            if (string.IsNullOrEmpty(code))
            {
                return new CodeGenerationResult
                {
                    Success = false,
                    Error = "无法从 AI 响应中提取代码块。请确保 AI 返回了 ```csharp 格式的代码。",
                    Code = content
                };
            }

            var scriptName = ExtractClassName(code);
            var namespaceName = ExtractNamespace(code);

            if (string.IsNullOrEmpty(scriptName))
            {
                return new CodeGenerationResult
                {
                    Success = false,
                    Error = "无法从代码中提取类名，请确保代码包含有效的 class 定义。",
                    Code = code
                };
            }

            return new CodeGenerationResult
            {
                Success = true,
                ScriptName = scriptName,
                Namespace = namespaceName,
                Code = code
            };
        }

        /// <summary>
        /// 移除 AI 思考过程（&lt;think&gt;...&lt;/think&gt; 块）
        /// </summary>
        private static string StripThinkBlocks(string content)
        {
            return ThinkBlockRegex.Replace(content, "").Trim();
        }

        /// <summary>
        /// 从 Markdown 代码块中提取代码
        /// </summary>
        private static string? ExtractCodeBlock(string content)
        {
            var matches = CodeBlockRegex.Matches(content);
            if (matches.Count == 0)
                return null;

            string? bestMatch = null;
            var maxLength = 0;

            foreach (Match match in matches)
            {
                var code = match.Groups[1].Value.Trim();
                if (code.Length > maxLength)
                {
                    maxLength = code.Length;
                    bestMatch = code;
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// 当没有代码块标记时，尝试直接提取代码内容
        /// （用于 AI 直接输出代码而未使用 Markdown 格式的情况）
        /// </summary>
        private static string? TryExtractRawCode(string content)
        {
            if (content.Contains("using ") && content.Contains("class ") && content.Contains("{"))
            {
                return content.Trim();
            }

            return null;
        }

        /// <summary>
        /// 从代码中提取类名
        /// </summary>
        private static string? ExtractClassName(string code)
        {
            var match = ClassNameRegex.Match(code);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// 从代码中提取命名空间
        /// </summary>
        private static string? ExtractNamespace(string code)
        {
            var match = NamespaceRegex.Match(code);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
