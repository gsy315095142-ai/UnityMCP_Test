#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityMCP.AI;
using UnityMCP.Core;
using UnityMCP.Generators;
using UnityMCP.Tools;

namespace UnityMCP.UI
{
    /// <summary>
    /// MCP 工具调用模式：AI 通过一组预定义工具与 Unity 工程交互，替代原来的「意图路由 + 单次 AI 调用」流程。
    /// 整体流程：
    ///   1. 将用户请求 + 聊天记忆组装为对话消息列表。
    ///   2. 循环调用 <see cref="IAIService.SendWithToolsAsync"/>，AI 调用工具 → 插件执行 → 结果回送 AI。
    ///   3. AI 调用 reply 工具 或直接给出文字响应时，循环结束，结果展示在聊天界面。
    /// </summary>
    public partial class AIQuickCommand : EditorWindow
    {
        #region MCP 主循环

        private async void McpGenerateAsync(ChatMessage context)
        {
            if (_config == null)
            {
                context.ErrorMessage = "AI 服务未配置，请先打开设置窗口进行配置。";
                context.Type = MessageTypeEnum.Error;
                _isGenerating = false;
                AddResultBubble(context);
                return;
            }

            try
            {
                var service   = AIServiceFactory.Create(_config);
                var toolsJson = McpToolDefinitions.GetToolsJson();
                var memory    = BuildChatMemory(context.Content);

                // ── 构建初始对话消息列表 ─────────────────────────────────────────────
                var messages = new List<string>();
                messages.Add(BuildMcpSystemMsg());

                // 注入聊天记忆（历史 user/assistant 轮次）
                foreach (var t in memory)
                {
                    var role = (t.Role ?? "").Trim().ToLowerInvariant();
                    if (role != "user" && role != "assistant") continue;
                    messages.Add(McpMsg(role, t.Content ?? ""));
                }

                // 本轮用户消息（含附件信息）
                var userContent = context.Content
                    + PromptBuilder.BuildDroppedAssetsContext(context.DroppedAssets);
                messages.Add(McpMsg("user", userContent));

                // ── 工具调用主循环（最多 12 轮，第 12 轮强制收尾）───────────────────
                const int MaxIter      = 12;
                const int ForceReplyAt = 11; // 最后一轮前，追加"必须立即 reply"催促
                for (var iter = 0; iter < MaxIter; iter++)
                {
                    AddTextBubble(iter == 0
                        ? "⚙️ AI 正在分析请求..."
                        : $"⚙️ AI 处理中（第 {iter + 1} 轮）...");

                    // 在即将超限时注入一条催促消息，强制 AI 调用 reply 结束
                    if (iter == ForceReplyAt)
                        messages.Add(McpMsg("user",
                            "【系统提示】请不要再调用任何工具，直接调用 reply 工具，用中文向用户总结你已完成的所有操作，然后结束。"));

                    var mcpResp = await service.SendWithToolsAsync(messages, toolsJson);

                    // 记录本轮到 API 日志
                    var logResp = mcpResp.Success
                        ? AIResponse.Ok(mcpResp.AssistantMessageJson, mcpResp.Duration, mcpResp.TokensUsed)
                        : AIResponse.Fail(mcpResp.Error ?? "unknown");
                    LogAiExchange($"MCP 第{iter + 1}轮", logResp);

                    if (!mcpResp.Success)
                    {
                        context.ErrorMessage = mcpResp.Error ?? "工具调用 AI 请求失败";
                        context.Type = MessageTypeEnum.Error;
                        _isGenerating = false;
                        AddResultBubble(context);
                        return;
                    }

                    // 将本轮 assistant 消息加入历史
                    messages.Add(mcpResp.AssistantMessageJson);

                    // AI 直接给出文字回复（无 tool_calls）→ 展示并结束
                    if (mcpResp.IsTextReply)
                    {
                        ShowMcpFinalReply(context, mcpResp.TextContent,
                            mcpResp.Duration, mcpResp.TokensUsed);
                        return;
                    }

                    // ── 执行本轮所有工具调用 ───────────────────────────────────────
                    var shouldExit = false;
                    foreach (var tc in mcpResp.ToolCalls)
                    {
                        AddTextBubble($"⚙️ 执行工具：{McpToolDisplayName(tc.FunctionName)}...");

                        // reply 工具 → 展示给用户并结束循环
                        if (tc.FunctionName == McpToolNames.Reply)
                        {
                            var msg = ExtractArgStr(tc.ArgumentsJson, "message");
                            messages.Add(McpToolResultMsg(tc.Id, "已展示回复。"));
                            ShowMcpFinalReply(context, msg, mcpResp.Duration, mcpResp.TokensUsed);
                            shouldExit = true;
                            break;
                        }

                        var result = await ExecMcpToolAsync(tc);
                        messages.Add(McpToolResultMsg(tc.Id, result.Content));
                    }

                    if (shouldExit) return;
                }

                // 超出迭代上限（理论上不应走到这里，因为第 12 轮已强制收尾）
                ShowMcpFinalReply(context, "操作已完成，但因步骤较多未能生成完整总结。请查看聊天记录了解执行详情。",
                    0, 0);
            }
            catch (Exception ex)
            {
                AiExchangeDebugLog.AppendException("MCP 工具调用", ex);
                context.ErrorMessage = $"MCP 工具调用出错: {ex.Message}";
                context.Type = MessageTypeEnum.Error;
                _isGenerating = false;
                AddResultBubble(context);
            }
            finally
            {
                Repaint();
            }
        }

