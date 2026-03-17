#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityMCP.AI;
using UnityMCP.Core;
using UnityMCP.Generators;

namespace UnityMCP.UI
{
    /// <summary>
    /// 聊天消息角色
    /// </summary>
    public enum ChatRole
    {
        User,
        Assistant
    }

    /// <summary>
    /// 消息类型，用于指导气泡内的 UI 绘制
    /// </summary>
    public enum MessageTypeEnum
    {
        Text,               // 纯文本消息（普通回复或等待提示）
        CodeGenerated,      // 代码生成完毕，等待用户保存
        WaitingCompile,     // 等待编译中
        PrefabGenerated,    // 预制体生成完毕，等待用户保存
        SuccessResult,      // 最终成功结果展示
        Error               // 错误展示
    }

    /// <summary>
    /// 生成模式
    /// </summary>
    public enum GenerateMode
    {
        Code = 0,
        Prefab = 1,
        Combined = 2
    }

    /// <summary>
    /// 单条聊天消息的数据结构
    /// </summary>
    public class ChatMessage
    {
        public ChatRole Role;
        public MessageTypeEnum Type = MessageTypeEnum.Text;
        public string Content = "";

        // 所属任务的状态关联
        public GenerateMode Mode;
        public CodeType CodeType;

        // 生成结果数据
        public string ErrorMessage = "";
        
        public string GeneratedCode = "";
        public string ScriptName = "";
        
        public PrefabDescription? PrefabDescription;
        public string PrefabName = "";
        public string RawJson = "";
        public List<string> PrefabWarnings = new();

        public string SavedScriptPath = "";
        public string SavedPrefabPath = "";

        // 进度/耗时统计
        public float GenerationTime;
        public int TokensUsed;
        public float CodeGenerationTime;  // 联合模式步骤1耗时
        public int CodeTokensUsed;        // 联合模式步骤1 Token

        public int CompileWaitTicks;

        // 快捷创建文本消息
        public static ChatMessage CreateText(ChatRole role, string text) => new()
        {
            Role = role,
            Type = MessageTypeEnum.Text,
            Content = text
        };
    }

    /// <summary>
    /// AI 快捷命令窗口（对话式 UI）。
    /// </summary>
    public class AIQuickCommand : EditorWindow
    {
        private static readonly string[] MODE_LABELS = { "生成代码", "生成预制体", "联合生成" };

        #region Fields

        private GenerateMode _currentMode = GenerateMode.Code;
        private CodeType _currentCodeType = CodeType.Auto;
        private string _userInput = "";

        private List<ChatMessage> _chatHistory = new();
        private Vector2 _chatScrollPos;
        private AIServiceConfig? _config;
        
        // 运行状态：是否有后台任务正在执行
        private bool _isGenerating;
        private ChatMessage? _pendingMessage; // 当前正在处理的上下文

        // 编译回调相关
        private bool _compilationDetected;
        // 保存联合生成中步骤1耗时信息，用于最后汇总展示
        private float _combinedCodeGenTime;
        private int _combinedCodeTokens;

        // UI 样式缓存
        private GUIStyle? _userBubbleStyle;
        private GUIStyle? _assistantBubbleStyle;

        #endregion

        #region 窗口管理

        [MenuItem("Window/AI 助手/快捷生成 %#.", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<AIQuickCommand>(utility: true);
            window.titleContent = new GUIContent("AI 快捷生成");
            window.minSize = new Vector2(600, 500);
            window.maxSize = new Vector2(900, 800);
            window.ResetAll();
            window.ShowUtility();
            window.Focus();
        }

        private void OnEnable()
        {
            _config = AIServiceConfig.Load();
            ResetAll();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnCompileWaitUpdate;
        }

        private void ResetAll()
        {
            _userInput = "";
            _chatHistory.Clear();
            _isGenerating = false;
            _pendingMessage = null;
            _compilationDetected = false;
            EditorApplication.update -= OnCompileWaitUpdate;
        }

        #endregion

        #region UI 绘制

        private void InitStyles()
        {
            if (_userBubbleStyle == null)
            {
                _userBubbleStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    wordWrap = true,
                    richText = true,
                    fontSize = 13,
                    padding = new RectOffset(10, 10, 10, 10),
                    margin = new RectOffset(40, 10, 5, 5) // 靠右
                };
            }

            if (_assistantBubbleStyle == null)
            {
                _assistantBubbleStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    wordWrap = true,
                    richText = true,
                    fontSize = 13,
                    padding = new RectOffset(10, 10, 10, 10),
                    margin = new RectOffset(10, 40, 5, 5) // 靠左
                };
            }
        }

