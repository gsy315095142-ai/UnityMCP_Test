#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMCP.AI;
using UnityMCP.Core;
using UnityMCP.Generators;
using UnityMCP.Tools;

namespace UnityMCP.UI
{
    public partial class AIQuickCommand : EditorWindow
    {
        #region 聊天与生成核心逻辑

        private void StartNewTask()
        {
            if (string.IsNullOrWhiteSpace(_userInput)) return;

            var userText = _userInput;
            _userInput = "";
            GUI.FocusControl(null); 

            // 用户输入气泡
            _chatHistory.Add(ChatMessage.CreateText(ChatRole.User, userText));
            PersistChatHistory();
            ScrollToBottom();

            // 创建执行上下文
            var contextMsg = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = userText, // 保存初始输入用于重试或传给下一步
                Mode = _currentMode,
                CodeType = _currentCodeType
            };

            ExecuteTaskPhase(contextMsg);
        }

        /// <summary>
        /// 根据设置与当前聊天历史构造多轮记忆（不含本轮用户句）。
        /// </summary>
        private IReadOnlyList<ChatMemoryTurn> BuildChatMemory(string currentUserContent)
        {
            if (_config == null || _config.chatMemoryMaxTurns <= 0)
                return Array.Empty<ChatMemoryTurn>();
            return ChatHistoryMemoryBuilder.BuildPriorTurns(_chatHistory, _config.chatMemoryMaxTurns, currentUserContent);
        }