        private void ShowMcpFinalReply(ChatMessage ctx, string text, float dur, int tokens)
        {
            ctx.Type           = MessageTypeEnum.SuccessResult;
            ctx.Content        = text;
            ctx.GenerationTime = dur;
            ctx.TokensUsed     = tokens;
            _isGenerating      = false;
            AddResultBubble(ctx);
            Repaint();
        }

        #endregion

        #region 工具执行路由

        private async Task<McpToolResult> ExecMcpToolAsync(McpToolCall tc)
        {
            try
            {
                switch (tc.FunctionName)
                {
                    case McpToolNames.GetSceneState:   return McpToolResult.Ok(tc.Id, ExecGetSceneState());
                    case McpToolNames.GetProjectInfo:  return McpToolResult.Ok(tc.Id, ExecGetProjectInfo());
                    case McpToolNames.ExecuteSceneOps: return ExecSceneOps(tc);
                    case McpToolNames.GenerateCode:    return await ExecGenerateCodeAsync(tc);
                    case McpToolNames.CreatePrefab:    return await ExecCreatePrefabAsync(tc);
                    case McpToolNames.DeleteAssets:    return ExecDeleteAssets(tc);
                    case McpToolNames.OrganizeAssets:  return ExecOrganizeAssets(tc);
                    default:
                        return McpToolResult.Fail(tc.Id, $"未知工具名称：{tc.FunctionName}");
                }
            }
            catch (Exception ex)
            {
                return McpToolResult.Fail(tc.Id, $"工具 {tc.FunctionName} 执行异常：{ex.Message}");
            }
        }

        #endregion

        #region 各工具实现

        // ── get_scene_state ────────────────────────────────────────────────────