        private void OnGUI()
        {
            InitStyles();

            EditorGUILayout.Space(5);
            DrawToolbar();
            EditorGUILayout.Space(5);

            DrawChatHistory();

            EditorGUILayout.Space(5);
            DrawInputArea();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            EditorGUILayout.LabelField("模式:", GUILayout.Width(40));
            _currentMode = (GenerateMode)EditorGUILayout.Popup((int)_currentMode, MODE_LABELS, EditorStyles.toolbarPopup, GUILayout.Width(100));

            if (_currentMode == GenerateMode.Code || _currentMode == GenerateMode.Combined)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("代码类型:", GUILayout.Width(60));
                _currentCodeType = (CodeType)EditorGUILayout.Popup((int)_currentCodeType, PromptBuilder.CodeTypeLabels, EditorStyles.toolbarPopup, GUILayout.Width(120));
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("清空历史", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                ResetAll();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawChatHistory()
        {
            _chatScrollPos = EditorGUILayout.BeginScrollView(_chatScrollPos, GUILayout.ExpandHeight(true));

            if (_chatHistory.Count == 0)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("在下方输入需求以开始生成", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
            }
            else
            {
                // 这里需要复制一个列表遍历，因为界面绘制中可能有操作修改 _chatHistory
                var messages = new List<ChatMessage>(_chatHistory);
                foreach (var msg in messages)
                {
                    if (msg.Role == ChatRole.User)
                    {
                        DrawUserMessage(msg);
                    }
                    else
                    {
                        DrawAssistantMessage(msg);
                    }
                    EditorGUILayout.Space(5);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawUserMessage(ChatMessage msg)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(_userBubbleStyle!, GUILayout.MinWidth(100));
            EditorGUILayout.LabelField($"<b>用户:</b>", _userBubbleStyle!);
            EditorGUILayout.LabelField(msg.Content, _userBubbleStyle!);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAssistantMessage(ChatMessage msg)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(_assistantBubbleStyle!, GUILayout.MinWidth(200));
            EditorGUILayout.LabelField($"<b>AI 助手:</b>", _assistantBubbleStyle!);

            switch (msg.Type)
            {
                case MessageTypeEnum.Text:
                    EditorGUILayout.LabelField(msg.Content, _assistantBubbleStyle!);
                    if (msg.Content.Contains("⏳")) Repaint(); // 如果是等待中，刷新UI
                    break;
                case MessageTypeEnum.CodeGenerated:
                    DrawCodeGeneratedState(msg);
                    break;
                case MessageTypeEnum.WaitingCompile:
                    DrawWaitingCompileState(msg);
                    break;
                case MessageTypeEnum.PrefabGenerated:
                    DrawPrefabGeneratedState(msg);
                    break;
                case MessageTypeEnum.SuccessResult:
                    DrawSuccessState(msg);
                    break;
                case MessageTypeEnum.Error:
                    DrawErrorState(msg);
                    break;
            }

            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInputArea()
        {
            EditorGUILayout.BeginHorizontal();

            string hint = _currentMode switch
            {
                GenerateMode.Code => "描述你需要的脚本，如：创建一个包含WASD移动的Player脚本",
                GenerateMode.Prefab => "描述你需要的预制体，如：创建一个包含碰撞体的玩家预制体",
                GenerateMode.Combined => "描述需要的功能，如：创建一个可拾取的道具(包含脚本和预制体)",
                _ => ""
            };

            GUI.SetNextControlName("ChatInput");
            _userInput = EditorGUILayout.TextArea(_userInput, GUILayout.Height(60), GUILayout.ExpandWidth(true));

            if (string.IsNullOrEmpty(_userInput))
            {
                var rect = GUILayoutUtility.GetLastRect();
                rect.x += 4;
                rect.y += 2;
                GUI.Label(rect, hint, EditorStyles.centeredGreyMiniLabel);
            }

            bool canSend = !string.IsNullOrWhiteSpace(_userInput) && !_isGenerating;
            GUI.enabled = canSend;

            if (GUILayout.Button("发送\n(Ctrl+Enter)", GUILayout.Width(90), GUILayout.Height(60)))
            {
                StartNewTask();
            }

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && Event.current.control && canSend)
            {
                StartNewTask();
                Event.current.Use();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 消息内部状态绘制

        private void DrawCodeGeneratedState(ChatMessage msg)
        {
            string title = msg.Mode == GenerateMode.Combined ? "✅ <b>第 1 步完成</b>: 代码已生成！" : "✅ 代码已生成！";
            EditorGUILayout.LabelField(title, _assistantBubbleStyle!);
            EditorGUILayout.LabelField($"耗时: {msg.GenerationTime:F1}秒 | Token: {msg.TokensUsed}", EditorStyles.miniLabel);
            
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("脚本名称:", GUILayout.Width(60));
            msg.ScriptName = EditorGUILayout.TextField(msg.ScriptName);
            EditorGUILayout.LabelField(".cs", GUILayout.Width(25));
            EditorGUILayout.EndHorizontal();

            if (ScriptGenerator.ScriptExists(msg.ScriptName))
            {
                EditorGUILayout.HelpBox($"文件 {msg.ScriptName}.cs 已存在，保存将覆盖", MessageType.Warning);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("预览代码", GUILayout.Height(25)))
            {
                PreviewWindow.ShowWindow($"{msg.ScriptName}.cs 预览", msg.GeneratedCode);
            }
            if (msg.Mode == GenerateMode.Combined)
            {
                if (GUILayout.Button("保存并继续生成预制体", GUILayout.Height(25)))
                {
                    SaveCodeAndContinueCombined(msg);
                }
            }
            else
            {
                if (GUILayout.Button("保存文件", GUILayout.Height(25)))
                {
                    SaveScript(msg);
                }
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPrefabGeneratedState(ChatMessage msg)
        {
            string title = msg.Mode == GenerateMode.Combined ? "✅ <b>第 2 步完成</b>: 预制体已生成！" : "✅ 预制体已生成！";
            EditorGUILayout.LabelField(title, _assistantBubbleStyle!);
            EditorGUILayout.LabelField($"耗时: {msg.GenerationTime:F1}秒 | Token: {msg.TokensUsed}", EditorStyles.miniLabel);

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("预制体名称:", GUILayout.Width(70));
            msg.PrefabName = EditorGUILayout.TextField(msg.PrefabName);
            EditorGUILayout.LabelField(".prefab", GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            if (PrefabGenerator.PrefabExists(msg.PrefabName))
                EditorGUILayout.HelpBox($"预制体 {msg.PrefabName}.prefab 已存在，保存将覆盖", MessageType.Warning);

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("预览 JSON", GUILayout.Height(25)))
            {
                PreviewWindow.ShowWindow($"{msg.PrefabName}.prefab JSON 预览", msg.RawJson);
            }
            if (GUILayout.Button("创建预制体", GUILayout.Height(25)))
            {
                SavePrefab(msg);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawWaitingCompileState(ChatMessage msg)
        {
            EditorGUILayout.LabelField($"✓ 脚本已保存: {msg.SavedScriptPath}");
            
            var dots = new string('.', (msg.CompileWaitTicks / 10 % 4) + 1);
            var waitSeconds = msg.CompileWaitTicks * 0.1f;
            
            EditorGUILayout.LabelField($"⟳ 等待 Unity 编译完成{dots} ({waitSeconds:F1}秒)");
            
            EditorGUILayout.Space(5);
            if (GUILayout.Button("取消联合生成", GUILayout.Width(150), GUILayout.Height(25)))
            {
                // 取消后，该消息变更为成功（仅脚本）
                msg.Type = MessageTypeEnum.SuccessResult;
                _isGenerating = false;
                EditorApplication.update -= OnCompileWaitUpdate;
                _pendingMessage = null;
                ScrollToBottom();
            }
            Repaint();
        }

        private void DrawSuccessState(ChatMessage msg)
        {
            string text = msg.Mode == GenerateMode.Combined && !string.IsNullOrEmpty(msg.SavedPrefabPath) 
                ? "🎉 联合生成最终完成！" 
                : (msg.Mode == GenerateMode.Code ? "🎉 代码生成并保存成功！" : "🎉 预制体生成并保存成功！");
                
            EditorGUILayout.LabelField($"<b>{text}</b>", _assistantBubbleStyle!);
            
            EditorGUILayout.Space(5);

            if (!string.IsNullOrEmpty(msg.SavedScriptPath))
                EditorGUILayout.LabelField($"已生成脚本: {msg.SavedScriptPath}", EditorStyles.miniLabel);
            
            if (!string.IsNullOrEmpty(msg.SavedPrefabPath))
                EditorGUILayout.LabelField($"已生成预制体: {msg.SavedPrefabPath}", EditorStyles.miniLabel);

            if (msg.PrefabWarnings.Count > 0)
            {
                EditorGUILayout.Space(2);
                foreach (var w in msg.PrefabWarnings)
                    EditorGUILayout.HelpBox(w, MessageType.Warning);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            
            if (!string.IsNullOrEmpty(msg.SavedScriptPath) && GUILayout.Button("打开脚本", GUILayout.Height(25)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(msg.SavedScriptPath);
                if (asset != null) AssetDatabase.OpenAsset(asset);
            }
            
            if (!string.IsNullOrEmpty(msg.SavedPrefabPath) && GUILayout.Button("选中预制体", GUILayout.Height(25)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(msg.SavedPrefabPath);
                if (asset != null)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawErrorState(ChatMessage msg)
        {
            EditorGUILayout.LabelField("❌ <b>生成失败</b>", _assistantBubbleStyle!);
            EditorGUILayout.HelpBox(msg.ErrorMessage, MessageType.Error);
            
            EditorGUILayout.Space(5);
            if (GUILayout.Button("重试此任务", GUILayout.Width(100), GUILayout.Height(25)))
            {
                RetryTask(msg);
            }
        }

        #endregion

        #region 聊天与生成核心逻辑

        private void StartNewTask()
        {
            if (string.IsNullOrWhiteSpace(_userInput)) return;

            var userText = _userInput;
            _userInput = "";
            GUI.FocusControl(null); 

            // 用户输入气泡
            _chatHistory.Add(ChatMessage.CreateText(ChatRole.User, userText));
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
            }
            ScrollToBottom();
        }

        private void AddTextBubble(string text)
        {
            // 移除历史消息中仍是"正在生成"的纯文本气泡（为了UI整洁）
            _chatHistory.RemoveAll(m => m.Role == ChatRole.Assistant && m.Type == MessageTypeEnum.Text && m.Content.StartsWith("⏳"));
            
            var msg = ChatMessage.CreateText(ChatRole.Assistant, text);
            _chatHistory.Add(msg);
            ScrollToBottom();
        }

        private void AddResultBubble(ChatMessage msg)
        {
            // 同样移除等待中的文本气泡
            _chatHistory.RemoveAll(m => m.Role == ChatRole.Assistant && m.Type == MessageTypeEnum.Text && m.Content.StartsWith("⏳"));
            _chatHistory.Add(msg);
            ScrollToBottom();
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

                var response = await service.SendMessageAsync(systemPrompt, userPrompt);

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
                context.GenerationTime = response.Duration;
                context.TokensUsed = response.TokensUsed;
                
                context.Type = MessageTypeEnum.CodeGenerated;
                AddResultBubble(context);
            }
            catch (Exception ex)
            {
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

                var response = await service.SendMessageAsync(systemPrompt, userPrompt);

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

        #endregion

        #region 操作按钮处理

        private void SaveScript(ChatMessage msg)
        {
            if (string.IsNullOrEmpty(msg.ScriptName))
            {
                EditorUtility.DisplayDialog("错误", "脚本名称不能为空", "确定");
                return;
            }

            if (ScriptGenerator.ScriptExists(msg.ScriptName))
            {
                if (!EditorUtility.DisplayDialog("文件已存在", $"{msg.ScriptName}.cs 已存在，是否覆盖？", "覆盖", "取消"))
                    return;
            }

            var result = ScriptGenerator.SaveScript(msg.ScriptName, msg.GeneratedCode);
            if (result.Success)
            {
                // 将原气泡改为成功展示
                msg.SavedScriptPath = result.FilePath;
                msg.Type = MessageTypeEnum.SuccessResult;
                _isGenerating = false;
                
                // 为了让用户注意到结果，我们可以新发一条最终结果气泡，原气泡可以直接变成提示或保留
                // 这里我们选择把这个气泡的Type转换为Success，它会自动渲染成最终结果UI
                Repaint();
                ScrollToBottom();
            }
            else
            {
                EditorUtility.DisplayDialog("保存失败", result.Error ?? "未知错误", "确定");
            }
        }

        private void SavePrefab(ChatMessage msg)
        {
            if (msg.PrefabDescription == null || string.IsNullOrEmpty(msg.PrefabName)) return;

            msg.PrefabDescription.prefabName = msg.PrefabName;
            var result = PrefabGenerator.Generate(msg.PrefabDescription);

            if (result.Success)
            {
                msg.SavedPrefabPath = result.AssetPath;
                msg.PrefabWarnings = result.Warnings;
                msg.Type = MessageTypeEnum.SuccessResult;
                
                if (msg.Mode == GenerateMode.Combined)
                {
                    // 把之前存的步骤1耗时信息放回msg，方便展示
                    msg.CodeGenerationTime = _combinedCodeGenTime;
                    msg.CodeTokensUsed = _combinedCodeTokens;
                }

                _isGenerating = false;
                Repaint();
                ScrollToBottom();
            }
            else
            {
                string err = result.Error ?? "未知错误";
                if (result.Warnings.Count > 0) err += "\n" + string.Join("\n", result.Warnings);
                EditorUtility.DisplayDialog("保存失败", err, "确定");
            }
        }

        private void SaveCodeAndContinueCombined(ChatMessage msg)
        {
            if (string.IsNullOrEmpty(msg.ScriptName)) return;

            if (ScriptGenerator.ScriptExists(msg.ScriptName))
            {
                if (!EditorUtility.DisplayDialog("文件已存在", $"{msg.ScriptName}.cs 已存在，是否覆盖？", "覆盖", "取消"))
                    return;
            }

            // 存下步骤1的信息
            _combinedCodeGenTime = msg.GenerationTime;
            _combinedCodeTokens = msg.TokensUsed;

            var result = ScriptGenerator.SaveScript(msg.ScriptName, msg.GeneratedCode);
            if (!result.Success)
            {
                EditorUtility.DisplayDialog("保存失败", result.Error ?? "未知错误", "确定");
                return;
            }

            // 修改原气泡类型为等待编译
            msg.SavedScriptPath = result.FilePath;
            msg.Type = MessageTypeEnum.WaitingCompile;
            msg.CompileWaitTicks = 0;
            _compilationDetected = false;
            
            _pendingMessage = msg;
            EditorApplication.update += OnCompileWaitUpdate;
            Repaint();
            ScrollToBottom();
        }

        private void OnCompileWaitUpdate()
        {
            if (_pendingMessage == null)
            {
                EditorApplication.update -= OnCompileWaitUpdate;
                return;
            }

            _pendingMessage.CompileWaitTicks++;

            if (EditorApplication.isCompiling)
            {
                _compilationDetected = true;
            }
            else if (_compilationDetected || _pendingMessage.CompileWaitTicks > 150)
            {
                EditorApplication.update -= OnCompileWaitUpdate;
                
                // 编译完成后，新建一个气泡提示正在生成预制体
                // 之前等待编译的气泡我们需要固定它的状态，这里可以直接将其移出或者保留一条“脚本已保存”文本
                _pendingMessage.Type = MessageTypeEnum.SuccessResult; // 它变成了一个仅代码的成功节点
                var savedScript = _pendingMessage.SavedScriptPath;
                var scriptName = _pendingMessage.ScriptName;
                var content = _pendingMessage.Content; // 用户的原始输入

                _pendingMessage = new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Mode = GenerateMode.Combined,
                    Content = content,
                    ScriptName = scriptName,
                    SavedScriptPath = savedScript // 传递已保存的脚本路径
                };

                AddTextBubble("⏳ 联合生成 (第2步): 编译完成，正在生成预制体...");
                GeneratePrefabAsync(_pendingMessage);
                return;
            }

            Repaint();
        }

        #endregion

        private void ScrollToBottom()
        {
            _chatScrollPos.y = float.MaxValue;
        }
    }
}
