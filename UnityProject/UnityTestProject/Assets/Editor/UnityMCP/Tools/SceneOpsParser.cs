#nullable enable

using System.Text.RegularExpressions;
using UnityEngine;
using UnityMCP.AI;

namespace UnityMCP.Tools
{
    /// <summary>
    /// 从模型输出或纯 JSON 文本解析 <see cref="SceneOpsEnvelopeDto"/>（A.2）。
    /// </summary>
    public static class SceneOpsParser
    {
        /// <summary>与 JSON 中 unityOpsVersion 一致</summary>
        public const int SupportedVersion = 1;

        private static readonly Regex TrailingCommaRegex = new(@",(\s*[\]}])", RegexOptions.Compiled);

        public static SceneOpsParseResult Parse(string textOrAiOutput)
        {
            if (string.IsNullOrWhiteSpace(textOrAiOutput))
                return SceneOpsParseResult.Fail("输入为空", "");

            var json = ResponseParser.ExtractJsonFromModelOutput(textOrAiOutput);
            if (string.IsNullOrWhiteSpace(json))
                return SceneOpsParseResult.Fail(
                    "无法提取 JSON。请使用 ```json 代码块或输出含 unityOpsVersion / operations 的对象。",
                    textOrAiOutput);

            json = RemoveTrailingCommas(json);

            SceneOpsEnvelopeDto? dto;
            try
            {
                dto = JsonUtility.FromJson<SceneOpsEnvelopeDto>(json);
            }
            catch (System.Exception ex)
            {
                return SceneOpsParseResult.Fail($"JSON 反序列化失败: {ex.Message}", json);
            }

            if (dto == null)
                return SceneOpsParseResult.Fail("JSON 反序列化结果为 null", json);

            if (dto.unityOpsVersion != SupportedVersion)
            {
                return SceneOpsParseResult.Fail(
                    $"unityOpsVersion 必须为 {SupportedVersion}，当前为 {dto.unityOpsVersion}",
                    json);
            }

            if (dto.operations == null || dto.operations.Length == 0)
                return SceneOpsParseResult.Fail("operations 不能为空", json);

            return SceneOpsParseResult.Ok(dto, json);
        }

        private static string RemoveTrailingCommas(string json)
        {
            var s = json;
            for (var i = 0; i < 8; i++)
            {
                var n = TrailingCommaRegex.Replace(s, "$1");
                if (n == s) break;
                s = n;
            }

            return s;
        }
    }
}
