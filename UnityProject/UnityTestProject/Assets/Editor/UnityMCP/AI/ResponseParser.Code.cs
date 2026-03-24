#nullable enable

using System.Text.RegularExpressions;

namespace UnityMCP.AI
{
    public static partial class ResponseParser
    {
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
                code = TryExtractPlainFenceAsCSharp(content);
            if (string.IsNullOrEmpty(code))
                code = TryExtractRawCode(content);

            if (string.IsNullOrEmpty(code))
            {
                return new CodeGenerationResult
                {
                    Success = false,
                    Error = "无法从 AI 响应中提取 C# 代码。\n\n" + BuildCodeParseHints(),
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
                    Error = "无法从代码中提取类名。请确保包含有效的 `public class 类名` 定义。\n\n" + BuildCodeParseHints(),
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

        private static string? TryExtractPlainFenceAsCSharp(string content)
        {
            var matches = PlainCodeFenceRegex.Matches(content);
            string? best = null;
            var bestScore = 0;
            foreach (Match match in matches)
            {
                var body = match.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(body) || body.StartsWith("{")) continue;
                var score = 0;
                if (body.Contains("using ")) score += 2;
                if (body.Contains("class ")) score += 3;
                if (body.Contains("namespace ")) score += 1;
                if (score >= 5 && body.Length > bestScore)
                {
                    bestScore = body.Length;
                    best = body;
                }
            }

            return best;
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
        private static string BuildCodeParseHints() =>
            "建议：\n" +
            "· 使用 ```csharp 包裹完整的一个 .cs 文件；\n" +
            "· 包含 public class 类名 与命名空间（如需）；\n" +
            "· 本地模型可多试一次，或在设置中略降 Temperature。";
    }
}
