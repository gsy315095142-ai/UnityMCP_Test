#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
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
    /// AI 响应解析器。
    /// 从 AI 返回的文本中提取代码块、类名等关键信息。
    /// </summary>
    public static class ResponseParser
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

        [Serializable]
        private class GenerationIntentJson
        {
            public string? generationTarget;
            public string? codeType;
            /// <summary>联合生成时步骤顺序：prefabFirst / codeFirst（可省略，默认 codeFirst）。</summary>
            public string? combinedOrder;
        }

        /// <summary>
        /// 从 AI 返回文本中解析生成意图（代码 / 预制体 / 联合 / 场景操控）。
        /// </summary>
        public static GenerationIntentResult ParseGenerationIntent(string aiContent)
        {
            if (string.IsNullOrWhiteSpace(aiContent))
                return GenerationIntentResult.Fail("AI 返回了空内容");

            var content = StripThinkBlocks(aiContent);

            var jsonText = ExtractJsonBlock(content);
            if (string.IsNullOrEmpty(jsonText))
                jsonText = TryExtractIntentJson(content);

            if (string.IsNullOrEmpty(jsonText))
            {
                return GenerationIntentResult.Fail(
                    "无法提取意图 JSON。请确保 AI 返回包含 generationTarget 的 ```json 代码块。",
                    content);
            }

            jsonText = NormalizeGenerationIntentJsonKeys(jsonText);

            GenerationIntentJson? parsed;
            try
            {
                parsed = JsonUtility.FromJson<GenerationIntentJson>(jsonText);
            }
            catch (Exception ex)
            {
                return GenerationIntentResult.Fail($"意图 JSON 解析失败: {ex.Message}", jsonText);
            }

            if (parsed == null)
                return GenerationIntentResult.Fail("意图 JSON 解析结果为空", jsonText);

            var route = MapGenerationTarget(parsed.generationTarget);
            if (route == null)
            {
                return GenerationIntentResult.Fail(
                    $"无法识别的 generationTarget: “{parsed.generationTarget ?? "(空)"}”，应为 code / prefab / both / sceneOps / projectQuery。",
                    jsonText);
            }

            var codeType = MapCodeTypeHint(parsed.codeType);
            var prefabFirst = route == GenerationRoute.Both && MapCombinedOrderIsPrefabFirst(parsed.combinedOrder);
            return GenerationIntentResult.Ok(route.Value, codeType, jsonText, prefabFirst);
        }

        private static bool MapCombinedOrderIsPrefabFirst(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            var s = raw.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
            if (s is "prefabfirst" or "prefab_first")
                return true;
            if (s.Contains("先预制", StringComparison.Ordinal) || s.Contains("先prefab", StringComparison.Ordinal))
                return true;
            return s.Contains("预制体先", StringComparison.Ordinal) || s.Contains("ui先", StringComparison.Ordinal) ||
                   s.Contains("先ui", StringComparison.Ordinal);
        }

        private static GenerationRoute? MapGenerationTarget(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
            return s switch
            {
                "code" or "script" or "csharp" or "cs" => GenerationRoute.Code,
                "prefab" or "prefabrication" or "gameobject" => GenerationRoute.Prefab,
                "both" or "combined" or "all" or "codeandprefab" or "prefabandcode" => GenerationRoute.Both,
                "sceneops" or "sceneop" or "hierarchyedit" or "unitysceneops" => GenerationRoute.SceneOps,
                "projectquery" or "projectinfo" or "query" or "info" or "answer" or "readonly" or "inventory" =>
                    GenerationRoute.ProjectQuery,
                // 常见中文返回值（本地模型）
                "代码" or "脚本" or "csharp脚本" => GenerationRoute.Code,
                "预制体" or "预设" => GenerationRoute.Prefab,
                "联合" or "两者" or "都要" or "代码和预制体" or "脚本和预制体" => GenerationRoute.Both,
                "场景操控" or "场景操作" or "编辑场景" or "hierarchy操作" => GenerationRoute.SceneOps,
                "项目查询" or "项目盘点" or "检查项目" or "只读查询" => GenerationRoute.ProjectQuery,
                _ => null
            };
        }

        private static CodeType MapCodeTypeHint(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return CodeType.Auto;
            var s = raw.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
            return s switch
            {
                "monobehaviour" or "monobehavior" or "component" or "behaviour" or "behavior" => CodeType.MonoBehaviour,
                "scriptableobject" or "scriptable" => CodeType.ScriptableObject,
                "manager" or "singleton" => CodeType.ManagerSingleton,
                _ => CodeType.Auto
            };
        }

        /// <summary>
        /// 从正文中提取包含 generationTarget 的 JSON 对象（未使用代码块时）。
        /// </summary>
        private static string? TryExtractIntentJson(string content)
        {
            var idx = content.IndexOf("\"generationTarget\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = content.IndexOf("'generationTarget'", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var start = content.LastIndexOf('{', idx);
            if (start < 0) return null;

            var depth = 0;
            for (var i = start; i < content.Length; i++)
            {
                if (content[i] == '{') depth++;
                else if (content[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return content.Substring(start, i - start + 1).Trim();
                }
            }

            return null;
        }

        private static string NormalizeGenerationIntentJsonKeys(string json)
        {
            var s = json.Trim();
            if (s.Length > 0 && s[0] == '\uFEFF')
                s = s.Substring(1);
            s = Regex.Replace(s, @"(?i)""generationtarget""\s*:", "\"generationTarget\":");
            s = Regex.Replace(s, @"(?i)""codetype""\s*:", "\"codeType\":");
            s = Regex.Replace(s, @"(?i)""combinedorder""\s*:", "\"combinedOrder\":");
            for (var i = 0; i < 6; i++)
            {
                var n = Regex.Replace(s, @",(\s*[\]}])", "$1");
                if (n == s) break;
                s = n;
            }

            return s;
        }

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
        /// 移除 AI 思考过程（&lt;think&gt;...&lt;/think&gt; 块）
        /// </summary>
        public static string StripThinkBlocks(string content)
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
                   ?? TryExtractBalancedJsonContainingKey(stripped, "operations");
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

        private static string BuildCodeParseHints() =>
            "建议：\n" +
            "· 使用 ```csharp 包裹完整的一个 .cs 文件；\n" +
            "· 包含 public class 类名 与命名空间（如需）；\n" +
            "· 本地模型可多试一次，或在设置中略降 Temperature。";

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
