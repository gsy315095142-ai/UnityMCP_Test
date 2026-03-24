#nullable enable

using UnityMCP.Generators;

namespace UnityMCP.AI
{
    /// <summary>
    /// 解析「AI 判断」路由阶段返回的 JSON 后的结果。
    /// </summary>
    public sealed class GenerationIntentResult
    {
        public bool Success { get; set; }
        public GenerationRoute Route { get; set; }
        public CodeType CodeType { get; set; }
        /// <summary>联合生成（both）时是否为先预制体再脚本。</summary>
        public bool CombinedPrefabFirst { get; set; }
        public string? Error { get; set; }
        public string? RawJson { get; set; }

        public static GenerationIntentResult Ok(
            GenerationRoute route,
            CodeType codeType,
            string? rawJson,
            bool combinedPrefabFirst = false) => new()
        {
            Success = true,
            Route = route,
            CodeType = codeType,
            CombinedPrefabFirst = combinedPrefabFirst,
            RawJson = rawJson ?? ""
        };

        public static GenerationIntentResult Fail(string error, string? rawJson = null) => new()
        {
            Success = false,
            Error = error,
            RawJson = rawJson ?? ""
        };
    }

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
    /// 预制体 JSON 解析结果
    /// </summary>
    public class PrefabParseResult
    {
        public bool Success { get; set; }
        public PrefabDescription? Description { get; set; }
        public string RawJson { get; set; } = "";
        public string? Error { get; set; }

        public static PrefabParseResult Ok(PrefabDescription desc, string rawJson) => new()
        {
            Success = true,
            Description = desc,
            RawJson = rawJson
        };

        public static PrefabParseResult Fail(string error, string? rawJson = null) => new()
        {
            Success = false,
            Error = error,
            RawJson = rawJson ?? ""
        };
    }
}
