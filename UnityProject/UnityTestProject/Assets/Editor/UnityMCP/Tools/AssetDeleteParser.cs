#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityMCP.AI;

namespace UnityMCP.Tools
{
    /// <summary>
    /// AI 输出的「待删除资源路径」JSON（Assets 下任意资源，须用户确认后执行）。
    /// </summary>
    [Serializable]
    public sealed class AssetDeleteEnvelopeDto
    {
        public string[]? assetPaths;
        public string? note;
    }

    /// <summary>
    /// 解析 <see cref="AssetDeleteEnvelopeDto"/> 的结果。
    /// </summary>
    public sealed class AssetDeleteParseResult
    {
        public bool Success { get; }
        public string? Error { get; }
        public AssetDeleteEnvelopeDto? Envelope { get; }
        public IReadOnlyList<string> NormalizedPaths { get; }
        public string RawJson { get; }

        private AssetDeleteParseResult(
            bool success,
            string? error,
            AssetDeleteEnvelopeDto? envelope,
            IReadOnlyList<string> paths,
            string rawJson)
        {
            Success = success;
            Error = error;
            Envelope = envelope;
            NormalizedPaths = paths;
            RawJson = rawJson;
        }

        public static AssetDeleteParseResult Ok(AssetDeleteEnvelopeDto env, IReadOnlyList<string> paths, string rawJson) =>
            new(true, null, env, paths, rawJson);

        public static AssetDeleteParseResult Fail(string error, string rawJson) =>
            new(false, error, null, Array.Empty<string>(), rawJson);
    }

    /// <summary>
    /// 从模型输出解析待删除的资源路径列表（不限于 .prefab）。
    /// </summary>
    public static class AssetDeleteParser
    {
        private static readonly Regex TrailingCommaRegex = new(@",(\s*[\]}])", RegexOptions.Compiled);

        public static AssetDeleteParseResult Parse(string textOrAiOutput)
        {
            if (string.IsNullOrWhiteSpace(textOrAiOutput))
                return AssetDeleteParseResult.Fail("输入为空", "");

            var json = ResponseParser.ExtractJsonFromModelOutput(textOrAiOutput);
            if (string.IsNullOrWhiteSpace(json))
                return AssetDeleteParseResult.Fail(
                    "无法提取 JSON。请使用 ```json 代码块，或输出含 assetPaths 数组的对象。",
                    textOrAiOutput);

            json = RemoveTrailingCommas(json.Trim());

            AssetDeleteEnvelopeDto? dto;
            try
            {
                dto = JsonUtility.FromJson<AssetDeleteEnvelopeDto>(json);
            }
            catch (Exception ex)
            {
                return AssetDeleteParseResult.Fail($"JSON 反序列化失败: {ex.Message}", json);
            }

            if (dto == null)
                return AssetDeleteParseResult.Fail("JSON 反序列化结果为 null", json);

            if (dto.assetPaths == null || dto.assetPaths.Length == 0)
                return AssetDeleteParseResult.Fail("assetPaths 不能为空", json);

            var normalized = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in dto.assetPaths)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                if (!AssetPathSecurity.TryValidateGenericAssetPath(raw, out var path, out _))
                    continue;
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                    continue;
                if (seen.Add(path))
                    normalized.Add(path);
            }

            if (normalized.Count == 0)
                return AssetDeleteParseResult.Fail(
                    "assetPaths 中无有效的 Assets 资源路径（已过滤非法或不存在项）。",
                    json);

            return AssetDeleteParseResult.Ok(dto, normalized, json);
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