        private static string ExecGetSceneState()
        {
            var hierarchy = PromptBuilder.BuildSceneHierarchyDump();

            var selInfo = "";
            var selGo   = Selection.activeGameObject;
            if (selGo != null)
                selInfo = $"\n\n**当前选中 GameObject：** {GetGoPath(selGo)}";
            else if (Selection.activeObject != null)
                selInfo = $"\n\n**当前选中 Project 资源：** {AssetDatabase.GetAssetPath(Selection.activeObject)}";
            else
                selInfo = "\n\n**当前选中：** 无";

            return hierarchy + selInfo;
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

        // ── get_project_info ───────────────────────────────────────────────────

        private static string ExecGetProjectInfo()
        {
            var p  = ProjectContext.Collect();
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

        // ── execute_scene_ops ──────────────────────────────────────────────────

        private static McpToolResult ExecSceneOps(McpToolCall tc)
        {
            var opsJson = ExtractArgStr(tc.ArgumentsJson, "operations_json");
            if (string.IsNullOrWhiteSpace(opsJson))
                return McpToolResult.Fail(tc.Id, "operations_json 参数为空");

            // AI 可能将向量字段写成 {"x":1,"y":2} 对象，规范化为 JsonUtility 期望的 "1,2" 字符串
            opsJson = NormalizeVectorFieldsInOpsJson(opsJson);

            // 包装成 SceneOpsParser 期望的信封格式
            var envelope    = $"{{\"unityOpsVersion\":1,\"operations\":{opsJson}}}";
            var parseResult = SceneOpsParser.Parse(envelope);
            if (!parseResult.Success)
                return McpToolResult.Fail(tc.Id, $"场景操作 JSON 解析失败：{parseResult.Error}");

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("MCP 场景操作");
            var groupId = Undo.GetCurrentGroup();

            var batchResult = SceneOpsExecutor.Execute(parseResult.Envelope!);
            Undo.CollapseUndoOperations(groupId);

            EditorApplication.delayCall += SceneView.RepaintAll;

            if (batchResult.Success)
                return McpToolResult.Ok(tc.Id,
                    $"场景操作全部成功，共完成 {batchResult.StepsCompleted} 步。");

            return McpToolResult.Fail(tc.Id,
                $"场景操作在第 {batchResult.FailedAtIndex + 1} 步失败" +
                $"（共 {parseResult.Envelope!.operations.Length} 步）：{batchResult.Error}");
        }

        // ── generate_code ──────────────────────────────────────────────────────

        private async Task<McpToolResult> ExecGenerateCodeAsync(McpToolCall tc)
        {
            var description = ExtractArgStr(tc.ArgumentsJson, "description");
            var className   = ExtractArgStr(tc.ArgumentsJson, "class_name");
            var codeTypeStr = ExtractArgStr(tc.ArgumentsJson, "code_type");

            if (string.IsNullOrWhiteSpace(description))
                return McpToolResult.Fail(tc.Id, "description 参数为空");

            var codeType = ParseMcpCodeType(codeTypeStr);

            try
            {
                var service  = AIServiceFactory.Create(_config!);
                var projCtx  = ProjectContext.Collect();
                var sysPmt   = PromptBuilder.BuildCodeSystemPrompt(projCtx, codeType);
                var userMsg  = string.IsNullOrEmpty(className)
                    ? description
                    : $"类名：{className}；功能描述：{description}";
                var resp = await service.SendMessageAsync(sysPmt, PromptBuilder.BuildCodeUserPrompt(userMsg));
                LogAiExchange("MCP-生成代码", resp);

                if (!resp.Success)
                    return McpToolResult.Fail(tc.Id, $"代码生成 AI 失败：{resp.Error}");

                var parsed = ResponseParser.ParseCodeResponse(resp.Content);
                if (!parsed.Success)
                    return McpToolResult.Fail(tc.Id, $"代码解析失败：{parsed.Error}");

                var effectiveName = !string.IsNullOrEmpty(parsed.ScriptName)
                    ? parsed.ScriptName
                    : (!string.IsNullOrEmpty(className) ? className : "GeneratedScript");

                var saved = ScriptGenerator.SaveScript(effectiveName, parsed.Code);
                if (!saved.Success)
                    return McpToolResult.Fail(tc.Id, $"代码保存失败：{saved.Error}");

                // 展示代码气泡（已自动保存，不需要手动点"保存"按钮）
                var codeBubble = new ChatMessage
                {
                    Role           = ChatRole.Assistant,
                    Type           = MessageTypeEnum.CodeGenerated,
                    Content        = description,
                    GeneratedCode  = parsed.Code,
                    ScriptName     = effectiveName,
                    SavedScriptPath = saved.FilePath,
                    GenerationTime  = resp.Duration,
                    TokensUsed      = resp.TokensUsed,
                };
                AddResultBubble(codeBubble, removeWaitingTextBubbles: false);
                Repaint();

                return McpToolResult.Ok(tc.Id,
                    $"代码已保存至 {saved.FilePath}（类名：{effectiveName}）。");
            }
            catch (Exception ex)
            {
                return McpToolResult.Fail(tc.Id, $"代码生成异常：{ex.Message}");
            }
        }

        // ── create_prefab ──────────────────────────────────────────────────────

        private async Task<McpToolResult> ExecCreatePrefabAsync(McpToolCall tc)
        {
            var description  = ExtractArgStr(tc.ArgumentsJson, "description");
            var prefabName   = ExtractArgStr(tc.ArgumentsJson, "prefab_name");
            var placeInScene = ExtractArgBool(tc.ArgumentsJson, "place_in_scene");

            if (string.IsNullOrWhiteSpace(description))
                return McpToolResult.Fail(tc.Id, "description 参数为空");

            try
            {
                var service = AIServiceFactory.Create(_config!);
                var projCtx = ProjectContext.Collect();
                var sysPmt  = PromptBuilder.BuildPrefabSystemPrompt(projCtx);
                var userMsg = string.IsNullOrEmpty(prefabName)
                    ? description
                    : $"预制体名称：{prefabName}；{description}";
                var resp = await service.SendMessageAsync(sysPmt, PromptBuilder.BuildPrefabUserPrompt(userMsg));
                LogAiExchange("MCP-生成预制体", resp);

                if (!resp.Success)
                    return McpToolResult.Fail(tc.Id, $"预制体 AI 失败：{resp.Error}");

                var parsed = ResponseParser.ParsePrefabResponse(resp.Content);
                if (!parsed.Success)
                    return McpToolResult.Fail(tc.Id, $"预制体 JSON 解析失败：{parsed.Error}");

                var generated = PrefabGenerator.Generate(parsed.Description!);
                if (!generated.Success)
                    return McpToolResult.Fail(tc.Id, $"预制体生成失败：{generated.Error}");

                if (placeInScene && !string.IsNullOrEmpty(generated.AssetPath))
                    TryInstantiatePrefabInActiveScene(generated.AssetPath);

                // 展示预制体气泡
                var prefabBubble = new ChatMessage
                {
                    Role             = ChatRole.Assistant,
                    Type             = MessageTypeEnum.PrefabGenerated,
                    Content          = description,
                    PrefabDescription = parsed.Description,
                    PrefabName       = parsed.Description!.prefabName,
                    RawJson          = parsed.RawJson,
                    SavedPrefabPath  = generated.AssetPath,
                    GenerationTime   = resp.Duration,
                    TokensUsed       = resp.TokensUsed,
                    PrefabWarnings   = generated.Warnings,
                };
                AddResultBubble(prefabBubble, removeWaitingTextBubbles: false);
                Repaint();

                return McpToolResult.Ok(tc.Id,
                    $"预制体已保存至 {generated.AssetPath}。"
                    + (placeInScene ? " 已实例化到当前场景。" : ""));
            }
            catch (Exception ex)
            {
                return McpToolResult.Fail(tc.Id, $"预制体生成异常：{ex.Message}");
            }
        }

        // ── delete_assets ──────────────────────────────────────────────────────

        private static McpToolResult ExecDeleteAssets(McpToolCall tc)
        {
            var pathsJson = ExtractArgStr(tc.ArgumentsJson, "asset_paths_json");
            if (string.IsNullOrWhiteSpace(pathsJson))
                return McpToolResult.Fail(tc.Id, "asset_paths_json 参数为空");

            var paths = ParseJsonStringArray(pathsJson);
            if (paths.Count == 0)
                return McpToolResult.Fail(tc.Id, "未能解析出有效的路径列表");

            var deleted  = new List<string>();
            var failures = new List<string>();

            foreach (var p in paths)
            {
                if (!p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{p}（路径必须以 Assets/ 开头）");
                    continue;
                }
                if (AssetDatabase.DeleteAsset(p))
                    deleted.Add(p);
                else
                    failures.Add($"{p}（删除失败，可能不存在）");
            }

            AssetDatabase.Refresh();

            var sb = new StringBuilder();
            if (deleted.Count  > 0) sb.AppendLine($"已删除 {deleted.Count} 个：{string.Join("，", deleted)}");
            if (failures.Count > 0) sb.AppendLine($"失败 {failures.Count} 个：{string.Join("；", failures)}");

            return failures.Count == 0
                ? McpToolResult.Ok(tc.Id, sb.ToString().Trim())
                : McpToolResult.Fail(tc.Id, sb.ToString().Trim());
        }

        // ── organize_assets ────────────────────────────────────────────────────

        private static McpToolResult ExecOrganizeAssets(McpToolCall tc)
        {
            var opsJson = ExtractArgStr(tc.ArgumentsJson, "operations_json");
            if (string.IsNullOrWhiteSpace(opsJson))
                return McpToolResult.Fail(tc.Id, "operations_json 参数为空");

            var envelope = $"{{\"assetOpsVersion\":1,\"operations\":{opsJson}}}";
            var parsed   = AssetOpsParser.Parse(envelope);
            if (!parsed.Success)
                return McpToolResult.Fail(tc.Id, $"资源整理 JSON 解析失败：{parsed.Error}");

            var result = AssetOpsExecutor.Execute(parsed.Envelope!);
            return result.Success
                ? McpToolResult.Ok(tc.Id, $"资源整理完成，共 {result.StepsCompleted} 步。")
                : McpToolResult.Fail(tc.Id,
                    $"资源整理在第 {result.FailedAtIndex + 1} 步失败：{result.Error}");
        }

        #endregion

        #region 对话消息构建辅助

        private static string BuildMcpSystemMsg()
        {
            const string content =
                "你是 Unity 编辑器 AI 助手（UnityMCP）。你可以使用工具查询工程状态并执行操作。\n\n" +

                "## 标准执行流程（严格遵守）\n" +
                "1. **查询阶段**（按需，各最多调用 1 次）\n" +
                "   - 需要操作场景时：先调用 get_scene_state（仅调 1 次，后续无需重复）\n" +
                "   - 需要查看资源时：先调用 get_project_info（仅调 1 次）\n" +
                "2. **执行阶段**：调用对应操作工具（execute_scene_ops / generate_code / create_prefab / delete_assets / organize_assets）\n" +
                "   - execute_scene_ops 的 operations 数组可包含多个步骤，尽量合并为 **1 次调用**\n" +
                "3. **必须收尾**：操作完成后，**立即调用 reply 工具**，用中文向用户说明执行结果，然后停止。\n\n" +

                "## 关键约束\n" +
                "- **reply 是强制步骤**，不得省略；操作成功或失败，都必须以 reply 结束本轮对话。\n" +
                "- 不要重复调用同类查询工具（get_scene_state / get_project_info 每次对话各最多调 1 次）。\n" +
                "- 不要在工具调用之间插入多余的确认轮次；直接执行，完成后一次性 reply。\n" +
                "- createPrimitive 规则：球体/正方体/胶囊体等 Unity 内置几何体必须用 op=createPrimitive" +
                "（primitiveType: Sphere/Cube/Capsule/Cylinder/Plane/Quad），禁止用 instantiatePrefab。\n" +
                "- 使用中文回复用户。";

            return "{\"role\":\"system\",\"content\":" + MJ(content) + "}";
        }

        /// <summary>构建一条普通角色消息的原始 JSON 字符串。</summary>
        private static string McpMsg(string role, string content) =>
            "{\"role\":" + MJ(role) + ",\"content\":" + MJ(content) + "}";

        /// <summary>构建 tool-result 消息的原始 JSON 字符串。</summary>
        private static string McpToolResultMsg(string toolCallId, string content) =>
            "{\"role\":\"tool\",\"tool_call_id\":" + MJ(toolCallId) + ",\"content\":" + MJ(content) + "}";

        /// <summary>JSON 字符串转义并包上双引号（MJ = Make JSON string）。</summary>
        private static string MJ(string s) =>
            "\"" + s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
            + "\"";

        #endregion

        #region 参数解析辅助

        /// <summary>从 arguments JSON 提取字符串字段（处理 JSON 转义）。</summary>
        private static string ExtractArgStr(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return "";
            var needle = "\"" + key + "\"";
            var idx    = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";

            var colon = json.IndexOf(':', idx + needle.Length);
            if (colon < 0) return "";

            // 跳过空白，找第一个非空白字符
            var valStart = colon + 1;
            while (valStart < json.Length && json[valStart] == ' ') valStart++;

            if (valStart >= json.Length) return "";

            // 若值以 { 或 [ 开头，返回整段 JSON
            if (json[valStart] == '{' || json[valStart] == '[')
            {
                var open  = json[valStart];
                var close = open == '{' ? '}' : ']';
                var end   = MjFindBracket(json, valStart, open, close);
                return end < 0 ? "" : json.Substring(valStart, end - valStart + 1);
            }

            // 字符串值
            if (json[valStart] != '"') return "";
            var sb = new StringBuilder();
            var i  = valStart + 1;
            while (i < json.Length)
            {
                var c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    var n = json[i + 1];
                    switch (n)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        default:   sb.Append(n);    break;
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

        private static bool ExtractArgBool(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return false;
            var needle = "\"" + key + "\"";
            var idx    = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            var colon = json.IndexOf(':', idx + needle.Length);
            if (colon < 0) return false;
            var after = json.Substring(colon + 1).TrimStart();
            return after.StartsWith("true", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> ParseJsonStringArray(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(json)) return result;

            var arr = json.Trim();
            if (!arr.StartsWith("[")) return result;

            var i = 1;
            while (i < arr.Length)
            {
                var q = arr.IndexOf('"', i);
                if (q < 0) break;
                var sb = new StringBuilder();
                var j  = q + 1;
                while (j < arr.Length)
                {
                    var c = arr[j];
                    if (c == '\\' && j + 1 < arr.Length) { sb.Append(arr[j + 1]); j += 2; continue; }
                    if (c == '"') break;
                    sb.Append(c);
                    j++;
                }
                result.Add(sb.ToString());
                i = j + 1;
            }
            return result;
        }

        private static int MjFindBracket(string s, int start, char open, char close)
        {
            var depth = 0;
            var inStr = false;
            for (var i = start; i < s.Length; i++)
            {
                if (inStr)
                {
                    if (s[i] == '\\') { i++; continue; }
                    if (s[i] == '"')  inStr = false;
                    continue;
                }
                if (s[i] == '"')   { inStr = true; continue; }
                if (s[i] == open)  { depth++; continue; }
                if (s[i] == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static CodeType ParseMcpCodeType(string s) => s?.ToLowerInvariant() switch
        {
            "monobehaviour"   => CodeType.MonoBehaviour,
            "scriptableobject"=> CodeType.ScriptableObject,
            _                 => CodeType.Auto
        };

        // ── 向量字段格式规范化 ─────────────────────────────────────────────────
        // AI 有时会将 localPosition / anchoredPosition 等字段写成 JSON 对象 {"x":1,"y":2}，
        // 而 SceneOperationDto 期望的是逗号字符串 "1,2"。此方法在送入解析器前做兼容转换。

        private static string NormalizeVectorFieldsInOpsJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;

            // 3D 向量字段（格式 "x,y,z"）
            foreach (var f in new[] { "localPosition", "localEulerAngles", "localScale" })
                json = NormalizeVectorField(json, f, 3);

            // 2D 向量字段（格式 "x,y"）
            foreach (var f in new[] { "anchoredPosition", "anchorMin", "anchorMax",
                                      "sizeDelta", "pivot", "offsetMin", "offsetMax" })
                json = NormalizeVectorField(json, f, 2);

            return json;
        }

        /// <summary>
        /// 将 "fieldName": {"x":N,"y":N[,"z":N]} 替换为 "fieldName": "N,N[,N]"。
        /// 模式只匹配不含嵌套花括号的简单对象，不会误伤其他字段。
        /// </summary>
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

        /// <summary>从 {"x":1.5,"y":0} 这样的内部字符串中提取数值字段，返回 InvariantCulture 格式字符串。</summary>
        private static string? ExtractNumFromVecInner(string inner, string key)
        {
            var needle = "\"" + key + "\"";
            var idx    = inner.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var colon = inner.IndexOf(':', idx + needle.Length);
            if (colon < 0) return null;
            var rest  = inner.Substring(colon + 1).TrimStart();
            var match = Regex.Match(rest, @"^-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?");
            if (!match.Success) return null;
            if (float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                return f.ToString(CultureInfo.InvariantCulture);
            return match.Value;
        }

        private static string McpToolDisplayName(string toolName) => toolName switch
        {
            McpToolNames.GetSceneState   => "查询场景状态",
            McpToolNames.GetProjectInfo  => "查询工程资源",
            McpToolNames.ExecuteSceneOps => "执行场景操作",
            McpToolNames.GenerateCode    => "生成代码",
            McpToolNames.CreatePrefab    => "创建预制体",
            McpToolNames.DeleteAssets    => "删除资源",
            McpToolNames.OrganizeAssets  => "整理资源",
            McpToolNames.Reply           => "回复用户",
            _                            => toolName
        };

        #endregion
    }
}
