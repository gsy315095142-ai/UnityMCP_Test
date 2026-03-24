#nullable enable

using System;
using UnityEngine;
using UnityMCP.AI;

namespace UnityMCP.Tools
{
    /// <summary>
    /// 从模型输出解析 <see cref="AssetOpsEnvelopeDto"/>。
    /// </summary>
    public static class AssetOpsParser
    {
        public const int SupportedVersion = 1;

        public static AssetOpsParseResult Parse(string textOrAiOutput)
        {
            if (string.IsNullOrWhiteSpace(textOrAiOutput))
                return AssetOpsParseResult.Fail("输入为空", "");

            var json = ResponseParser.ExtractJsonFromModelOutput(textOrAiOutput);
            if (string.IsNullOrWhiteSpace(json))
                return AssetOpsParseResult.Fail(
                    "无法提取 JSON。请使用 ```json 代码块或输出含 assetOpsVersion / operations 的对象。",
                    textOrAiOutput);

            AssetOpsEnvelopeDto? dto;
            try
            {
                dto = JsonUtility.FromJson<AssetOpsEnvelopeDto>(json);
            }
            catch (Exception ex)
            {
                return AssetOpsParseResult.Fail($"JSON 反序列化失败: {ex.Message}", json);
            }

            if (dto == null)
                return AssetOpsParseResult.Fail("JSON 反序列化结果为 null", json);
            if (dto.assetOpsVersion != SupportedVersion)
                return AssetOpsParseResult.Fail(
                    $"assetOpsVersion 必须为 {SupportedVersion}，当前为 {dto.assetOpsVersion}",
                    json);
            if (dto.operations == null || dto.operations.Length == 0)
                return AssetOpsParseResult.Fail("operations 不能为空", json);

            return AssetOpsParseResult.Ok(dto, json);
        }
    }
}