        private void RetryTask(ChatMessage failedMsg)
        {
            // 对于重试，我们复制原任务信息，并重新执行
            var contextMsg = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = failedMsg.Content,
                Mode = failedMsg.Mode,
                CodeType = failedMsg.CodeType
            };
            ExecuteTaskPhase(contextMsg);
        }

        private void ExecuteTaskPhase(ChatMessage context)
        {
            _isGenerating = true;
            _pendingMessage = context;

            switch (context.Mode)
            {
                case GenerateMode.AiJudge:
                    AddTextBubble("⏳ AI 正在分析需求类型，请稍候...");
                    ResolveIntentThenExecuteAsync(context);
                    break;
                case GenerateMode.Code:
                    AddTextBubble("⏳ 正在生成代码，请稍候...");
                    GenerateCodeAsync(context);
                    break;
                case GenerateMode.Prefab:
                    AddTextBubble("⏳ 正在生成预制体，请稍候...");
                    GeneratePrefabAsync(context);
                    break;
                case GenerateMode.Combined:
                    AddTextBubble("⏳ 联合生成 (第1步): 正在生成代码，请稍候...");
                    GenerateCodeAsync(context);
                    break;
                case GenerateMode.CombinedPrefabFirst:
                    AddTextBubble("⏳ 联合生成 (第1步): 正在生成预制体，请稍候...");
                    GeneratePrefabAsync(context);
                    break;
                case GenerateMode.SceneOps:
                    AddTextBubble("⏳ 正在生成场景操控列表，请稍候...");
                    GenerateSceneOpsAsync(context);
                    break;
                case GenerateMode.ProjectQuery:
                    AddTextBubble("⏳ 正在根据项目上下文回答，请稍候...");
                    ProjectQueryAsync(context);
                    break;
                case GenerateMode.AssetDelete:
                    AddTextBubble("⏳ 正在解析待删除资源列表，请稍候...");
                    AssetDeleteAsync(context);
                    break;
                case GenerateMode.AssetOps:
                    AddTextBubble("⏳ 正在生成资源整理步骤，请稍候...");
                    GenerateAssetOpsAsync(context);
                    break;
            }
            ScrollToBottom();
        }

        private void AddTextBubble(string text)
        {
            // 移除历史消息中仍是"正在生成"的纯文本气泡（为了UI整洁）
            _chatHistory.RemoveAll(m => m.Role == ChatRole.Assistant && m.Type == MessageTypeEnum.Text && m.Content.StartsWith("⏳"));
            
            var msg = ChatMessage.CreateText(ChatRole.Assistant, text);
            _chatHistory.Add(msg);
            PersistChatHistory();
            ScrollToBottom();
        }

        private void AddResultBubble(ChatMessage msg)
        {
            // 同样移除等待中的文本气泡
            _chatHistory.RemoveAll(m => m.Role == ChatRole.Assistant && m.Type == MessageTypeEnum.Text && m.Content.StartsWith("⏳"));
            // 同一条实例不应出现两次，否则阶段切换时一处改 Type/Content 会牵动所有气泡
            if (_chatHistory.Contains(msg))
                msg = ChatMessage.CloneSnapshot(msg);
            _chatHistory.Add(msg);
            PersistChatHistory();
            ScrollToBottom();
        }

        private static GenerateMode MapRouteToMode(GenerationRoute route, bool combinedPrefabFirst) => route switch
        {
            GenerationRoute.Code => GenerateMode.Code,
            GenerationRoute.Prefab => GenerateMode.Prefab,
            GenerationRoute.Both => combinedPrefabFirst ? GenerateMode.CombinedPrefabFirst : GenerateMode.Combined,
            GenerationRoute.SceneOps => GenerateMode.SceneOps,
            GenerationRoute.ProjectQuery => GenerateMode.ProjectQuery,
            GenerationRoute.AssetDelete => GenerateMode.AssetDelete,
            GenerationRoute.AssetOps => GenerateMode.AssetOps,
            _ => GenerateMode.Code
        };

        private static string ModeDecisionLabel(GenerateMode mode) => mode switch
        {
            GenerateMode.Code => "生成代码",
            GenerateMode.Prefab => "生成预制体",
            GenerateMode.Combined => "联合生成（代码 + 预制体）",
            GenerateMode.CombinedPrefabFirst => "联合生成（先预制体 + 脚本）",
            GenerateMode.SceneOps => "场景操控（unity-ops）",
            GenerateMode.ProjectQuery => "项目查询（盘点预制体等）",
            GenerateMode.AssetDelete => "删除 Project 资源",
            GenerateMode.AssetOps => "整理资源（asset-ops）",
            _ => mode.ToString()
        };

        /// <summary>
        /// 【AI判断】先调用模型输出路由 JSON，再进入对应生成流程。
        /// </summary>
        private async void ResolveIntentThenExecuteAsync(ChatMessage context)
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
                var service = AIServiceFactory.Create(_config);
                var projContext = ProjectContext.Collect();
                var systemPrompt = PromptBuilder.BuildIntentRouteSystemPrompt(projContext);
                var userPrompt = PromptBuilder.BuildIntentRouteUserPrompt(context.Content);
                var memory = BuildChatMemory(context.Content);
                var response = await AIRequestRetry.SendWithRetryAsync(service, _config, systemPrompt, userPrompt, memory);
                LogAiExchange("意图路由", response, $"memoryTurns={memory?.Count ?? 0}");

                if (!response.Success)
                {
                    context.ErrorMessage = response.Error ?? "AI 返回了未知错误";
                    context.Type = MessageTypeEnum.Error;
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                var intent = ResponseParser.ParseGenerationIntent(response.Content);
                if (!intent.Success)
                {
                    context.ErrorMessage = intent.Error ?? "无法解析 AI 判断结果";
                    if (!string.IsNullOrEmpty(intent.RawJson))
                        context.ErrorMessage += $"\n\nAI 原始输出:\n{intent.RawJson}";
                    context.Type = MessageTypeEnum.Error;
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                var resolvedMode = MapRouteToMode(intent.Route, intent.CombinedPrefabFirst);
                context.Mode = resolvedMode;
                context.CombinedPrefabFirst = intent.CombinedPrefabFirst;
                context.CodeType = intent.CodeType;

                _chatHistory.RemoveAll(m => m.Role == ChatRole.Assistant && m.Type == MessageTypeEnum.Text && m.Content.StartsWith("⏳"));

                var label = ModeDecisionLabel(resolvedMode);
                var codeHint = resolvedMode != GenerateMode.Prefab && resolvedMode != GenerateMode.SceneOps &&
                               resolvedMode != GenerateMode.ProjectQuery && resolvedMode != GenerateMode.AssetDelete &&
                               resolvedMode != GenerateMode.AssetOps
                    ? $"（代码类型：{PromptBuilder.CodeTypeLabels[(int)intent.CodeType]}）"
                    : "";
                if (resolvedMode == GenerateMode.CombinedPrefabFirst)
                    codeHint += "（顺序：先预制体 → 再脚本）";
                AddTextBubble($"根据需求判断为：<b>{label}</b> {codeHint}");

                switch (resolvedMode)
                {
                    case GenerateMode.Code:
                        AddTextBubble("⏳ 正在生成代码，请稍候...");
                        GenerateCodeAsync(context);
                        break;
                    case GenerateMode.Prefab:
                        AddTextBubble("⏳ 正在生成预制体，请稍候...");
                        GeneratePrefabAsync(context);
                        break;
                    case GenerateMode.Combined:
                        AddTextBubble("⏳ 联合生成 (第1步): 正在生成代码，请稍候...");
                        GenerateCodeAsync(context);
                        break;
                    case GenerateMode.CombinedPrefabFirst:
                        AddTextBubble("⏳ 联合生成 (第1步): 正在生成预制体，请稍候...");
                        GeneratePrefabAsync(context);
                        break;
                    case GenerateMode.SceneOps:
                        AddTextBubble("⏳ 正在生成场景操控列表，请稍候...");
                        GenerateSceneOpsAsync(context);
                        break;
                    case GenerateMode.ProjectQuery:
                        AddTextBubble("⏳ 正在根据项目上下文回答，请稍候...");
                        ProjectQueryAsync(context);
                        break;
                    case GenerateMode.AssetDelete:
                        AddTextBubble("⏳ 正在解析待删除资源列表，请稍候...");
                        AssetDeleteAsync(context);
                        break;
                    case GenerateMode.AssetOps:
                        AddTextBubble("⏳ 正在生成资源整理步骤，请稍候...");
                        GenerateAssetOpsAsync(context);
                        break;
                    default:
                        context.ErrorMessage = $"内部错误：未知解析模式 {resolvedMode}";
                        context.Type = MessageTypeEnum.Error;
                        _isGenerating = false;
                        AddResultBubble(context);
                        break;
                }
            }
            catch (Exception ex)
            {
                AiExchangeDebugLog.AppendException("意图路由", ex);
                context.ErrorMessage = $"判断需求类型时出错: {ex.Message}";
                context.Type = MessageTypeEnum.Error;
                _isGenerating = false;
                AddResultBubble(context);
            }
            finally
            {
                Repaint();
            }
        }

        private async void GenerateCodeAsync(ChatMessage context)
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
                var service = AIServiceFactory.Create(_config);
                var projContext = ProjectContext.Collect();

                var systemPrompt = PromptBuilder.BuildCodeSystemPrompt(projContext, context.CodeType);
                var userPrompt = PromptBuilder.BuildCodeUserPrompt(context.Content);
                var memory = BuildChatMemory(context.Content);
                var response = await AIRequestRetry.SendWithRetryAsync(service, _config, systemPrompt, userPrompt, memory);
                LogAiExchange("生成代码", response, $"CodeType={context.CodeType}, memoryTurns={memory?.Count ?? 0}");

                if (!response.Success)
                {
                    context.ErrorMessage = response.Error ?? "AI 返回了未知错误";
                    context.Type = MessageTypeEnum.Error;
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                var parseResult = ResponseParser.ParseCodeResponse(response.Content);

                if (!parseResult.Success)
                {
                    context.ErrorMessage = parseResult.Error ?? "无法解析 AI 响应";
                    if (!string.IsNullOrEmpty(parseResult.Code))
                        context.ErrorMessage += $"\n\nAI 原始输出:\n{parseResult.Code}";
                    context.Type = MessageTypeEnum.Error;
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                context.GeneratedCode = parseResult.Code;
                context.ScriptName = parseResult.ScriptName;
                if (context.Mode == GenerateMode.CombinedPrefabFirst)
                {
                    context.CodeGenerationTime = response.Duration;
                    context.CodeTokensUsed = response.TokensUsed;
                    context.GenerationTime = _combinedPrefabGenTime;
                    context.TokensUsed = _combinedPrefabTokens;
                }
                else
                {
                    context.GenerationTime = response.Duration;
                    context.TokensUsed = response.TokensUsed;
                }

                context.Type = MessageTypeEnum.CodeGenerated;
                AddResultBubble(context);
            }
            catch (Exception ex)
            {
                AiExchangeDebugLog.AppendException("生成代码", ex);
                context.ErrorMessage = $"生成过程中出错: {ex.Message}";
                context.Type = MessageTypeEnum.Error;
                _isGenerating = false;
                AddResultBubble(context);
            }
            finally
            {
                Repaint();
            }
        }

        private async void GeneratePrefabAsync(ChatMessage context)
        {
            if (_config == null)
            {
                context.ErrorMessage = "AI 服务未配置。";
                context.Type = MessageTypeEnum.Error;
                _isGenerating = false;
                AddResultBubble(context);
                return;
            }

            try
            {
                var service = AIServiceFactory.Create(_config);
                var projContext = ProjectContext.Collect();

                string systemPrompt = PromptBuilder.BuildPrefabSystemPrompt(projContext);
                string userPrompt = context.Mode == GenerateMode.Combined
                    ? PromptBuilder.BuildCombinedPrefabUserPrompt(context.Content, context.ScriptName)
                    : PromptBuilder.BuildPrefabUserPrompt(context.Content);

                var memory = BuildChatMemory(context.Content);
                var response = await AIRequestRetry.SendWithRetryAsync(service, _config, systemPrompt, userPrompt, memory);
                LogAiExchange(
                    "生成预制体",
                    response,
                    $"Mode={context.Mode}, ScriptName={context.ScriptName ?? "-"}, memoryTurns={memory?.Count ?? 0}");

                if (!response.Success)
                {
                    context.ErrorMessage = response.Error ?? "AI 返回了未知错误";
                    context.Type = MessageTypeEnum.Error;
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                var parseResult = ResponseParser.ParsePrefabResponse(response.Content);

                if (!parseResult.Success)
                {
                    context.ErrorMessage = parseResult.Error ?? "无法解析预制体 JSON";
                    if (!string.IsNullOrEmpty(parseResult.RawJson))
                        context.ErrorMessage += $"\n\nAI 原始输出:\n{parseResult.RawJson}";
                    context.Type = MessageTypeEnum.Error;
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                context.PrefabDescription = parseResult.Description;
                context.PrefabName = parseResult.Description!.prefabName;
                context.RawJson = parseResult.RawJson;
                context.GenerationTime = response.Duration;
                context.TokensUsed = response.TokensUsed;
                context.Type = MessageTypeEnum.PrefabGenerated;
                
                AddResultBubble(context);
            }
            catch (Exception ex)
            {
                AiExchangeDebugLog.AppendException("生成预制体", ex);
                context.ErrorMessage = $"生成过程中出错: {ex.Message}";
                context.Type = MessageTypeEnum.Error;
                _isGenerating = false;
                AddResultBubble(context);
            }
            finally
            {
                Repaint();
            }
        }

        private async void GenerateSceneOpsAsync(ChatMessage context)
        {
            if (_config == null)
            {
                context.ErrorMessage = "AI 服务未配置。";
                context.Type = MessageTypeEnum.Error;
                _isGenerating = false;
                AddResultBubble(context);
                return;
            }

            try
            {
                var service = AIServiceFactory.Create(_config);
                var projContext = ProjectContext.Collect();
                var systemPrompt = PromptBuilder.BuildSceneOpsSystemPrompt(projContext);
                var userPrompt = PromptBuilder.BuildSceneOpsUserPrompt(
                    context.Content,
                    PromptBuilder.GetActiveSceneNameForPrompt(),
                    appendProjectBrief: false);

                var memory = BuildChatMemory(context.Content);
                var response = await AIRequestRetry.SendWithRetryAsync(service, _config, systemPrompt, userPrompt, memory);
                LogAiExchange("场景操控 JSON", response, $"memoryTurns={memory?.Count ?? 0}");

                if (!response.Success)
                {
                    context.ErrorMessage = response.Error ?? "AI 返回了未知错误";
                    context.Type = MessageTypeEnum.Error;
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                var parse = SceneOpsParser.Parse(response.Content);
                if (!parse.Success || parse.Envelope == null)
                {
                    context.ErrorMessage = parse.Error ?? "无法解析 unity-ops JSON";
                    if (!string.IsNullOrEmpty(response.Content))
                        context.ErrorMessage += $"\n\nAI 原始输出:\n{response.Content}";
                    context.Type = MessageTypeEnum.Error;
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                context.SceneOpsEnvelope = parse.Envelope;
                context.RawJson = parse.RawJson;
                context.GenerationTime = response.Duration;
                context.TokensUsed = response.TokensUsed;
                context.Type = MessageTypeEnum.SceneOpsReady;
                _isGenerating = false;
                AddResultBubble(context);
            }
            catch (Exception ex)
            {
                AiExchangeDebugLog.AppendException("场景操控 JSON", ex);
                context.ErrorMessage = $"生成场景操控列表时出错: {ex.Message}";
                context.Type = MessageTypeEnum.Error;
                _isGenerating = false;
                AddResultBubble(context);
            }
            finally
            {
                Repaint();
            }
        }

        private async void GenerateAssetOpsAsync(ChatMessage context)
        {
            if (_config == null)
            {
                context.ErrorMessage = "AI 服务未配置。";
                context.Type = MessageTypeEnum.Error;
                _isGenerating = false;
                AddResultBubble(context);
                return;
            }

            try
            {
                var service = AIServiceFactory.Create(_config);
                var projContext = ProjectContext.Collect();
                var systemPrompt = PromptBuilder.BuildAssetOpsSystemPrompt(projContext);
                var userPrompt = PromptBuilder.BuildAssetOpsUserPrompt(context.Content);
                var memory = BuildChatMemory(context.Content);
                var response = await AIRequestRetry.SendWithRetryAsync(service, _config, systemPrompt, userPrompt, memory);
                LogAiExchange("资源整理 JSON", response, $"memoryTurns={memory?.Count ?? 0}");

                if (!response.Success)
                {
                    context.ErrorMessage = response.Error ?? "AI 返回了未知错误";
                    context.Type = MessageTypeEnum.Error;
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                var parse = AssetOpsParser.Parse(response.Content ?? "");
                if (!parse.Success || parse.Envelope == null)
                {
                    context.ErrorMessage = parse.Error ?? "无法解析 asset-ops JSON";
                    if (!string.IsNullOrEmpty(response.Content))
                        context.ErrorMessage += $"\n\nAI 原始输出:\n{response.Content}";
                    context.Type = MessageTypeEnum.Error;
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                context.AssetOpsEnvelope = parse.Envelope;
                context.RawJson = parse.RawJson;
                context.GenerationTime = response.Duration;
                context.TokensUsed = response.TokensUsed;
                context.Type = MessageTypeEnum.AssetOpsReady;
                context.Content = "将按顺序执行下列资源操作（可在 Project 中 Ctrl+Z 尝试撤销部分步骤）：";
                _isGenerating = false;
                AddResultBubble(context);
            }
            catch (Exception ex)
            {
                AiExchangeDebugLog.AppendException("资源整理 JSON", ex);
                context.ErrorMessage = $"生成资源整理步骤时出错: {ex.Message}";
                context.Type = MessageTypeEnum.Error;
                _isGenerating = false;
                AddResultBubble(context);
            }
            finally
            {
                Repaint();
            }
        }

        private async void ProjectQueryAsync(ChatMessage context)
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
                var service = AIServiceFactory.Create(_config);
                var projContext = ProjectContext.Collect();
                var systemPrompt = PromptBuilder.BuildProjectQuerySystemPrompt(projContext);
                var userPrompt = context.Content;
                var memory = BuildChatMemory(context.Content);
                var response = await AIRequestRetry.SendWithRetryAsync(service, _config, systemPrompt, userPrompt, memory);
                LogAiExchange("项目查询", response, $"memoryTurns={memory?.Count ?? 0}");

                if (!response.Success)
                {
                    context.ErrorMessage = response.Error ?? "AI 返回了未知错误";
                    context.Type = MessageTypeEnum.Error;
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                var text = ResponseParser.StripThinkBlocks(response.Content ?? "");
                context.Type = MessageTypeEnum.Text;
                context.Content = string.IsNullOrWhiteSpace(text) ? "（无正文）" : text.Trim();
                context.GenerationTime = response.Duration;
                context.TokensUsed = response.TokensUsed;
                _isGenerating = false;
                AddResultBubble(context);
            }
            catch (Exception ex)
            {
                AiExchangeDebugLog.AppendException("项目查询", ex);
                context.ErrorMessage = $"项目查询时出错: {ex.Message}";
                context.Type = MessageTypeEnum.Error;
                _isGenerating = false;
                AddResultBubble(context);
            }
            finally
            {
                Repaint();
            }
        }

        private async void AssetDeleteAsync(ChatMessage context)
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
                var service = AIServiceFactory.Create(_config);
                var projContext = ProjectContext.Collect();
                var systemPrompt = PromptBuilder.BuildAssetDeleteSystemPrompt(projContext);
                var userPrompt = PromptBuilder.BuildAssetDeleteUserPrompt(context.Content);
                var memory = BuildChatMemory(context.Content);
                var response = await AIRequestRetry.SendWithRetryAsync(service, _config, systemPrompt, userPrompt, memory);
                LogAiExchange("删除预制体", response, $"memoryTurns={memory?.Count ?? 0}");

                if (!response.Success)
                {
                    context.ErrorMessage = response.Error ?? "AI 返回了未知错误";
                    context.Type = MessageTypeEnum.Error;
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                var parse = AssetDeleteParser.Parse(response.Content ?? "");
                if (!parse.Success)
                {
                    context.ErrorMessage = parse.Error ?? "无法解析删除列表";
                    context.Type = MessageTypeEnum.Error;
                    if (!string.IsNullOrEmpty(response.Content))
                        context.ErrorMessage += $"\n\nAI 原始输出:\n{response.Content}";
                    _isGenerating = false;
                    AddResultBubble(context);
                    return;
                }

                context.AssetDeletePaths = new List<string>(parse.NormalizedPaths);
                context.RawJson = parse.RawJson;
                var note = parse.Envelope?.note ?? "";
                context.Content = string.IsNullOrWhiteSpace(note)
                    ? "以下资源将被删除（请确认）："
                    : note.Trim();
                context.GenerationTime = response.Duration;
                context.TokensUsed = response.TokensUsed;
                context.Type = MessageTypeEnum.AssetDeleteReady;
                _isGenerating = false;
                AddResultBubble(context);
            }
            catch (Exception ex)
            {
                AiExchangeDebugLog.AppendException("删除预制体", ex);
                context.ErrorMessage = $"删除预制体解析时出错: {ex.Message}";
                context.Type = MessageTypeEnum.Error;
                _isGenerating = false;
                AddResultBubble(context);
            }
            finally
            {
                Repaint();
            }
        }

        private void ExecuteSceneOps(ChatMessage msg)
        {
            if (msg.SceneOpsEnvelope == null)
            {
                RemoveInlineAction(msg, InlineActionKeys.SceneOpsExecute);
                PersistChatHistory();
                EditorUtility.DisplayDialog("场景操控", "内部错误：未找到已解析的操作列表。", "确定");
                Repaint();
                return;
            }

            if (!SceneOpsPreflight.TryValidateSelectionPlaceholder(msg.SceneOpsEnvelope, out var preflightMsg))
            {
                RemoveInlineAction(msg, InlineActionKeys.SceneOpsExecute);
                PersistChatHistory();
                EditorUtility.DisplayDialog("场景操控无法执行", preflightMsg, "确定");
                Repaint();
                return;
            }

            var workspace = SceneWorkspaceSettings.LoadFromEditorPrefs();
            if (!workspace.Enforce)
            {
                RunSceneOpsBatchDirect(msg);
                return;
            }

            var envelope = msg.SceneOpsEnvelope;
            var ops = envelope.operations;
            if (ops == null || ops.Length == 0)
            {
                RemoveInlineAction(msg, InlineActionKeys.SceneOpsExecute);
                PersistChatHistory();
                EditorUtility.DisplayDialog("场景操控", "operations 为空。", "确定");
                Repaint();
                return;
            }

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var done = 0;
            var skipped = 0;

            for (var i = 0; i < ops.Length; i++)
            {
                var op = ops[i];
                if (!SceneWorkspaceEvaluator.IsWithinWorkspace(op, scene, workspace, out var why))
                {
                    var head = $"第 {i + 1}/{ops.Length} 步 — 超出工作区或需您确认\n\n" +
                               SceneWorkspaceEvaluator.DescribeOperation(op) + "\n\n" + why;
                    var choice = EditorUtility.DisplayDialogComplex(
                        "工作区确认",
                        head,
                        "执行此项",
                        "中止整批",
                        "跳过此项");

                    if (choice == 1)
                    {
                        RemoveInlineAction(msg, InlineActionKeys.SceneOpsExecute);
                        PersistChatHistory();
                        EditorUtility.DisplayDialog(
                            "已中止",
                            $"已执行 {done} 步，跳过 {skipped} 步，已中止（后续未执行）。已做的修改可通过 Ctrl+Z 撤销。",
                            "确定");
                        Repaint();
                        return;
                    }

                    if (choice == 2)
                    {
                        skipped++;
                        continue;
                    }
                }

                var stepResult = MainThread.IsMainThread
                    ? SceneOpsExecutor.ExecuteStep(op)
                    : MainThread.Run(() => SceneOpsExecutor.ExecuteStep(op));
                if (!stepResult.Success)
                {
                    RemoveInlineAction(msg, InlineActionKeys.SceneOpsExecute);
                    PersistChatHistory();
                    EditorUtility.DisplayDialog(
                        "场景操控执行失败",
                        $"第 {i + 1} 步失败\n\n{stepResult.Error}\n\n已在此之前成功执行 {done} 步，跳过 {skipped} 步。",
                        "确定");
                    Repaint();
                    return;
                }

                done++;
            }

            msg.SceneOpsExecutedStepCount = done;
            msg.SceneOpsSkippedStepCount = skipped;
            msg.Type = MessageTypeEnum.SuccessResult;
            _isGenerating = false;
            Repaint();
            ScrollToBottom();

            if (done == 0 && skipped > 0)
            {
                EditorUtility.DisplayDialog(
                    "场景操控",
                    "所有步骤均已跳过，场景未改动。",
                    "确定");
            }
        }

        private void RunSceneOpsBatchDirect(ChatMessage msg)
        {
            var batch = MainThread.IsMainThread
                ? SceneOpsExecutor.Execute(msg.SceneOpsEnvelope!)
                : MainThread.Run(() => SceneOpsExecutor.Execute(msg.SceneOpsEnvelope!));
            if (batch.Success)
            {
                msg.SceneOpsExecutedStepCount = batch.StepsCompleted;
                msg.SceneOpsSkippedStepCount = 0;
                msg.Type = MessageTypeEnum.SuccessResult;
                _isGenerating = false;
                Repaint();
                ScrollToBottom();
                return;
            }

            var detail = batch.Error ?? "未知错误";
            RemoveInlineAction(msg, InlineActionKeys.SceneOpsExecute);
            PersistChatHistory();
            EditorUtility.DisplayDialog(
                "场景操控执行失败",
                $"第 {batch.FailedAtIndex + 1} 步失败（0-based 下标 {batch.FailedAtIndex}）\n\n{detail}\n\n可修正场景或 Hierarchy 后再次点击「执行场景操作」，或使用「重试此任务」重新问 AI。",
                "确定");
            Repaint();
        }

        #endregion
    }
}
