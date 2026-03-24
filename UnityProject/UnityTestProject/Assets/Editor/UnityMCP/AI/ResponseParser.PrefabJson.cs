#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityMCP.Generators;

namespace UnityMCP.AI
{
    public static partial class ResponseParser
    {
        #region 预制体 JSON 解析

        /// <summary>
        /// 从 AI 响应中解析预制体描述
        /// </summary>
        /// <param name="aiContent">AI 返回的原始文本</param>
        /// <returns>预制体描述（解析失败时返回 null 和错误信息）</returns>
        public static PrefabParseResult ParsePrefabResponse(string aiContent)
        {
            if (string.IsNullOrWhiteSpace(aiContent))
            {
                return PrefabParseResult.Fail("AI 返回了空内容");
            }

            var content = StripThinkBlocks(aiContent);

            var jsonText = ExtractJsonBlock(content);
            if (string.IsNullOrEmpty(jsonText))
                jsonText = TryExtractRawJson(content);
            if (string.IsNullOrEmpty(jsonText))
                jsonText = TryExtractLargestPrefabLikeJson(content);

            if (string.IsNullOrEmpty(jsonText))
            {
                return PrefabParseResult.Fail(
                    "无法从 AI 响应中提取预制体 JSON。\n\n" + BuildPrefabExtractFailureHints(),
                    content);
            }

            try
            {
                var description = ParsePrefabJson(jsonText);
                if (description == null)
                {
                    return PrefabParseResult.Fail(
                        "JSON 结构化失败（解析结果为空）。\n\n" + BuildPrefabParseHints(jsonText),
                        jsonText);
                }

                if (string.IsNullOrWhiteSpace(description.prefabName))
                {
                    description.prefabName = description.rootObject?.name ?? "NewPrefab";
                }

                if (string.IsNullOrWhiteSpace(description.prefabName))
                    description.prefabName = "NewPrefab";

                return PrefabParseResult.Ok(description, jsonText);
            }
            catch (Exception ex)
            {
                return PrefabParseResult.Fail(
                    $"JSON 解析异常: {ex.Message}\n\n{BuildPrefabParseHints(jsonText)}",
                    jsonText);
            }
        }

        /// <summary>
        /// 尝试直接提取 JSON 内容（AI 未使用 Markdown 代码块时）
        /// </summary>
        private static string? TryExtractRawJson(string content)
        {
            var trimmed = content.Trim();
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                return trimmed;
            return null;
        }

        /// <summary>
        /// 从正文中找出最像预制体定义的 {...} 片段（成对花括号、含 prefabName/rootObject/components 之一）。
        /// </summary>
        private static string? TryExtractLargestPrefabLikeJson(string content)
        {
            string? best = null;
            var bestLen = -1;
            for (var i = 0; i < content.Length; i++)
            {
                if (content[i] != '{') continue;
                var end = PrefabJsonSanitizer.FindMatchingClosingBrace(content, i);
                if (end < 0 || end - i < 10) continue;
                var slice = content.Substring(i, end - i + 1);
                if (!SliceLooksLikePrefabJson(slice)) continue;
                var len = slice.Length;
                if (len > bestLen)
                {
                    bestLen = len;
                    best = slice;
                }
            }

            return best;
        }

