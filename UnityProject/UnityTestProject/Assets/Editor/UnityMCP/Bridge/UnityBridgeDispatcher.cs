#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityMCP.AI;
using UnityMCP.Core;
using UnityMCP.Generators;
using UnityMCP.Tools;

namespace UnityMCP.Bridge
{
    /// <summary>
    /// Bridge tool dispatcher. Executes supported Unity tools on main thread.
    /// </summary>
    internal static class UnityBridgeDispatcher
    {
        internal sealed class ToolDescriptor
        {
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public bool Available { get; set; }
            public string Note { get; set; } = "";
        }

        internal sealed class DispatchResult
        {
            public bool Success { get; set; }
            public string ErrorCode { get; set; } = "";
            public string Message { get; set; } = "";
            public string DataJson { get; set; } = "{}";

            public static DispatchResult Ok(string dataJson) => new()
            {
                Success = true,
                DataJson = string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson
            };

            public static DispatchResult Fail(string code, string message) => new()
            {
                Success = false,
                ErrorCode = code,
                Message = message
            };
        }

        private static readonly Dictionary<string, string> ToolAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["create-gameobject"] = McpToolNames.ExecuteSceneOps,
            ["set-parent"] = McpToolNames.ExecuteSceneOps,
            ["add-component"] = McpToolNames.ExecuteSceneOps,
            ["set-transform"] = McpToolNames.ExecuteSceneOps,
            ["instantiate-prefab"] = McpToolNames.ExecuteSceneOps,
            ["save-scene"] = McpToolNames.ExecuteSceneOps,
            ["get-scene-hierarchy"] = McpToolNames.GetSceneState,
            ["get-scene-info"] = McpToolNames.GetSceneState
        };

        private static readonly HashSet<string> SupportedTools = new(StringComparer.OrdinalIgnoreCase)
        {
            McpToolNames.GetSceneState,
            McpToolNames.GetProjectInfo,
            McpToolNames.ExecuteSceneOps,
            McpToolNames.GenerateCode,
            McpToolNames.CreatePrefab,
            McpToolNames.DeleteAssets,
            McpToolNames.OrganizeAssets
        };

        internal static List<ToolDescriptor> GetToolDescriptors()
        {
            var tools = new List<ToolDescriptor>
            {
                Descriptor(McpToolNames.GetSceneState, "获取场景状态/层级快照", true, ""),
                Descriptor(McpToolNames.GetProjectInfo, "获取工程信息摘要", true, ""),
                Descriptor(McpToolNames.ExecuteSceneOps, "执行批量场景操作", true, ""),
                Descriptor(McpToolNames.GenerateCode, "生成并保存 C# 脚本", true, ""),
                Descriptor(McpToolNames.CreatePrefab, "生成并保存预制体", true, ""),
                Descriptor(McpToolNames.DeleteAssets, "删除 Assets 下资源", true, ""),
                Descriptor(McpToolNames.OrganizeAssets, "移动/复制/重命名/建目录", true, ""),
                Descriptor(McpToolNames.Reply, "会话收尾回复", false, "Bridge 场景无需该工具")
            };
            return tools;
        }

        internal static DispatchResult Dispatch(string toolNameRaw, string argsJson)
        {
            var canonicalName = CanonicalizeToolName(toolNameRaw);
            if (string.IsNullOrWhiteSpace(canonicalName))
                return DispatchResult.Fail("BAD_REQUEST", "tool 不能为空");

            if (!SupportedTools.Contains(canonicalName))
                return DispatchResult.Fail("TOOL_NOT_AVAILABLE", $"当前 Bridge 不支持工具：{canonicalName}");

            try
            {
                // AI 网络请求应在后台线程执行，避免卡住编辑器主线程。
                if (canonicalName == McpToolNames.GenerateCode)
                    return ExecGenerateCode(argsJson ?? "{}");
                if (canonicalName == McpToolNames.CreatePrefab)
                    return ExecCreatePrefab(argsJson ?? "{}");

                return MainThread.Run(() => DispatchOnMainThread(canonicalName, argsJson ?? "{}"));
            }
            catch (Exception ex)
            {
                return DispatchResult.Fail("EXECUTION_FAILED", ex.Message);
            }
        }

