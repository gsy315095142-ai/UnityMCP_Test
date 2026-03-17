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
    /// AI 快捷命令窗口。
    /// 支持三种模式：生成代码、生成预制体、联合生成（代码+预制体）。
    /// </summary>
    public class AIQuickCommand : EditorWindow
    {
        private enum GenerateMode
        {
            Code = 0,
            Prefab = 1,
            Combined = 2
        }

        private enum State
        {
            Input,
            Loading,
            CodePreview,
            PrefabPreview,
            WaitingCompile,
            Success,
            Error
        }

        private static readonly string[] MODE_LABELS = { "生成代码", "生成预制体", "联合生成" };

        #region Fields

        private State _state = State.Input;
        private GenerateMode _mode = GenerateMode.Code;
        private string _userInput = "";

        // D1/D2: 代码类型选择
        private CodeType _codeType = CodeType.Auto;

        // 代码生成
        private string _generatedCode = "";
        private string _scriptName = "";

        // 预制体生成
        private PrefabDescription? _prefabDescription;
        private string _prefabName = "";
        private string _rawJson = "";
        private List<string> _prefabWarnings = new();

        // D4: 联合生成状态
        private bool _combinedPrefabPhase;
        private string _savedScriptPath = "";

        // 编译等待
        private bool _compilationDetected;
        private int _compileWaitTicks;

        // 通用
        private string _errorMessage = "";
        private string _savedFilePath = "";
        private float _generationTime;
        private float _codeGenerationTime;
        private int _tokensUsed;
        private int _codeTokensUsed;
        private Vector2 _scrollPos;
        private AIServiceConfig? _config;

        #endregion

        #region 窗口管理

        [MenuItem("Window/AI 助手/快捷生成 %#.", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<AIQuickCommand>(utility: true);
            window.titleContent = new GUIContent("AI 快捷生成");
            window.minSize = new Vector2(600, 500);
            window.maxSize = new Vector2(900, 750);
            window.ResetAll();
            window.ShowUtility();
            window.Focus();
        }

        private void OnEnable()
        {
            _config = AIServiceConfig.Load();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnCompileWaitUpdate;
        }

        private void ResetAll()
        {
            _state = State.Input;
            _userInput = "";
            _generatedCode = "";
            _scriptName = "";
            _prefabDescription = null;
            _prefabName = "";
            _rawJson = "";
            _prefabWarnings = new List<string>();
            _combinedPrefabPhase = false;
            _savedScriptPath = "";
            _compilationDetected = false;
            _compileWaitTicks = 0;
            _errorMessage = "";
            _savedFilePath = "";
            _codeGenerationTime = 0;
            _codeTokensUsed = 0;
            _scrollPos = Vector2.zero;
        }

        #endregion

        #region OnGUI

        private void OnGUI()
        {
            EditorGUILayout.Space(5);

            switch (_state)
            {
                case State.Input:
                    DrawInputUI();
                    break;
                case State.Loading:
                    DrawLoadingUI();
                    break;
                case State.CodePreview:
                    DrawCodePreviewUI();
                    break;
                case State.PrefabPreview:
                    DrawPrefabPreviewUI();
                    break;
                case State.WaitingCompile:
                    DrawWaitingCompileUI();
                    break;
                case State.Success:
                    DrawSuccessUI();
                    break;
                case State.Error:
                    DrawErrorUI();
                    break;
            }
        }

        #endregion

        #region 输入界面

        private void DrawInputUI()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("生成模式", EditorStyles.boldLabel, GUILayout.Width(60));
            _mode = (GenerateMode)GUILayout.Toolbar((int)_mode, MODE_LABELS, GUILayout.Height(25));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // D1/D2: 代码类型选择（代码模式和联合模式可用）
            if (_mode == GenerateMode.Code || _mode == GenerateMode.Combined)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("代码类型", GUILayout.Width(60));
                _codeType = (CodeType)EditorGUILayout.Popup((int)_codeType, PromptBuilder.CodeTypeLabels);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(5);
            }

            switch (_mode)
            {
                case GenerateMode.Code:
                    EditorGUILayout.LabelField("描述你需要的脚本", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "示例：\n" +
                        "• 创建一个 Player 脚本，包含 WASD 移动和空格跳跃\n" +
                        "• 创建一个武器配置 ScriptableObject，包含攻击力和冷却时间\n" +
                        "• 创建一个游戏管理器单例，管理游戏状态和分数",
                        MessageType.None);
                    break;

                case GenerateMode.Prefab:
                    EditorGUILayout.LabelField("描述你需要的预制体", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "示例：\n" +
                        "• 创建一个玩家预制体，包含 Rigidbody、CapsuleCollider 和 Animator\n" +
                        "• 创建一个 UI 面板，包含标题文本和关闭按钮\n" +
                        "• 创建一个敌人预制体，带有子对象 Model 和 HealthBar",
                        MessageType.None);
                    break;

                case GenerateMode.Combined:
                    EditorGUILayout.LabelField("描述你需要的功能（将同时生成代码和预制体）", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "示例：\n" +
                        "• 创建一个可拾取的道具，包含旋转脚本和带碰撞体的预制体\n" +
                        "• 创建一个 NPC 对话系统，包含对话管理脚本和 UI 对话框预制体\n" +
                        "• 创建一个血条 UI，包含生命值脚本和 Slider 预制体",
                        MessageType.None);
                    break;
            }

            EditorGUILayout.Space(5);

            _userInput = EditorGUILayout.TextArea(_userInput, GUILayout.MinHeight(80));

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUI.enabled = !string.IsNullOrWhiteSpace(_userInput);

            var buttonLabel = _mode switch
            {
                GenerateMode.Code => "生成代码",
                GenerateMode.Prefab => "生成预制体",
                GenerateMode.Combined => "开始生成",
                _ => "生成"
            };

            if (GUILayout.Button(buttonLabel, GUILayout.Width(120), GUILayout.Height(30)))
            {
                StartGeneration();
            }

            if (Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Return
                && Event.current.control
                && !string.IsNullOrWhiteSpace(_userInput))
            {
                StartGeneration();
                Event.current.Use();
            }

            GUI.enabled = true;

            if (GUILayout.Button("取消", GUILayout.Width(80), GUILayout.Height(30)))
            {
                Close();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("提示: Ctrl+Enter 快速生成", EditorStyles.miniLabel);
        }

        #endregion

        #region 加载界面

        private void DrawLoadingUI()
        {
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            string loadingText;
            if (_mode == GenerateMode.Combined)
            {
                loadingText = _combinedPrefabPhase
                    ? "正在生成预制体，请稍候... (第 2 步)"
                    : "正在生成代码，请稍候... (第 1 步)";
            }
            else
            {
                loadingText = _mode == GenerateMode.Code
                    ? "正在生成代码，请稍候..."
                    : "正在生成预制体，请稍候...";
            }

            EditorGUILayout.LabelField(loadingText, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"模型: {_config?.GetEffectiveModel() ?? "未配置"}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            Repaint();
        }

        #endregion

        #region 代码预览界面

        private void DrawCodePreviewUI()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("代码生成结果", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                $"耗时: {_generationTime:F1}秒 | Token: {_tokensUsed}",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (_mode == GenerateMode.Combined)
            {
                EditorGUILayout.HelpBox("联合生成 - 第 1 步：代码已生成，保存后将自动生成预制体", MessageType.Info);
            }

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("脚本名称:", GUILayout.Width(70));
            _scriptName = EditorGUILayout.TextField(_scriptName);
            EditorGUILayout.LabelField(".cs", GUILayout.Width(25));
            EditorGUILayout.EndHorizontal();

            var targetPath = ScriptGenerator.GetScriptPath(_scriptName);
            EditorGUILayout.LabelField($"保存位置: {targetPath}", EditorStyles.miniLabel);

            if (ScriptGenerator.ScriptExists(_scriptName))
            {
                EditorGUILayout.HelpBox(
                    $"文件 {_scriptName}.cs 已存在，保存将覆盖（会自动备份）",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("代码预览:");
            _scrollPos = EditorGUILayout.BeginScrollView(
                _scrollPos, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_generatedCode, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            if (_mode == GenerateMode.Combined)
            {
                if (GUILayout.Button("保存代码并生成预制体", GUILayout.Height(30)))
                    SaveCodeAndContinueCombined();
                if (GUILayout.Button("仅保存代码", GUILayout.Height(30)))
                    SaveScript();
            }
            else
            {
                if (GUILayout.Button("创建文件", GUILayout.Height(30)))
                    SaveScript();
            }

            if (GUILayout.Button("重新生成", GUILayout.Height(30)))
                StartGeneration();
            if (GUILayout.Button("取消", GUILayout.Height(30)))
                ResetAll();
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 预制体预览界面

        private void DrawPrefabPreviewUI()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("预制体生成结果", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                $"耗时: {_generationTime:F1}秒 | Token: {_tokensUsed}",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (_mode == GenerateMode.Combined)
            {
                EditorGUILayout.HelpBox(
                    $"联合生成 - 第 2 步：预制体已生成\n脚本已保存: {_savedScriptPath}",
                    MessageType.Info);
            }

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("预制体名称:", GUILayout.Width(80));
            _prefabName = EditorGUILayout.TextField(_prefabName);
            EditorGUILayout.LabelField(".prefab", GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            var prefabPath = PrefabGenerator.GetPrefabPath(_prefabName);
            EditorGUILayout.LabelField($"保存位置: {prefabPath}", EditorStyles.miniLabel);

            if (PrefabGenerator.PrefabExists(_prefabName))
            {
                EditorGUILayout.HelpBox(
                    $"预制体 {_prefabName}.prefab 已存在，保存将覆盖",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(5);

            if (_prefabDescription != null)
            {
                EditorGUILayout.LabelField("预制体结构预览:");
                _scrollPos = EditorGUILayout.BeginScrollView(
                    _scrollPos, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
                DrawGameObjectTree(_prefabDescription.rootObject, 0);
                EditorGUILayout.EndScrollView();
            }

            if (_prefabWarnings.Count > 0)
            {
                EditorGUILayout.Space(5);
                foreach (var warning in _prefabWarnings)
                {
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
                }
            }

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("创建预制体", GUILayout.Height(30)))
                SavePrefab();
            if (GUILayout.Button("重新生成", GUILayout.Height(30)))
            {
                if (_mode == GenerateMode.Combined)
                    GenerateCombinedPrefab();
                else
                    StartGeneration();
            }
            if (GUILayout.Button("取消", GUILayout.Height(30)))
                ResetAll();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGameObjectTree(GameObjectDescription desc, int indent)
        {
            var prefix = new string(' ', indent * 4);
            var activeIcon = desc.active ? "●" : "○";

            EditorGUILayout.LabelField($"{prefix}{activeIcon} {desc.name}", EditorStyles.boldLabel);

            if (desc.components.Count > 0)
            {
                foreach (var comp in desc.components)
                {
                    var propsText = comp.properties.Count > 0
                        ? $" ({comp.properties.Count} 个属性)"
                        : "";
                    EditorGUILayout.LabelField(
                        $"{prefix}    + {comp.type}{propsText}",
                        EditorStyles.miniLabel);
                }
            }

            foreach (var child in desc.children)
            {
                DrawGameObjectTree(child, indent + 1);
            }
        }

        #endregion

        #region 编译等待界面 (D4)

        private void DrawWaitingCompileUI()
        {
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("联合生成 - 等待编译", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(15);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"✓  脚本已保存: {_savedScriptPath}", EditorStyles.label);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            var dots = new string('.', (_compileWaitTicks / 10 % 4) + 1);
            var waitSeconds = _compileWaitTicks * 0.1f;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"⟳  等待 Unity 编译完成{dots} ({waitSeconds:F1}秒)", EditorStyles.label);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("编译完成后将自动生成预制体", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("取消联合生成", GUILayout.Width(150), GUILayout.Height(28)))
            {
                EditorApplication.update -= OnCompileWaitUpdate;
                _savedFilePath = _savedScriptPath;
                _mode = GenerateMode.Code;
                _state = State.Success;
                Repaint();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);
        }

        #endregion

        #region 成功 / 错误界面

        private void DrawSuccessUI()
        {
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            string successText;
            if (_mode == GenerateMode.Combined && !string.IsNullOrEmpty(_savedScriptPath) && !string.IsNullOrEmpty(_savedFilePath))
                successText = "脚本和预制体创建成功！";
            else if (_mode == GenerateMode.Prefab || (_mode == GenerateMode.Combined && !string.IsNullOrEmpty(_savedFilePath)))
                successText = "预制体创建成功！";
            else
                successText = "脚本创建成功！";

            EditorGUILayout.LabelField($"✅ {successText}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 联合模式：显示两个路径
            if (_mode == GenerateMode.Combined && !string.IsNullOrEmpty(_savedScriptPath))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"脚本: {_savedScriptPath}", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_savedFilePath) && _savedFilePath != _savedScriptPath)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField($"预制体: {_savedFilePath}", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }

                if (_codeGenerationTime > 0)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        $"代码耗时: {_codeGenerationTime:F1}秒 ({_codeTokensUsed} token) | 预制体耗时: {_generationTime:F1}秒 ({_tokensUsed} token)",
                        EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(_savedFilePath, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            if (_prefabWarnings.Count > 0)
            {
                EditorGUILayout.Space(5);
                foreach (var w in _prefabWarnings)
                    EditorGUILayout.HelpBox(w, MessageType.Warning);
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();

            if (_mode == GenerateMode.Combined && !string.IsNullOrEmpty(_savedScriptPath))
            {
                if (GUILayout.Button("打开脚本", GUILayout.Height(30)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(_savedScriptPath);
                    if (asset != null) AssetDatabase.OpenAsset(asset);
                }
            }

            if ((_mode == GenerateMode.Code ||
                 (_mode == GenerateMode.Combined && string.IsNullOrEmpty(_savedFilePath)))
                && !string.IsNullOrEmpty(_savedFilePath))
            {
                if (GUILayout.Button("打开文件", GUILayout.Height(30)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(_savedFilePath);
                    if (asset != null) AssetDatabase.OpenAsset(asset);
                }
            }

            if (_mode == GenerateMode.Prefab ||
                (_mode == GenerateMode.Combined && !string.IsNullOrEmpty(_savedFilePath) && _savedFilePath != _savedScriptPath))
            {
                if (GUILayout.Button("选中预制体", GUILayout.Height(30)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(_savedFilePath);
                    if (asset != null)
                    {
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                }
            }

            if (GUILayout.Button("继续生成", GUILayout.Height(30)))
                ResetAll();
            if (GUILayout.Button("关闭", GUILayout.Height(30)))
                Close();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawErrorUI()
        {
            EditorGUILayout.LabelField("生成失败", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.MaxHeight(300));
            EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重试", GUILayout.Height(30)))
                _state = State.Input;
            if (GUILayout.Button("打开设置", GUILayout.Height(30)))
                SettingsWindow.ShowWindow();
            if (GUILayout.Button("关闭", GUILayout.Height(30)))
                Close();
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 核心生成逻辑

        private void StartGeneration()
        {
            _combinedPrefabPhase = false;

            switch (_mode)
            {
                case GenerateMode.Code:
                    GenerateCode();
                    break;
                case GenerateMode.Prefab:
                    GeneratePrefab();
                    break;
                case GenerateMode.Combined:
                    GenerateCode();
                    break;
            }
        }

        private async void GenerateCode()
        {
            if (_config == null)
            {
                _errorMessage = "AI 服务未配置，请先打开设置窗口进行配置。";
                _state = State.Error;
                return;
            }

            _state = State.Loading;
            Repaint();

            try
            {
                var service = AIServiceFactory.Create(_config);
                var context = ProjectContext.Collect();

                var systemPrompt = PromptBuilder.BuildCodeSystemPrompt(context, _codeType);
                var userPrompt = PromptBuilder.BuildCodeUserPrompt(_userInput);

                var response = await service.SendMessageAsync(systemPrompt, userPrompt);

                if (!response.Success)
                {
                    _errorMessage = response.Error ?? "AI 返回了未知错误";
                    _state = State.Error;
                    Repaint();
                    return;
                }

                var parseResult = ResponseParser.ParseCodeResponse(response.Content);

                if (!parseResult.Success)
                {
                    _errorMessage = parseResult.Error ?? "无法解析 AI 响应";
                    if (!string.IsNullOrEmpty(parseResult.Code))
                        _errorMessage += $"\n\nAI 原始输出:\n{parseResult.Code}";
                    _state = State.Error;
                    Repaint();
                    return;
                }

                _generatedCode = parseResult.Code;
                _scriptName = parseResult.ScriptName;
                _generationTime = response.Duration;
                _tokensUsed = response.TokensUsed;
                _state = State.CodePreview;
            }
            catch (Exception ex)
            {
                _errorMessage = $"生成过程中出错: {ex.Message}";
                _state = State.Error;
            }

            Repaint();
        }

        private async void GeneratePrefab()
        {
            if (_config == null)
            {
                _errorMessage = "AI 服务未配置，请先打开设置窗口进行配置。";
                _state = State.Error;
                return;
            }

            _state = State.Loading;
            Repaint();

            try
            {
                var service = AIServiceFactory.Create(_config);
                var context = ProjectContext.Collect();

                var systemPrompt = PromptBuilder.BuildPrefabSystemPrompt(context);
                var userPrompt = PromptBuilder.BuildPrefabUserPrompt(_userInput);

                var response = await service.SendMessageAsync(systemPrompt, userPrompt);

                if (!response.Success)
                {
                    _errorMessage = response.Error ?? "AI 返回了未知错误";
                    _state = State.Error;
                    Repaint();
                    return;
                }

                var parseResult = ResponseParser.ParsePrefabResponse(response.Content);

                if (!parseResult.Success)
                {
                    _errorMessage = parseResult.Error ?? "无法解析预制体 JSON";
                    if (!string.IsNullOrEmpty(parseResult.RawJson))
                        _errorMessage += $"\n\nAI 原始输出:\n{parseResult.RawJson}";
                    _state = State.Error;
                    Repaint();
                    return;
                }

                _prefabDescription = parseResult.Description;
                _prefabName = parseResult.Description!.prefabName;
                _rawJson = parseResult.RawJson;
                _generationTime = response.Duration;
                _tokensUsed = response.TokensUsed;
                _state = State.PrefabPreview;
            }
            catch (Exception ex)
            {
                _errorMessage = $"生成过程中出错: {ex.Message}";
                _state = State.Error;
            }

            Repaint();
        }

        /// <summary>
        /// D4: 联合模式下生成预制体（编译完成后自动调用）
        /// </summary>
        private async void GenerateCombinedPrefab()
        {
            if (_config == null)
            {
                _errorMessage = "AI 服务未配置。";
                _state = State.Error;
                Repaint();
                return;
            }

            _combinedPrefabPhase = true;
            _state = State.Loading;
            Repaint();

            try
            {
                var service = AIServiceFactory.Create(_config);
                var context = ProjectContext.Collect();

                var systemPrompt = PromptBuilder.BuildPrefabSystemPrompt(context);
                var userPrompt = PromptBuilder.BuildCombinedPrefabUserPrompt(_userInput, _scriptName);

                var response = await service.SendMessageAsync(systemPrompt, userPrompt);

                if (!response.Success)
                {
                    _errorMessage = response.Error ?? "AI 返回了未知错误";
                    _state = State.Error;
                    Repaint();
                    return;
                }

                var parseResult = ResponseParser.ParsePrefabResponse(response.Content);

                if (!parseResult.Success)
                {
                    _errorMessage = parseResult.Error ?? "无法解析预制体 JSON";
                    if (!string.IsNullOrEmpty(parseResult.RawJson))
                        _errorMessage += $"\n\nAI 原始输出:\n{parseResult.RawJson}";
                    _state = State.Error;
                    Repaint();
                    return;
                }

                _prefabDescription = parseResult.Description;
                _prefabName = parseResult.Description!.prefabName;
                _rawJson = parseResult.RawJson;
                _generationTime = response.Duration;
                _tokensUsed = response.TokensUsed;
                _state = State.PrefabPreview;
            }
            catch (Exception ex)
            {
                _errorMessage = $"生成预制体时出错: {ex.Message}";
                _state = State.Error;
            }

            Repaint();
        }

        #endregion

        #region 保存逻辑

        private void SaveScript()
        {
            if (string.IsNullOrEmpty(_scriptName))
            {
                EditorUtility.DisplayDialog("错误", "脚本名称不能为空", "确定");
                return;
            }

            if (ScriptGenerator.ScriptExists(_scriptName))
            {
                if (!EditorUtility.DisplayDialog(
                    "文件已存在",
                    $"{_scriptName}.cs 已存在，是否覆盖？\n（原文件将自动备份为 .cs.bak）",
                    "覆盖", "取消"))
                    return;
            }

            var result = ScriptGenerator.SaveScript(_scriptName, _generatedCode);

            if (result.Success)
            {
                _savedFilePath = result.FilePath;
                _state = State.Success;
            }
            else
            {
                _errorMessage = result.Error ?? "保存失败";
                _state = State.Error;
            }

            Repaint();
        }

        private void SavePrefab()
        {
            if (_prefabDescription == null)
            {
                EditorUtility.DisplayDialog("错误", "预制体描述为空", "确定");
                return;
            }

            if (string.IsNullOrEmpty(_prefabName))
            {
                EditorUtility.DisplayDialog("错误", "预制体名称不能为空", "确定");
                return;
            }

            _prefabDescription.prefabName = _prefabName;

            var result = PrefabGenerator.Generate(_prefabDescription);

            if (result.Success)
            {
                _savedFilePath = result.AssetPath;
                _prefabWarnings = result.Warnings;
                _state = State.Success;
            }
            else
            {
                _errorMessage = result.Error ?? "保存预制体失败";
                if (result.Warnings.Count > 0)
                    _errorMessage += "\n\n警告:\n" + string.Join("\n", result.Warnings);
                _state = State.Error;
            }

            Repaint();
        }

        /// <summary>
        /// D4: 联合模式 - 保存代码后进入编译等待
        /// </summary>
        private void SaveCodeAndContinueCombined()
        {
            if (string.IsNullOrEmpty(_scriptName))
            {
                EditorUtility.DisplayDialog("错误", "脚本名称不能为空", "确定");
                return;
            }

            if (ScriptGenerator.ScriptExists(_scriptName))
            {
                if (!EditorUtility.DisplayDialog(
                    "文件已存在",
                    $"{_scriptName}.cs 已存在，是否覆盖？\n（原文件将自动备份为 .cs.bak）",
                    "覆盖", "取消"))
                    return;
            }

            // 保存代码生成阶段的耗时统计
            _codeGenerationTime = _generationTime;
            _codeTokensUsed = _tokensUsed;

            var result = ScriptGenerator.SaveScript(_scriptName, _generatedCode);

            if (!result.Success)
            {
                _errorMessage = result.Error ?? "保存失败";
                _state = State.Error;
                Repaint();
                return;
            }

            _savedScriptPath = result.FilePath;

            // 进入编译等待
            _compilationDetected = false;
            _compileWaitTicks = 0;
            _state = State.WaitingCompile;

            EditorApplication.update += OnCompileWaitUpdate;
            Repaint();
        }

        /// <summary>
        /// D4: 编译等待轮询回调。
        /// 策略：先等 isCompiling 变为 true（编译开始），再等变为 false（编译完成）。
        /// 如果超过 15 秒仍未检测到编译，视为编译已完成（脚本可能没有实际变化）。
        /// </summary>
        private void OnCompileWaitUpdate()
        {
            _compileWaitTicks++;

            if (EditorApplication.isCompiling)
            {
                _compilationDetected = true;
            }
            else if (_compilationDetected || _compileWaitTicks > 150)
            {
                EditorApplication.update -= OnCompileWaitUpdate;
                GenerateCombinedPrefab();
                return;
            }

            Repaint();
        }

        #endregion
    }
}