        private static bool SliceLooksLikePrefabJson(string slice)
        {
            return slice.IndexOf("prefabName", StringComparison.OrdinalIgnoreCase) >= 0
                   || slice.IndexOf("rootObject", StringComparison.OrdinalIgnoreCase) >= 0
                   || slice.Contains("\"components\"", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildPrefabExtractFailureHints() =>
            "建议：\n" +
            "· 请模型只输出一个 ```json 代码块，根字段包含 prefabName 与 rootObject；\n" +
            "· rootObject 内使用 name、components、children；每个组件含 type 与 properties 对象；\n" +
            "· 若仍失败，可改用「生成预制体」模式并缩短需求描述，减少模型发挥空间。";

        private static string BuildPrefabParseHints(string? rawSnippet)
        {
            var sb = new StringBuilder();
            sb.AppendLine("可执行的检查项：");
            sb.AppendLine("· 根级字段：prefabName（字符串）、rootObject（对象）；");
            sb.AppendLine("· 组件：{\"type\":\"Rigidbody\",\"properties\":{\"mass\":\"1\"}}，属性值尽量用字符串；");
            sb.AppendLine("· 不要有尾逗号、不要用单引号包裹键名；");
            sb.AppendLine("· 中文项目下若类名脚本将挂在预制体上，type 填脚本类名。");
            if (!string.IsNullOrEmpty(rawSnippet) && rawSnippet.Length > 600)
                sb.AppendLine("· 原始 JSON 较长，请用「预览 JSON」查看截取前的完整内容。");
            return sb.ToString().TrimEnd();
        }
        /// <summary>
        /// 手动解析预制体 JSON（使用 Unity 的 JsonUtility 结合手动处理）。
        /// JsonUtility 不支持 Dictionary，因此组件属性需要特殊处理。
        /// </summary>
        private static PrefabDescription? ParsePrefabJson(string jsonText)
        {
            var sanitized = PrefabJsonSanitizer.Sanitize(jsonText);

            PrefabDescriptionRaw wrapper;
            try
            {
                wrapper = JsonUtility.FromJson<PrefabDescriptionRaw>(sanitized);
            }
            catch
            {
                var fallback = TryPrefabFromFlatGameObjectOnly(sanitized);
                return fallback;
            }

            var result = new PrefabDescription
            {
                prefabName = wrapper.prefabName ?? ""
            };

            if (wrapper.rootObject != null)
            {
                result.rootObject = ConvertGameObjectDesc(wrapper.rootObject);
            }
            else
            {
                var flatCandidate = JsonUtility.FromJson<GameObjectDescriptionRaw>(sanitized);
                if (LooksLikeGameObjectNode(flatCandidate))
                {
                    result.rootObject = ConvertGameObjectDesc(flatCandidate);
                    if (string.IsNullOrEmpty(result.prefabName))
                        result.prefabName = flatCandidate.name ?? "GeneratedPrefab";
                }
                else
                {
                    result.rootObject = new GameObjectDescription
                    {
                        name = string.IsNullOrWhiteSpace(result.prefabName) ? "Root" : result.prefabName
                    };
                }
            }

            return result;
        }

        private static bool LooksLikeGameObjectNode(GameObjectDescriptionRaw? g) =>
            g != null && (
                !string.IsNullOrEmpty(g.name)
                || (g.components != null && g.components.Count > 0)
                || (g.children != null && g.children.Count > 0));

        private static PrefabDescription? TryPrefabFromFlatGameObjectOnly(string sanitized)
        {
            var flat = JsonUtility.FromJson<GameObjectDescriptionRaw>(sanitized);
            if (!LooksLikeGameObjectNode(flat))
                return null;

            return new PrefabDescription
            {
                prefabName = flat.name ?? "GeneratedPrefab",
                rootObject = ConvertGameObjectDesc(flat)
            };
        }

        private static GameObjectDescription ConvertGameObjectDesc(GameObjectDescriptionRaw raw)
        {
            var desc = new GameObjectDescription
            {
                name = raw.name ?? "GameObject",
                tag = raw.tag ?? "Untagged",
                layer = raw.layer,
                active = raw.active,
                position = raw.position ?? new[] { 0f, 0f, 0f },
                rotation = raw.rotation ?? new[] { 0f, 0f, 0f },
                scale = raw.scale ?? new[] { 1f, 1f, 1f },
                primitive = raw.primitive ?? ""
            };

            if (raw.components != null)
            {
                foreach (var compRaw in raw.components)
                {
                    var compDesc = new ComponentDescription
                    {
                        type = compRaw.type ?? "",
                        properties = ParseProperties(compRaw.propertiesJson)
                    };
                    desc.components.Add(compDesc);
                }
            }

            if (raw.children != null)
            {
                foreach (var childRaw in raw.children)
                {
                    desc.children.Add(ConvertGameObjectDesc(childRaw));
                }
            }

            return desc;
        }

        /// <summary>
        /// 从 JSON 属性对象中解析键值对。
        /// 由于 JsonUtility 不支持 Dictionary，采用简单的字符串解析。
        /// </summary>
        private static Dictionary<string, object> ParseProperties(string? propertiesJson)
        {
            var result = new Dictionary<string, object>();
            if (string.IsNullOrWhiteSpace(propertiesJson))
                return result;

            var trimmed = propertiesJson!.Trim().Trim('{', '}').Trim();
            if (string.IsNullOrEmpty(trimmed))
                return result;

            var pairs = SplitJsonPairs(trimmed);
            foreach (var pair in pairs)
            {
                var colonIdx = pair.IndexOf(':');
                if (colonIdx <= 0) continue;

                var key = pair.Substring(0, colonIdx).Trim().Trim('"');
                var val = pair.Substring(colonIdx + 1).Trim().Trim('"');

                if (!string.IsNullOrEmpty(key))
                    result[key] = val;
            }

            return result;
        }

        /// <summary>
        /// 安全地分割 JSON 键值对（考虑嵌套的花括号和数组）
        /// </summary>
        private static List<string> SplitJsonPairs(string text)
        {
            var pairs = new List<string>();
            var depth = 0;
            var start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                else if (c == ',' && depth == 0)
                {
                    pairs.Add(text.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }

            if (start < text.Length)
                pairs.Add(text.Substring(start).Trim());

            return pairs;
        }

        #endregion
        #region 预制体 JSON 辅助数据结构（JsonUtility 兼容）

        [Serializable]
        private class PrefabDescriptionRaw
        {
            public string? prefabName;
            public GameObjectDescriptionRaw? rootObject;
        }

        [Serializable]
        private class GameObjectDescriptionRaw
        {
            public string? name;
            public string? tag;
            public int layer;
            public bool active = true;
            public float[]? position;
            public float[]? rotation;
            public float[]? scale;
            /// <summary>Cube / Sphere / Capsule / Cylinder / Plane / Quad 等，与 PrimitiveType 一致</summary>
            public string? primitive;
            public List<ComponentDescriptionRaw>? components;
            public List<GameObjectDescriptionRaw>? children;
        }

        [Serializable]
        private class ComponentDescriptionRaw
        {
            public string? type;
            public string? propertiesJson;
        }

        #endregion
    }
}
