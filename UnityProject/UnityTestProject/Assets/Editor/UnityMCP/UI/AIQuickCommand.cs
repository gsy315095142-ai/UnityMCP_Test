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
    /// 消息处理状态
    /// </summary>
    public enum MessageState
    {
        Generating,      // 正在生成
        CodeGenerated,   // 代码已生成（联合模式第一步）
        WaitingCompile,  // 等待编译（联合模式）
        Success,         // 生成成功
        Error            // 生成失败
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
        public string Content = "";

        // 仅助理消息使用的状态
        public MessageState State = MessageState.Generating;
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
        public float CodeGenerationTime;
        public int CodeTokensUsed;
        
        public bool IsCombinedPrefabPhase;
        public int CompileWaitTicks;
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
        
        // 编译回调相关
        private ChatMessage? _pendingCompileMessage;
        private bool _compilationDetected;

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
            _pendingCompileMessage = null;
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
                foreach (var msg in _chatHistory)
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

            switch (msg.State)
            {
                case MessageState.Generating:
                    DrawGeneratingState(msg);
                    break;
                case MessageState.CodeGenerated:
                    DrawCodeGeneratedState(msg);
                    break;
                case MessageState.WaitingCompile:
                    DrawWaitingCompileState(msg);
                    break;
                case MessageState.Success:
                    DrawSuccessState(msg);
                    break;
                case MessageState.Error:
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

            // 快捷提示
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

            bool canSend = !string.IsNullOrWhiteSpace(_userInput) && !IsAnyMessageGenerating();
            GUI.enabled = canSend;

            if (GUILayout.Button("发送\n(Ctrl+Enter)", GUILayout.Width(90), GUILayout.Height(60)))
            {
                SendMessage();
            }

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && Event.current.control && canSend)
            {
                SendMessage();
                Event.current.Use();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 消息状态绘制

        private void DrawGeneratingState(ChatMessage msg)
        {
            string text = msg.Mode == GenerateMode.Combined
                ? (msg.IsCombinedPrefabPhase ? "正在生成预制体，请稍候... (第 2 步)" : "正在生成代码，请稍候... (第 1 步)")
                : (msg.Mode == GenerateMode.Code ? "正在生成代码，请稍候..." : "正在生成预制体，请稍候...");
            
            EditorGUILayout.LabelField($"⏳ {text}");
            Repaint();
        }

        private void DrawCodeGeneratedState(ChatMessage msg)
        {
            EditorGUILayout.LabelField("✅ <b>第 1 步完成</b>: 代码已生成！", _assistantBubbleStyle!);
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
            if (GUILayout.Button("保存并继续生成预制体", GUILayout.Height(25)))
            {
                SaveCodeAndContinueCombined(msg);
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
                msg.State = MessageState.Success;
                EditorApplication.update -= OnCompileWaitUpdate;
                _pendingCompileMessage = null;
            }
            Repaint();
        }

        private void DrawSuccessState(ChatMessage msg)
        {
            string text = msg.Mode == GenerateMode.Combined && !string.IsNullOrEmpty(msg.SavedPrefabPath) 
                ? "联合生成完成！" 
                : (msg.Mode == GenerateMode.Code ? "代码生成完成！" : "预制体生成完成！");
                
            EditorGUILayout.LabelField($"✅ <b>{text}</b>", _assistantBubbleStyle!);
            
            if (msg.Mode == GenerateMode.Combined && !string.IsNullOrEmpty(msg.SavedPrefabPath))
            {
                EditorGUILayout.LabelField($"代码耗时: {msg.CodeGenerationTime:F1}秒 ({msg.CodeTokensUsed} token) | 预制体耗时: {msg.GenerationTime:F1}秒 ({msg.TokensUsed} token)", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField($"耗时: {msg.GenerationTime:F1}秒 | Token: {msg.TokensUsed}", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(5);

            // 未保存状态 (仅生成了，还需要用户确认)
            if (msg.Mode == GenerateMode.Code && string.IsNullOrEmpty(msg.SavedScriptPath))
            {
                DrawUnsavedCodeActions(msg);
            }
            else if (msg.Mode == GenerateMode.Prefab && string.IsNullOrEmpty(msg.SavedPrefabPath))
            {
                DrawUnsavedPrefabActions(msg);
            }
            else if (msg.Mode == GenerateMode.Combined && string.IsNullOrEmpty(msg.SavedPrefabPath) && !string.IsNullOrEmpty(msg.SavedScriptPath))
            {
                // 联合模式下第二步预制体已生成但未保存
                DrawUnsavedPrefabActions(msg);
            }
            else
            {
                // 已保存状态
                if (!string.IsNullOrEmpty(msg.SavedScriptPath))
                    EditorGUILayout.LabelField($"脚本: {msg.SavedScriptPath}", EditorStyles.miniLabel);
                
                if (!string.IsNullOrEmpty(msg.SavedPrefabPath))
                    EditorGUILayout.LabelField($"预制体: {msg.SavedPrefabPath}", EditorStyles.miniLabel);

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
        }

        private void DrawUnsavedCodeActions(ChatMessage msg)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("脚本名称:", GUILayout.Width(60));
            msg.ScriptName = EditorGUILayout.TextField(msg.ScriptName);
            EditorGUILayout.LabelField(".cs", GUILayout.Width(25));
            EditorGUILayout.EndHorizontal();

            if (ScriptGenerator.ScriptExists(msg.ScriptName))
                EditorGUILayout.HelpBox($"文件 {msg.ScriptName}.cs 已存在，保存将覆盖", MessageType.Warning);

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("预览", GUILayout.Height(25)))
            {
                PreviewWindow.ShowWindow($"{msg.ScriptName}.cs 预览", msg.GeneratedCode);
            }
            if (GUILayout.Button("保存文件", GUILayout.Height(25)))
            {
                SaveScript(msg);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawUnsavedPrefabActions(ChatMessage msg)
        {
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

        private void DrawErrorState(ChatMessage msg)
        {
            EditorGUILayout.LabelField("❌ <b>生成失败</b>", _assistantBubbleStyle!);
            EditorGUILayout.HelpBox(msg.ErrorMessage, MessageType.Error);
            
            EditorGUILayout.Space(5);
            if (GUILayout.Button("重试", GUILayout.Width(80), GUILayout.Height(25)))
            {
                RetryMessage(msg);
            }
        }

        #endregion

        #region 聊天与生成逻辑

        private bool IsAnyMessageGenerating()
        {
            foreach (var msg in _chatHistory)
            {
                if (msg.Role == ChatRole.Assistant && (msg.State == MessageState.Generating || msg.State == MessageState.WaitingCompile))
                    return true;
            }
            return false;
        }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(_userInput)) return;

            var userMsg = new ChatMessage
            {
                Role = ChatRole.User,
                Content = _userInput
            };
            _chatHistory.Add(userMsg);

            var assistantMsg = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = _userInput, // 保存输入以便重试
                Mode = _currentMode,
                CodeType = _currentCodeType,
                State = MessageState.Generating
            };
            _chatHistory.Add(assistantMsg);

            _userInput = "";
            GUI.FocusControl(null); // 取消输入框焦点
            ScrollToBottom();

            StartGeneration(assistantMsg);
        }

        private void RetryMessage(ChatMessage msg)
        {
            msg.State = MessageState.Generating;
            msg.ErrorMessage = "";
            StartGeneration(msg);
        }

        private void StartGeneration(ChatMessage msg)
        {
            msg.IsCombinedPrefabPhase = false;
            
            switch (msg.Mode)
            {
                case GenerateMode.Code:
                case GenerateMode.Combined:
                    GenerateCode(msg);
                    break;
                case GenerateMode.Prefab:
                    GeneratePrefab(msg);
                    break;
            }
        }

        private async void GenerateCode(ChatMessage msg)
        {
            if (_config == null)
            {
                msg.ErrorMessage = "AI 服务未配置，请先打开设置窗口进行配置。";
                msg.State = MessageState.Error;
                return;
            }

            try
            {
                var service = AIServiceFactory.Create(_config);
                var context = ProjectContext.Collect();

                var systemPrompt = PromptBuilder.BuildCodeSystemPrompt(context, msg.CodeType);
                var userPrompt = PromptBuilder.BuildCodeUserPrompt(msg.Content);

                var response = await service.SendMessageAsync(systemPrompt, userPrompt);

                if (!response.Success)
                {
                    msg.ErrorMessage = response.Error ?? "AI 返回了未知错误";
                    msg.State = MessageState.Error;
                    return;
                }

                var parseResult = ResponseParser.ParseCodeResponse(response.Content);

                if (!parseResult.Success)
                {
                    msg.ErrorMessage = parseResult.Error ?? "无法解析 AI 响应";
                    if (!string.IsNullOrEmpty(parseResult.Code))
                        msg.ErrorMessage += $"\n\nAI 原始输出:\n{parseResult.Code}";
                    msg.State = MessageState.Error;
                    return;
                }

                msg.GeneratedCode = parseResult.Code;
                msg.ScriptName = parseResult.ScriptName;
                msg.GenerationTime = response.Duration;
                msg.TokensUsed = response.TokensUsed;
                
                if (msg.Mode == GenerateMode.Combined)
                    msg.State = MessageState.CodeGenerated;
                else
                    msg.State = MessageState.Success;
            }
            catch (Exception ex)
            {
                msg.ErrorMessage = $"生成过程中出错: {ex.Message}";
                msg.State = MessageState.Error;
            }
            finally
            {
                Repaint();
                ScrollToBottom();
            }
        }

        private async void GeneratePrefab(ChatMessage msg)
        {
            if (_config == null)
            {
                msg.ErrorMessage = "AI 服务未配置，请先打开设置窗口进行配置。";
                msg.State = MessageState.Error;
                return;
            }

            try
            {
                var service = AIServiceFactory.Create(_config);
                var context = ProjectContext.Collect();

                string systemPrompt = PromptBuilder.BuildPrefabSystemPrompt(context);
                string userPrompt = msg.Mode == GenerateMode.Combined 
                    ? PromptBuilder.BuildCombinedPrefabUserPrompt(msg.Content, msg.ScriptName)
                    : PromptBuilder.BuildPrefabUserPrompt(msg.Content);

                var response = await service.SendMessageAsync(systemPrompt, userPrompt);

                if (!response.Success)
                {
                    msg.ErrorMessage = response.Error ?? "AI 返回了未知错误";
                    msg.State = MessageState.Error;
                    return;
                }

                var parseResult = ResponseParser.ParsePrefabResponse(response.Content);

                if (!parseResult.Success)
                {
                    msg.ErrorMessage = parseResult.Error ?? "无法解析预制体 JSON";
                    if (!string.IsNullOrEmpty(parseResult.RawJson))
                        msg.ErrorMessage += $"\n\nAI 原始输出:\n{parseResult.RawJson}";
                    msg.State = MessageState.Error;
                    return;
                }

                msg.PrefabDescription = parseResult.Description;
                msg.PrefabName = parseResult.Description!.prefabName;
                msg.RawJson = parseResult.RawJson;
                msg.GenerationTime = response.Duration;
                msg.TokensUsed = response.TokensUsed;
                msg.State = MessageState.Success;
            }
            catch (Exception ex)
            {
                msg.ErrorMessage = $"生成过程中出错: {ex.Message}";
                msg.State = MessageState.Error;
            }
            finally
            {
                Repaint();
                ScrollToBottom();
            }
        }

        #endregion

        #region 保存逻辑

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
                msg.SavedScriptPath = result.FilePath;
                Repaint();
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
                Repaint();
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

            // 保存耗时统计，因为GeneratePrefab会覆盖它
            msg.CodeGenerationTime = msg.GenerationTime;
            msg.CodeTokensUsed = msg.TokensUsed;

            var result = ScriptGenerator.SaveScript(msg.ScriptName, msg.GeneratedCode);
            if (!result.Success)
            {
                EditorUtility.DisplayDialog("保存失败", result.Error ?? "未知错误", "确定");
                return;
            }

            msg.SavedScriptPath = result.FilePath;
            msg.State = MessageState.WaitingCompile;
            msg.CompileWaitTicks = 0;
            _compilationDetected = false;
            
            _pendingCompileMessage = msg;
            EditorApplication.update += OnCompileWaitUpdate;
            Repaint();
        }

        private void OnCompileWaitUpdate()
        {
            if (_pendingCompileMessage == null)
            {
                EditorApplication.update -= OnCompileWaitUpdate;
                return;
            }

            _pendingCompileMessage.CompileWaitTicks++;

            if (EditorApplication.isCompiling)
            {
                _compilationDetected = true;
            }
            else if (_compilationDetected || _pendingCompileMessage.CompileWaitTicks > 150)
            {
                EditorApplication.update -= OnCompileWaitUpdate;
                _pendingCompileMessage.IsCombinedPrefabPhase = true;
                _pendingCompileMessage.State = MessageState.Generating;
                GeneratePrefab(_pendingCompileMessage);
                _pendingCompileMessage = null;
                return;
            }

            Repaint();
        }

        private void ScrollToBottom()
        {
            _chatScrollPos.y = float.MaxValue;
        }

        #endregion
    }
}