        internal static string CanonicalizeForApi(string toolNameRaw) => CanonicalizeToolName(toolNameRaw);

        internal static bool IsDangerousToolCall(string canonicalToolName, string argsJson, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(canonicalToolName))
                return false;

            if (string.Equals(canonicalToolName, McpToolNames.DeleteAssets, StringComparison.OrdinalIgnoreCase))
            {
                reason = "delete_assets 会删除资源";
                return true;
            }

            if (string.Equals(canonicalToolName, McpToolNames.OrganizeAssets, StringComparison.OrdinalIgnoreCase))
            {
                reason = "organize_assets 会移动/重命名资源";
                return true;
            }

            if (string.Equals(canonicalToolName, McpToolNames.ExecuteSceneOps, StringComparison.OrdinalIgnoreCase))
            {
                // 只对明显高风险操作要求确认。
                if (Regex.IsMatch(argsJson ?? "", "\"op\"\\s*:\\s*\"(?:destroy|openScene|saveScene)\"", RegexOptions.IgnoreCase))
                {
                    reason = "execute_scene_ops 包含高风险场景操作（destroy/openScene/saveScene）";
                    return true;
                }
            }

            return false;
        }

        private static DispatchResult DispatchOnMainThread(string toolName, string argsJson)
        {
            switch (toolName)
            {
                case McpToolNames.GetSceneState:
                    return DispatchResult.Ok("{\"content\":" + JsonEscaper.Q(ExecGetSceneState()) + "}");
                case McpToolNames.GetProjectInfo:
                    return DispatchResult.Ok("{\"content\":" + JsonEscaper.Q(ExecGetProjectInfo()) + "}");
                case McpToolNames.ExecuteSceneOps:
                    return ExecSceneOps(argsJson);
                case McpToolNames.GenerateCode:
                case McpToolNames.CreatePrefab:
                    return DispatchResult.Fail("EXECUTION_FAILED", $"工具 {toolName} 不应在主线程路由中直接调用");
                case McpToolNames.DeleteAssets:
                    return ExecDeleteAssets(argsJson);
                case McpToolNames.OrganizeAssets:
                    return ExecOrganizeAssets(argsJson);
                default:
                    return DispatchResult.Fail("TOOL_NOT_AVAILABLE", $"不支持工具：{toolName}");
            }
        }

        private static DispatchResult ExecSceneOps(string argsJson)
        {
            var opsJson = JsonFieldReader.ExtractFieldAsJson(argsJson, "operations_json");
            if (string.IsNullOrWhiteSpace(opsJson))
                return DispatchResult.Fail("BAD_REQUEST", "缺少 operations_json");

            if (opsJson.StartsWith("\"", StringComparison.Ordinal))
                opsJson = JsonFieldReader.ExtractString(argsJson, "operations_json");

            if (string.IsNullOrWhiteSpace(opsJson))
                return DispatchResult.Fail("BAD_REQUEST", "operations_json 为空");

            opsJson = NormalizeVectorFieldsInOpsJson(opsJson);
            var envelope = "{\"unityOpsVersion\":1,\"operations\":" + opsJson + "}";
            var parsed = SceneOpsParser.Parse(envelope);
            if (!parsed.Success || parsed.Envelope == null)
                return DispatchResult.Fail("BAD_REQUEST", $"scene-ops 解析失败：{parsed.Error ?? "unknown"}");

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("UnityBridge SceneOps");
            var groupId = Undo.GetCurrentGroup();

            var result = SceneOpsExecutor.Execute(parsed.Envelope);
            Undo.CollapseUndoOperations(groupId);
            EditorApplication.delayCall += SceneView.RepaintAll;

            if (!result.Success)
            {
                var msg = $"场景操作在第 {result.FailedAtIndex + 1} 步失败：{result.Error}";
                return DispatchResult.Fail("EXECUTION_FAILED", msg);
            }

            var dataJson = "{\"success\":true,\"stepsCompleted\":" + result.StepsCompleted.ToString(CultureInfo.InvariantCulture) + "}";
            return DispatchResult.Ok(dataJson);
        }

        private static DispatchResult ExecGenerateCode(string argsJson)
        {
            var specRaw = JsonFieldReader.ExtractFieldAsJson(argsJson, "spec_json");
            if (specRaw.StartsWith("\"", StringComparison.Ordinal))
                specRaw = JsonFieldReader.ExtractString(argsJson, "spec_json");

            if (string.IsNullOrWhiteSpace(specRaw))
                specRaw = argsJson;

            var description = JsonFieldReader.ExtractString(specRaw, "description");
            var className = JsonFieldReader.ExtractString(specRaw, "class_name");
            var codeType = JsonFieldReader.ExtractString(specRaw, "code_type");
            var codeRaw = JsonFieldReader.ExtractString(specRaw, "code");

            if (string.IsNullOrWhiteSpace(codeRaw) && string.IsNullOrWhiteSpace(description))
                return DispatchResult.Fail("BAD_REQUEST", "generate_code 需要 description 或 code");

            try
            {
                string generatedCode;
                string effectiveName;
                float duration = 0f;
                int tokens = 0;

                if (!string.IsNullOrWhiteSpace(codeRaw))
                {
                    var parsedCode = ResponseParser.ParseCodeResponse(codeRaw);
                    if (!parsedCode.Success || string.IsNullOrWhiteSpace(parsedCode.Code))
                        return DispatchResult.Fail("BAD_REQUEST", $"code 字段解析失败：{parsedCode.Error ?? "unknown"}");
                    generatedCode = parsedCode.Code;
                    effectiveName = !string.IsNullOrWhiteSpace(className) ? className : parsedCode.ScriptName;
                }
                else
                {
                    var cfg = AIServiceConfig.Load();
                    var service = AIServiceFactory.Create(cfg);
                    var projCtx = MainThread.Run(ProjectContext.Collect);
                    var sysPrompt = PromptBuilder.BuildCodeSystemPrompt(projCtx, ParseMcpCodeType(codeType));
                    var userMsg = string.IsNullOrWhiteSpace(className)
                        ? description
                        : $"类名：{className}；功能描述：{description}";

                    var aiResp = service.SendMessageAsync(sysPrompt, PromptBuilder.BuildCodeUserPrompt(userMsg)).GetAwaiter().GetResult();
                    if (!aiResp.Success)
                        return DispatchResult.Fail("EXECUTION_FAILED", $"代码生成 AI 失败：{aiResp.Error ?? "unknown"}");

                    var parsed = ResponseParser.ParseCodeResponse(aiResp.Content);
                    if (!parsed.Success)
                        return DispatchResult.Fail("EXECUTION_FAILED", $"代码解析失败：{parsed.Error ?? "unknown"}");

                    generatedCode = parsed.Code;
                    effectiveName = !string.IsNullOrWhiteSpace(parsed.ScriptName)
                        ? parsed.ScriptName
                        : (!string.IsNullOrWhiteSpace(className) ? className : "GeneratedScript");
                    duration = aiResp.Duration;
                    tokens = aiResp.TokensUsed;
                }

                var saveResult = MainThread.Run(() => ScriptGenerator.SaveScript(effectiveName, generatedCode));
                if (!saveResult.Success)
                    return DispatchResult.Fail("EXECUTION_FAILED", $"代码保存失败：{saveResult.Error ?? "unknown"}");

                var dataJson = "{"
                               + "\"scriptName\":" + JsonEscaper.Q(effectiveName) + ","
                               + "\"filePath\":" + JsonEscaper.Q(saveResult.FilePath) + ","
                               + "\"duration\":" + duration.ToString(CultureInfo.InvariantCulture) + ","
                               + "\"tokensUsed\":" + tokens.ToString(CultureInfo.InvariantCulture)
                               + "}";
                return DispatchResult.Ok(dataJson);
            }
            catch (Exception ex)
            {
                return DispatchResult.Fail("EXECUTION_FAILED", $"generate_code 执行异常：{ex.Message}");
            }
        }

        private static DispatchResult ExecCreatePrefab(string argsJson)
        {
            var prefabJsonRaw = JsonFieldReader.ExtractFieldAsJson(argsJson, "prefab_json");
            if (prefabJsonRaw.StartsWith("\"", StringComparison.Ordinal))
                prefabJsonRaw = JsonFieldReader.ExtractString(argsJson, "prefab_json");

            var description = JsonFieldReader.ExtractString(argsJson, "description");
            var prefabName = JsonFieldReader.ExtractString(argsJson, "prefab_name");
            var placeInScene = JsonFieldReader.ExtractBool(argsJson, "place_in_scene");

            try
            {
                PrefabParseResult parsed;
                float duration = 0f;
                int tokens = 0;

                if (!string.IsNullOrWhiteSpace(prefabJsonRaw))
                {
                    parsed = ResponseParser.ParsePrefabResponse(prefabJsonRaw);
                    if (!parsed.Success || parsed.Description == null)
                        return DispatchResult.Fail("BAD_REQUEST", $"prefab_json 解析失败：{parsed.Error ?? "unknown"}");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(description))
                        return DispatchResult.Fail("BAD_REQUEST", "create_prefab 需要 description 或 prefab_json");

                    var cfg = AIServiceConfig.Load();
                    var service = AIServiceFactory.Create(cfg);
                    var projCtx = MainThread.Run(ProjectContext.Collect);
                    var sysPrompt = PromptBuilder.BuildPrefabSystemPrompt(projCtx);
                    var userMsg = string.IsNullOrWhiteSpace(prefabName)
                        ? description
                        : $"预制体名称：{prefabName}；{description}";
                    var aiResp = service.SendMessageAsync(sysPrompt, PromptBuilder.BuildPrefabUserPrompt(userMsg)).GetAwaiter().GetResult();
                    if (!aiResp.Success)
                        return DispatchResult.Fail("EXECUTION_FAILED", $"预制体 AI 失败：{aiResp.Error ?? "unknown"}");
                    parsed = ResponseParser.ParsePrefabResponse(aiResp.Content);
                    if (!parsed.Success || parsed.Description == null)
                        return DispatchResult.Fail("EXECUTION_FAILED", $"预制体 JSON 解析失败：{parsed.Error ?? "unknown"}");
                    duration = aiResp.Duration;
                    tokens = aiResp.TokensUsed;
                }

                if (!string.IsNullOrWhiteSpace(prefabName))
                    parsed.Description!.prefabName = prefabName;

                var generated = MainThread.Run(() => PrefabGenerator.Generate(parsed.Description!));
                if (!generated.Success)
                    return DispatchResult.Fail("EXECUTION_FAILED", $"预制体生成失败：{generated.Error ?? "unknown"}");

                if (placeInScene && !string.IsNullOrWhiteSpace(generated.AssetPath))
                {
                    MainThread.Run(() =>
                    {
                        var r = SceneEditorTools.InstantiatePrefab(generated.AssetPath, null);
                        if (!r.Success)
                            Debug.LogWarning("[UnityBridge] 预制体已生成，但实例化失败: " + r.Error);
                    });
                }

                var dataJson = "{"
                               + "\"prefabName\":" + JsonEscaper.Q(parsed.Description!.prefabName ?? "") + ","
                               + "\"assetPath\":" + JsonEscaper.Q(generated.AssetPath) + ","
                               + "\"placedInScene\":" + (placeInScene ? "true" : "false") + ","
                               + "\"warnings\":" + ToJsonStringArray(generated.Warnings ?? new List<string>()) + ","
                               + "\"duration\":" + duration.ToString(CultureInfo.InvariantCulture) + ","
                               + "\"tokensUsed\":" + tokens.ToString(CultureInfo.InvariantCulture)
                               + "}";
                return DispatchResult.Ok(dataJson);
            }
            catch (Exception ex)
            {
                return DispatchResult.Fail("EXECUTION_FAILED", $"create_prefab 执行异常：{ex.Message}");
            }
        }

        private static DispatchResult ExecDeleteAssets(string argsJson)
        {
            var pathsJsonRaw = JsonFieldReader.ExtractFieldAsJson(argsJson, "asset_paths_json");
            if (string.IsNullOrWhiteSpace(pathsJsonRaw))
                return DispatchResult.Fail("BAD_REQUEST", "缺少 asset_paths_json");

            var pathsJson = pathsJsonRaw.StartsWith("\"", StringComparison.Ordinal)
                ? JsonFieldReader.ExtractString(argsJson, "asset_paths_json")
                : pathsJsonRaw;

            var paths = ParseJsonStringArray(pathsJson);
            if (paths.Count == 0)
                return DispatchResult.Fail("BAD_REQUEST", "asset_paths_json 中无有效路径");

            var deleted = new List<string>();
            var failed = new List<string>();

            foreach (var p in paths)
            {
                if (!p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    failed.Add($"{p}（路径必须以 Assets/ 开头）");
                    continue;
                }

                if (AssetDatabase.DeleteAsset(p))
                    deleted.Add(p);
                else
                    failed.Add($"{p}（删除失败或不存在）");
            }

            AssetDatabase.Refresh();

            var dataJson =
                "{\"deletedCount\":" + deleted.Count.ToString(CultureInfo.InvariantCulture) +
                ",\"failedCount\":" + failed.Count.ToString(CultureInfo.InvariantCulture) +
                ",\"deleted\":" + ToJsonStringArray(deleted) +
                ",\"failed\":" + ToJsonStringArray(failed) + "}";

            if (failed.Count > 0)
                return DispatchResult.Fail("EXECUTION_FAILED", "部分路径删除失败: " + string.Join("；", failed.Take(4)));

            return DispatchResult.Ok(dataJson);
        }

        private static DispatchResult ExecOrganizeAssets(string argsJson)
        {
            var opsJsonRaw = JsonFieldReader.ExtractFieldAsJson(argsJson, "operations_json");
            if (string.IsNullOrWhiteSpace(opsJsonRaw))
                return DispatchResult.Fail("BAD_REQUEST", "缺少 operations_json");

            var opsJson = opsJsonRaw.StartsWith("\"", StringComparison.Ordinal)
                ? JsonFieldReader.ExtractString(argsJson, "operations_json")
                : opsJsonRaw;

            if (string.IsNullOrWhiteSpace(opsJson))
                return DispatchResult.Fail("BAD_REQUEST", "operations_json 为空");

            var envelope = "{\"assetOpsVersion\":1,\"operations\":" + opsJson + "}";
            var parsed = AssetOpsParser.Parse(envelope);
            if (!parsed.Success || parsed.Envelope == null)
                return DispatchResult.Fail("BAD_REQUEST", $"asset-ops 解析失败：{parsed.Error ?? "unknown"}");

            var result = AssetOpsExecutor.Execute(parsed.Envelope);
            if (!result.Success)
                return DispatchResult.Fail("EXECUTION_FAILED", $"asset-ops 第 {result.FailedAtIndex + 1} 步失败：{result.Error}");

            var dataJson = "{\"success\":true,\"stepsCompleted\":" + result.StepsCompleted.ToString(CultureInfo.InvariantCulture) + "}";
            return DispatchResult.Ok(dataJson);
        }

        private static string ExecGetSceneState()
        {
            var hierarchy = PromptBuilder.BuildSceneHierarchyDump();
            string selectedText;
            var selectedGo = Selection.activeGameObject;
            if (selectedGo != null)
            {
                selectedText = "\n\n**当前选中 GameObject：** " + GetGoPath(selectedGo);
            }
            else if (Selection.activeObject != null)
            {
                selectedText = "\n\n**当前选中 Project 资源：** " + AssetDatabase.GetAssetPath(Selection.activeObject);
            }
            else
            {
                selectedText = "\n\n**当前选中：** 无";
            }
            return hierarchy + selectedText;
        }

        private static string ExecGetProjectInfo()
        {
            var p = ProjectContext.Collect();
            var sb = new StringBuilder();
            sb.AppendLine("# 工程资源摘要");
            sb.AppendLine($"- Unity 版本：{p.UnityVersion}  渲染管线：{p.RenderPipeline}");
            sb.AppendLine($"- 默认命名空间：{p.DefaultNamespace}  脚本输出路径：{p.ScriptOutputPath}");
            sb.AppendLine($"\n## C# 脚本（{p.ExistingScripts.Count} 个）");
            for (var i = 0; i < p.ScriptAssetPaths.Count && i < p.ExistingScripts.Count; i++)
                sb.AppendLine($"- {p.ExistingScripts[i]}  →  {p.ScriptAssetPaths[i]}");
            sb.AppendLine($"\n## 预制体（{p.PrefabAssetPaths.Count} 个）");
            foreach (var path in p.PrefabAssetPaths.Take(60))
                sb.AppendLine($"- {path}");
            if (p.PrefabAssetPaths.Count > 60)
                sb.AppendLine($"…（共 {p.PrefabAssetPaths.Count} 个，仅显示前 60）");
            sb.AppendLine($"\n## 材质（{p.MaterialAssetPaths.Count} 个）");
            foreach (var path in p.MaterialAssetPaths.Take(30))
                sb.AppendLine($"- {path}");
            sb.AppendLine($"\n## 贴图（{p.Texture2DAssetPaths.Count} 个）");
            foreach (var path in p.Texture2DAssetPaths.Take(30))
                sb.AppendLine($"- {path}");
            return sb.ToString();
        }

        private static ToolDescriptor Descriptor(string name, string description, bool available, string note) => new()
        {
            Name = name,
            Description = description,
            Available = available,
            Note = note
        };

        private static string CanonicalizeToolName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            if (ToolAliases.TryGetValue(name.Trim(), out var mapped))
                return mapped;
            return name.Trim();
        }

        private static string GetGoPath(GameObject go)
        {
            var parts = new Stack<string>();
            var t = go.transform;
            while (t != null)
            {
                parts.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        private static CodeType ParseMcpCodeType(string s) => s?.ToLowerInvariant() switch
        {
            "monobehaviour" => CodeType.MonoBehaviour,
            "scriptableobject" => CodeType.ScriptableObject,
            _ => CodeType.Auto
        };

        private static string ToJsonStringArray(IEnumerable<string> values) =>
            "[" + string.Join(",", values.Select(JsonEscaper.Q)) + "]";

        private static List<string> ParseJsonStringArray(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return result;
            var arr = json.Trim();
            if (!arr.StartsWith("[", StringComparison.Ordinal)) return result;

            var i = 1;
            while (i < arr.Length)
            {
                var q = arr.IndexOf('"', i);
                if (q < 0) break;
                var sb = new StringBuilder();
                var j = q + 1;
                while (j < arr.Length)
                {
                    var c = arr[j];
                    if (c == '\\' && j + 1 < arr.Length)
                    {
                        var n = arr[j + 1];
                        switch (n)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            default: sb.Append(n); break;
                        }
                        j += 2;
                        continue;
                    }
                    if (c == '"') break;
                    sb.Append(c);
                    j++;
                }
                result.Add(sb.ToString());
                i = j + 1;
            }

            return result;
        }

        private static string NormalizeVectorFieldsInOpsJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            foreach (var f in new[] { "localPosition", "localEulerAngles", "localScale" })
                json = NormalizeVectorField(json, f, 3);
            foreach (var f in new[] { "anchoredPosition", "anchorMin", "anchorMax", "sizeDelta", "pivot", "offsetMin", "offsetMax" })
                json = NormalizeVectorField(json, f, 2);
            return json;
        }

        private static string NormalizeVectorField(string json, string fieldName, int dims)
        {
            var pattern = "\"" + fieldName + @"""\s*:\s*\{([^{}]+)\}";
            return Regex.Replace(json, pattern, m =>
            {
                var inner = m.Groups[1].Value;
                var x = ExtractNumFromVecInner(inner, "x");
                var y = ExtractNumFromVecInner(inner, "y");
                if (x == null || y == null) return m.Value;

                if (dims == 3)
                {
                    var z = ExtractNumFromVecInner(inner, "z");
                    if (z == null) return m.Value;
                    return "\"" + fieldName + "\": \"" + x + "," + y + "," + z + "\"";
                }
                return "\"" + fieldName + "\": \"" + x + "," + y + "\"";
            });
        }

        private static string? ExtractNumFromVecInner(string inner, string key)
        {
            var needle = "\"" + key + "\"";
            var idx = inner.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var colon = inner.IndexOf(':', idx + needle.Length);
            if (colon < 0) return null;
            var rest = inner.Substring(colon + 1).TrimStart();
            var match = Regex.Match(rest, @"^-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?");
            if (!match.Success) return null;
            if (float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                return f.ToString(CultureInfo.InvariantCulture);
            return match.Value;
        }
    }

    internal static class JsonEscaper
    {
        internal static string Q(string s)
        {
            s ??= "";
            return "\"" + s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t") + "\"";
        }
    }

    internal static class JsonFieldReader
    {
        internal static string ExtractString(string json, string key)
        {
            var valStart = LocateValueStart(json, key);
            if (valStart < 0 || valStart >= json.Length || json[valStart] != '"')
                return "";

            var sb = new StringBuilder();
            var i = valStart + 1;
            while (i < json.Length)
            {
                var c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    var n = json[i + 1];
                    switch (n)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(n); break;
                    }
                    i += 2;
                    continue;
                }
                if (c == '"') break;
                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        internal static string ExtractFieldAsJson(string json, string key)
        {
            var valStart = LocateValueStart(json, key);
            if (valStart < 0 || valStart >= json.Length) return "";
            var c = json[valStart];
            if (c == '{')
            {
                var end = FindBracketEnd(json, valStart, '{', '}');
                return end < 0 ? "" : json.Substring(valStart, end - valStart + 1);
            }
            if (c == '[')
            {
                var end = FindBracketEnd(json, valStart, '[', ']');
                return end < 0 ? "" : json.Substring(valStart, end - valStart + 1);
            }
            if (c == '"')
            {
                var end = FindStringEnd(json, valStart);
                return end < 0 ? "" : json.Substring(valStart, end - valStart + 1);
            }

            var i = valStart;
            while (i < json.Length && json[i] != ',' && json[i] != '}') i++;
            return json.Substring(valStart, i - valStart).Trim();
        }

        private static int LocateValueStart(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return -1;

            var needle = "\"" + key + "\"";
            var idx = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return -1;
            var colon = json.IndexOf(':', idx + needle.Length);
            if (colon < 0) return -1;
            var p = colon + 1;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            return p;
        }

        private static int FindStringEnd(string json, int startQuote)
        {
            var i = startQuote + 1;
            while (i < json.Length)
            {
                if (json[i] == '\\')
                {
                    i += 2;
                    continue;
                }
                if (json[i] == '"')
                    return i;
                i++;
            }
            return -1;
        }

        internal static bool ExtractBool(string json, string key)
        {
            var value = ExtractFieldAsJson(json, key);
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.TrimStart().StartsWith("true", StringComparison.OrdinalIgnoreCase);
        }

        private static int FindBracketEnd(string s, int start, char open, char close)
        {
            var depth = 0;
            var inStr = false;
            for (var i = start; i < s.Length; i++)
            {
                if (inStr)
                {
                    if (s[i] == '\\') { i++; continue; }
                    if (s[i] == '"') inStr = false;
                    continue;
                }
                if (s[i] == '"') { inStr = true; continue; }
                if (s[i] == open) { depth++; continue; }
                if (s[i] == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}
