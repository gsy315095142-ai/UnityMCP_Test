#nullable enable

using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityMCP.AI
{
    public static partial class ResponseParser
    {
        [Serializable]
        private class GenerationIntentJson
        {
            public string? generationTarget;
            public string? codeType;
            /// <summary>联合生成时步骤顺序：prefabFirst / codeFirst（可省略，默认 codeFirst）。</summary>
            public string? combinedOrder;
            /// <summary>图片生成：英文图片描述（发给图片 AI）。</summary>
            public string? imagePrompt;
            /// <summary>图片生成：建议保存文件名（不含扩展名）。</summary>
            public string? saveFileName;
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
                    $"无法识别的 generationTarget: “{parsed.generationTarget ?? "(空)"}”，应为 code / prefab / both / sceneOps / projectQuery / assetDelete / assetOps。",
                    jsonText);
            }

            var codeType = MapCodeTypeHint(parsed.codeType);
            var prefabFirst = route == GenerationRoute.Both && MapCombinedOrderIsPrefabFirst(parsed.combinedOrder);
            return GenerationIntentResult.Ok(route.Value, codeType, jsonText, prefabFirst,
                imagePrompt: parsed.imagePrompt,
                saveFileName: parsed.saveFileName);
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
                "assetdelete" or "deleteprefabs" or "deleteassets" or "removeprefabs" or "removeassets" =>
                    GenerationRoute.AssetDelete,
                "assetops" or "assetoperations" or "projectassets" or "organizeassets" => GenerationRoute.AssetOps,
                // 常见中文返回值（本地模型）
                "代码" or "脚本" or "csharp脚本" => GenerationRoute.Code,
                "预制体" or "预设" => GenerationRoute.Prefab,
                "联合" or "两者" or "都要" or "代码和预制体" or "脚本和预制体" => GenerationRoute.Both,
                "场景操控" or "场景操作" or "编辑场景" or "hierarchy操作" => GenerationRoute.SceneOps,
                "项目查询" or "项目盘点" or "检查项目" or "只读查询" => GenerationRoute.ProjectQuery,
                "删除预制体" or "删除资源" or "移除预制体" or "删掉预制体" or "删除脚本" or "删掉脚本" or "移除脚本" =>
                    GenerationRoute.AssetDelete,
                "整理资源" or "移动资源" or "复制资源" or "重命名资源" or "新建文件夹" => GenerationRoute.AssetOps,
                "generatetexture" or "generateimage" or "imagegen" or "texturegen" => GenerationRoute.TextureGenerate,
                "生成贴图" or "生成图片" or "生成纹理" or "生成图标" or "生成图像" => GenerationRoute.TextureGenerate,
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
            s = Regex.Replace(s, @"(?i)""imageprompt""\s*:", "\"imagePrompt\":");
            s = Regex.Replace(s, @"(?i)""savefilename""\s*:", "\"saveFileName\":");
            for (var i = 0; i < 6; i++)
            {
                var n = Regex.Replace(s, @",(\s*[\]}])", "$1");
                if (n == s) break;
                s = n;
            }

            return s;
        }
    }
}
