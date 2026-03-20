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
        Error,               // 错误展示
        SceneOpsReady       // 场景操控 JSON 已解析，等待用户执行
    }

    /// <summary>
    /// 生成模式
    /// </summary>
    public enum GenerateMode
    {
        /// <summary>由 AI 根据自然语言判断生成代码、预制体或联合生成</summary>
        AiJudge = 0,
        Code = 1,
        Prefab = 2,
        Combined = 3,
        /// <summary>当前场景层级操控（unity-ops）</summary>
        SceneOps = 4
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

        /// <summary>场景操控：解析成功的 envelope，供预览与执行。</summary>
        public SceneOpsEnvelopeDto? SceneOpsEnvelope;
        /// <summary>场景操控执行成功后完成的步数。</summary>
        public int SceneOpsExecutedStepCount;
        /// <summary>因工作区确认被跳过的步数。</summary>
        public int SceneOpsSkippedStepCount;

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
    /// LumiAI 操控窗口（对话式生成与场景操作）。
    /// </summary>
    public class AIQuickCommand : EditorWindow
    {
        private static readonly string[] MODE_LABELS = { "AI判断", "生成代码", "生成预制体", "联合生成", "场景操控" };

        #region Fields

        private bool _workspaceFoldout = true;
        private GenerateMode _currentMode = GenerateMode.AiJudge;
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

        // UI 样式缓存（聊天气泡）
        private GUIStyle? _chatBubbleFrameStyle;
        private GUIStyle? _chatTitleUserStyle;
        private GUIStyle? _chatTitleAssistantStyle;
        private GUIStyle? _assistantBubbleStyle;

        /// <summary><see cref="AssetFolderLister"/> 缓存，工程变更时清空。</summary>
        private List<string>? _assetFoldersCache;

        #endregion

        #region 窗口管理

        [MenuItem("Window/AI 助手/LumiAI操控 %#.", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<AIQuickCommand>(utility: true);
            window.titleContent = new GUIContent(LumiAIProductInfo.WindowTitleWithVersion);
            window.minSize = new Vector2(600, 500);
            window.maxSize = new Vector2(900, 800);
            window.ResetAll();
            window.ShowUtility();
            window.Focus();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(LumiAIProductInfo.WindowTitleWithVersion);
            _config = AIServiceConfig.Load();
            ResetAll();
            EditorApplication.projectChanged += InvalidateAssetFoldersCache;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnCompileWaitUpdate;
            EditorApplication.projectChanged -= InvalidateAssetFoldersCache;
        }

        private void InvalidateAssetFoldersCache()
        {
            _assetFoldersCache = null;
        }

        private List<string> GetCachedAssetFolders()
        {
            if (_assetFoldersCache == null)
                _assetFoldersCache = AssetFolderLister.ListFoldersUnderAssets();
            return _assetFoldersCache;
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
            if (_chatBubbleFrameStyle == null)
            {
                _chatBubbleFrameStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(14, 14, 12, 14),
                    margin = new RectOffset(4, 4, 6, 6)
                };
            }

            if (_chatTitleUserStyle == null)
            {
                _chatTitleUserStyle = new GUIStyle(EditorStyles.label)
                {
                    richText = true,
                    fontSize = 11,
                    fontStyle = FontStyle.Normal,
                    alignment = TextAnchor.MiddleRight,
                    margin = new RectOffset(0, 0, 0, 4),
                    padding = new RectOffset(0, 0, 0, 0)
                };
                _chatTitleUserStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.72f, 0.86f, 1f)
                    : new Color(0.12f, 0.35f, 0.72f);
            }

            if (_chatTitleAssistantStyle == null)
            {
                _chatTitleAssistantStyle = new GUIStyle(EditorStyles.label)
                {
                    richText = true,
                    fontSize = 11,
                    fontStyle = FontStyle.Normal,
                    alignment = TextAnchor.MiddleLeft,
                    margin = new RectOffset(0, 0, 0, 4),
                    padding = new RectOffset(0, 0, 0, 0)
                };
                _chatTitleAssistantStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.55f, 0.9f, 0.88f)
                    : new Color(0.08f, 0.52f, 0.48f);
            }

            if (_assistantBubbleStyle == null)
            {
                _assistantBubbleStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    richText = true,
                    fontSize = 13,
                    wordWrap = true,
                    margin = new RectOffset(0, 0, 2, 2),
                    padding = new RectOffset(0, 0, 0, 0)
                };
                _assistantBubbleStyle.normal.textColor = EditorStyles.label.normal.textColor;
            }
        }

        /// <summary>
        /// IMGUI 的 LabelField / HelpBox 无法选中复制；用 SelectableLabel 支持鼠标拖选与 Ctrl+C/Ctrl+V（在可编辑区内）。
        /// </summary>
        private static void DrawSelectableLabel(string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            EditorGUILayout.SelectableLabel(text, style, GUILayout.ExpandWidth(true));
        }

        private static void DrawSelectableHelpPane(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var st = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { wordWrap = true };
            DrawSelectableLabel(text, st);
            EditorGUILayout.EndVertical();
        }

        /// <summary>用户消息气泡可用宽度（较大 MinWidth，避免竖条过长）</summary>
        private float ChatUserBubbleMinWidth()
        {
            var max = ChatUserBubbleMaxWidth();
            return Mathf.Clamp(max * 0.55f, 280f, max);
        }

        private float ChatUserBubbleMaxWidth()
        {
            var w = Mathf.Max(position.width, minSize.x);
            return Mathf.Min(560f, (w - 56f) * 0.88f);
        }

        private float ChatAssistantBubbleMinWidth()
        {
            var max = ChatAssistantBubbleMaxWidth();
            return Mathf.Clamp(max * 0.45f, 240f, max);
        }

        private float ChatAssistantBubbleMaxWidth()
        {
            var w = Mathf.Max(position.width, minSize.x);
            return Mathf.Min(640f, (w - 40f) * 0.94f);
        }

        private static Color ChatUserBubbleTint()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.38f, 0.58f, 0.92f, 0.42f)
                : new Color(0.86f, 0.93f, 1f, 1f);
        }

        private static Color ChatAssistantBubbleTint()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.48f, 0.55f, 0.58f, 0.38f)
                : new Color(0.94f, 0.97f, 0.98f, 1f);
        }

        private void OnGUI()
        {
            InitStyles();

            EditorGUILayout.Space(5);
            DrawToolbar();
            EditorGUILayout.Space(5);
            DrawWorkspaceScopePanel();
            EditorGUILayout.Space(5);

            DrawChatHistory();

            EditorGUILayout.Space(5);
            DrawInputArea();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            EditorGUILayout.LabelField("模式:", GUILayout.Width(40));
            _currentMode = (GenerateMode)EditorGUILayout.Popup((int)_currentMode, MODE_LABELS, EditorStyles.toolbarPopup, GUILayout.Width(128));

            if (_currentMode == GenerateMode.Code || _currentMode == GenerateMode.Combined)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("代码类型:", GUILayout.Width(60));
                _currentCodeType = (CodeType)EditorGUILayout.Popup((int)_currentCodeType, PromptBuilder.CodeTypeLabels, EditorStyles.toolbarPopup, GUILayout.Width(120));
            }
            else if (_currentMode == GenerateMode.AiJudge)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("(代码类型由 AI 判断)", EditorStyles.miniLabel, GUILayout.Width(140));
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("清空历史", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                ResetAll();
            }

            EditorGUILayout.EndHorizontal();
        }

        private const string WorkspaceScopeHelpBody =
            "启用工作区限制后，请至少满足下列之一：\n\n" +
            "· 勾选「当前活动场景（整场景）」\n" +
            "· 或填写「层级根路径」（只准许某一子树下的 Hierarchy 操作）\n" +
            "· 或填写「预制体路径前缀」（限制 instantiatePrefab 引用的资源路径）\n\n" +
            "说明：此处约束的是「当前活动场景」的 Hierarchy，与在 Project 里点选 .unity 文件不是同一套操作；" +
            "整场景模式表示当前正在编辑的这一套层级均可操作（仍可按预制体前缀限制资源）。\n\n" +
            "预制体路径前缀：AI「创建预制体」会保存到该文件夹（可不勾选「启用限制」也可填写）；可写 Assets/... 或相对路径（自动补全 Assets/）。未填写时默认为 Assets/Prefabs/Generated。\n\n" +
            "执行 scene-ops 时，若某步超出上述范围，将逐步询问「执行此项 / 中止整批 / 跳过此项」。";

        private void DrawWorkspaceScopePanel()
        {
            // 使用普通 Foldout；右侧「！」打开完整说明，避免长文在面板内显示不全
            EditorGUILayout.BeginHorizontal();
            // 仅用三参数重载，兼容无 (foldout, GUIStyle, params GUILayout) 的 Unity 版本
            _workspaceFoldout = EditorGUILayout.Foldout(_workspaceFoldout, "工作区范围", true);
            if (GUILayout.Button(new GUIContent("！", "点击查看工作区说明（可复制对话框内文字）"), EditorStyles.miniButton, GUILayout.Width(26)))
                EditorUtility.DisplayDialog("工作区范围说明", WorkspaceScopeHelpBody, "确定");
            EditorGUILayout.EndHorizontal();

            if (_workspaceFoldout)
            {
                var s = SceneWorkspaceSettings.LoadFromEditorPrefs();
                EditorGUI.BeginChangeCheck();
                s.Enforce = EditorGUILayout.ToggleLeft("启用限制：仅在工作区内的操作可直接执行，超出则逐步询问", s.Enforce);
                EditorGUI.BeginDisabledGroup(!s.Enforce);

                var activeScene = SceneManager.GetActiveScene();
                var activeSceneLabel = activeScene.IsValid()
                    ? $"{activeScene.name}"
                    : "（无活动场景）";

                // —— 层级：整场景 或 子树根路径 —— Unity 的「场景」是 .unity 资产；这里用「当前活动场景 + Hierarchy」表述
                s.HierarchyUseEntireActiveScene = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        $"层级范围：当前活动场景（整场景 · {activeSceneLabel}）",
                        "勾选后，层级工作区为当前加载的活动场景内全部 Hierarchy（不限子树），无需再填层级根路径。\n" +
                        "注意：这与 Project 里的 .unity 资源不同；此处约束的是 Hierarchy 中的父子路径，不是选磁盘上的场景文件。"),
                    s.HierarchyUseEntireActiveScene);

                EditorGUI.BeginDisabledGroup(s.HierarchyUseEntireActiveScene);
                EditorGUILayout.LabelField("层级根路径（仅子树模式）", EditorStyles.label);
                EditorGUILayout.BeginHorizontal();
                s.HierarchyRoot = EditorGUILayout.TextField(
                    new GUIContent(string.Empty, "从场景 Hierarchy 根物体起的路径，如 MyGame/LumiRoot。整场景模式下忽略此项。"),
                    s.HierarchyRoot);
                if (GUILayout.Button(new GUIContent("选中物体", "用当前在 Hierarchy 中选中的物体路径填入"), GUILayout.Width(72)))
                {
                    var go = Selection.activeGameObject;
                    var sc = SceneManager.GetActiveScene();
                    if (go != null && sc.IsValid())
                    {
                        var p = HierarchyLocator.GetHierarchyPath(sc, go);
                        if (!string.IsNullOrEmpty(p))
                        {
                            s.HierarchyRoot = p;
                            s.SaveToEditorPrefs();
                        }
                        else
                            EditorUtility.DisplayDialog("工作区", "无法解析选中物体的层级路径（是否不属于活动场景？）。", "确定");
                    }
                    else
                        EditorUtility.DisplayDialog("工作区", "请在 Hierarchy 中选中一个属于活动场景的物体。", "确定");
                }

                EditorGUILayout.EndHorizontal();

                var roots = activeScene.IsValid() ? activeScene.GetRootGameObjects() : Array.Empty<GameObject>();
                var hierarchyPopupLabels = new List<string> { "（自定义 / 使用上方输入或「选中物体」）" };
                hierarchyPopupLabels.AddRange(roots.Select(r => $"场景根 · {r.name}"));

                var hierarchyPopupIndex = 0;
                var hr = (s.HierarchyRoot ?? "").Trim();
                if (!string.IsNullOrEmpty(hr) && roots.Length > 0)
                {
                    var firstSeg = hr.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrEmpty(firstSeg))
                    {
                        for (var i = 0; i < roots.Length; i++)
                        {
                            if (roots[i].name != firstSeg)
                                continue;
                            hierarchyPopupIndex = i + 1;
                            break;
                        }
                    }
                }

                var newHierarchyPopup = EditorGUILayout.Popup(
                    new GUIContent("快捷（场景根一级）", "仅覆盖为单层根名；深层路径请手动输入或使用「选中物体」。"),
                    hierarchyPopupIndex,
                    hierarchyPopupLabels.ToArray());
                if (newHierarchyPopup != hierarchyPopupIndex && newHierarchyPopup > 0 && newHierarchyPopup - 1 < roots.Length)
                    s.HierarchyRoot = roots[newHierarchyPopup - 1].name;

                EditorGUI.EndDisabledGroup();
                // 层级约束依赖「启用限制」；预制体保存目录与 scene-ops 校验是两套需求，须始终可编辑。
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(4);

                // —— 预制体路径前缀（磁盘 / Assets 目录）——
                EditorGUILayout.LabelField("预制体路径前缀（Assets 下文件夹）", EditorStyles.label);
                s.PrefabAssetPrefix = EditorGUILayout.TextField(
                    new GUIContent(string.Empty,
                        "instantiatePrefab 的资源路径须以此开头；填写文件夹路径后，AI「创建预制体」会写入此处（可写 Assets/... 或以工程根为基准的相对路径，会自动补全 Assets/）。未填写时默认为 Assets/Prefabs/Generated。"),
                    s.PrefabAssetPrefix);

                var folders = GetCachedAssetFolders();
                var prefabPopupLabels = new List<string> { "（自定义 / 与上方输入不一致）" };
                prefabPopupLabels.AddRange(folders);

                var normalizedPrefix = NormalizeAssetFolderKey(s.PrefabAssetPrefix);
                var prefabPopupIndex = 0;
                if (!string.IsNullOrEmpty(normalizedPrefix))
                {
                    var idx = folders.FindIndex(f => string.Equals(NormalizeAssetFolderKey(f), normalizedPrefix, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                        prefabPopupIndex = idx + 1;
                }

                var newPrefabPopup = EditorGUILayout.Popup(
                    new GUIContent("从项目选择目录", "枚举当前工程 Assets 下的文件夹；工程结构变化后列表在下次打开窗口或资源刷新时更新。"),
                    prefabPopupIndex,
                    prefabPopupLabels.ToArray());
                if (newPrefabPopup != prefabPopupIndex && newPrefabPopup > 0 && newPrefabPopup - 1 < folders.Count)
                    s.PrefabAssetPrefix = folders[newPrefabPopup - 1];

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("刷新目录列表", GUILayout.Width(100)))
                {
                    InvalidateAssetFoldersCache();
                    Repaint();
                }

                EditorGUILayout.EndHorizontal();

                if (EditorGUI.EndChangeCheck())
                    s.SaveToEditorPrefs();
            }
        }

        private static string NormalizeAssetFolderKey(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";
            return path.Trim().Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// AI 生成预制体的落盘目录：与工作区「预制体路径前缀」对齐（须为 Assets/ 下文件夹），否则用项目默认。
        /// </summary>
        private static string ResolvePrefabSaveFolder()
        {
            var ws = SceneWorkspaceSettings.LoadFromEditorPrefs();
            var prefix = SceneWorkspaceSettings.CanonicalAssetFolderPath(ws.PrefabAssetPrefix);
            if (!string.IsNullOrEmpty(prefix))
                return prefix;

            return ProjectContext.Collect().PrefabOutputPath;
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
            InitStyles();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var bg = GUI.backgroundColor;
            GUI.backgroundColor = Color.Lerp(Color.white, ChatUserBubbleTint(), EditorGUIUtility.isProSkin ? 0.82f : 0.52f);

            EditorGUILayout.BeginVertical(_chatBubbleFrameStyle!,
                GUILayout.MaxWidth(ChatUserBubbleMaxWidth()),
                GUILayout.MinWidth(ChatUserBubbleMinWidth()));
            GUI.backgroundColor = bg;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("用户", _chatTitleUserStyle!, GUILayout.ExpandWidth(false));
            EditorGUILayout.EndHorizontal();

            DrawSelectableLabel(msg.Content, _assistantBubbleStyle!);

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawAssistantMessage(ChatMessage msg)
        {
            InitStyles();
            EditorGUILayout.BeginHorizontal();

            var bg = GUI.backgroundColor;
            GUI.backgroundColor = Color.Lerp(Color.white, ChatAssistantBubbleTint(), EditorGUIUtility.isProSkin ? 0.82f : 0.5f);

            EditorGUILayout.BeginVertical(_chatBubbleFrameStyle!,
                GUILayout.MaxWidth(ChatAssistantBubbleMaxWidth()),
                GUILayout.MinWidth(ChatAssistantBubbleMinWidth()));
            GUI.backgroundColor = bg;

            EditorGUILayout.LabelField("AI 助手", _chatTitleAssistantStyle!, GUILayout.ExpandWidth(false));

            switch (msg.Type)
            {
                case MessageTypeEnum.Text:
                    DrawSelectableLabel(msg.Content, _assistantBubbleStyle!);
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
                case MessageTypeEnum.SceneOpsReady:
                    DrawSceneOpsReadyState(msg);
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
                GenerateMode.AiJudge => "用自然语言描述即可；AI 会判断生成脚本、预制体、联合或场景内操控。例：写飞机控制器脚本 / 做带血条 UI 预制体 / 脚本+预制体联合 / 在当前场景根下建个 Door 并加碰撞体",
                GenerateMode.Code => "描述你需要的脚本，如：创建一个包含WASD移动的Player脚本",
                GenerateMode.Prefab => "描述你需要的预制体，如：创建一个包含碰撞体的玩家预制体",
                GenerateMode.Combined => "描述需要的功能，如：创建一个可拾取的道具(包含脚本和预制体)",
                GenerateMode.SceneOps => "描述在当前场景要做的事情，如：在根下建空物体 Door，加 BoxCollider；或把 Props/Crate 挂到选中物体下；或实例化 Assets/.../Enemy.prefab",
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
            DrawSelectableLabel(title, _assistantBubbleStyle!);
            DrawSelectableLabel($"耗时: {msg.GenerationTime:F1}秒 | Token: {msg.TokensUsed}", EditorStyles.miniLabel);
            
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("脚本名称:", GUILayout.Width(60));
            msg.ScriptName = EditorGUILayout.TextField(msg.ScriptName);
            EditorGUILayout.LabelField(".cs", GUILayout.Width(25));
            EditorGUILayout.EndHorizontal();

            if (ScriptGenerator.ScriptExists(msg.ScriptName))
            {
                DrawSelectableHelpPane($"文件 {msg.ScriptName}.cs 已存在，保存将覆盖");
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
            DrawSelectableLabel(title, _assistantBubbleStyle!);
            DrawSelectableLabel($"耗时: {msg.GenerationTime:F1}秒 | Token: {msg.TokensUsed}", EditorStyles.miniLabel);

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("预制体名称:", GUILayout.Width(70));
            msg.PrefabName = EditorGUILayout.TextField(msg.PrefabName);
            EditorGUILayout.LabelField(".prefab", GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            var saveDir = ResolvePrefabSaveFolder();
            DrawSelectableLabel($"保存目录: {saveDir}/", EditorStyles.miniLabel);

            if (PrefabGenerator.PrefabExists(msg.PrefabName, saveDir))
                DrawSelectableHelpPane($"预制体 {msg.PrefabName}.prefab 已存在，保存将覆盖");

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

        private static string BuildSceneOpsStepSummary(SceneOpsEnvelopeDto env)
        {
            if (env.operations == null || env.operations.Length == 0)
                return "（无步骤）";
            var parts = new List<string>();
            foreach (var op in env.operations)
            {
                var o = string.IsNullOrWhiteSpace(op.op) ? "?" : op.op.Trim();
                parts.Add(o);
            }

            return $"共 {env.operations.Length} 步: " + string.Join(" → ", parts);
        }

        private void DrawSceneOpsReadyState(ChatMessage msg)
        {
            DrawSelectableLabel("✅ 场景操控列表已生成（请预览后执行）", _assistantBubbleStyle!);
            DrawSelectableLabel($"耗时: {msg.GenerationTime:F1}秒 | Token: {msg.TokensUsed}", EditorStyles.miniLabel);

            if (msg.SceneOpsEnvelope != null)
            {
                EditorGUILayout.Space(4);
                DrawSelectableLabel(BuildSceneOpsStepSummary(msg.SceneOpsEnvelope), EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("预览 JSON", GUILayout.Height(25)))
            {
                PreviewWindow.ShowWindow("unity-ops JSON 预览", msg.RawJson);
            }

            if (GUILayout.Button("执行场景操作", GUILayout.Height(25)))
            {
                ExecuteSceneOps(msg);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawWaitingCompileState(ChatMessage msg)
        {
            DrawSelectableLabel($"✓ 脚本已保存: {msg.SavedScriptPath}", EditorStyles.wordWrappedLabel);
            
            var dots = new string('.', (msg.CompileWaitTicks / 10 % 4) + 1);
            var waitSeconds = msg.CompileWaitTicks * 0.1f;
            
            DrawSelectableLabel($"⟳ 等待 Unity 编译完成{dots} ({waitSeconds:F1}秒)", EditorStyles.wordWrappedLabel);
            
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
            string text = msg.Mode switch
            {
                GenerateMode.SceneOps => "🎉 场景操控执行成功！",
                GenerateMode.Combined when !string.IsNullOrEmpty(msg.SavedPrefabPath) => "🎉 联合生成最终完成！",
                GenerateMode.Code => "🎉 代码生成并保存成功！",
                _ => "🎉 预制体生成并保存成功！"
            };
                
            DrawSelectableLabel($"<b>{text}</b>", _assistantBubbleStyle!);
            
            EditorGUILayout.Space(5);

            if (!string.IsNullOrEmpty(msg.SavedScriptPath))
                DrawSelectableLabel($"已生成脚本: {msg.SavedScriptPath}", EditorStyles.miniLabel);
            
            if (!string.IsNullOrEmpty(msg.SavedPrefabPath))
                DrawSelectableLabel($"已生成预制体: {msg.SavedPrefabPath}", EditorStyles.miniLabel);

            if (msg.Mode == GenerateMode.SceneOps)
                DrawSelectableLabel(
                    $"场景操控：已执行 {msg.SceneOpsExecutedStepCount} 步，跳过 {msg.SceneOpsSkippedStepCount} 步（工作区确认）",
                    EditorStyles.miniLabel);

            if (msg.PrefabWarnings.Count > 0)
            {
                EditorGUILayout.Space(2);
                foreach (var w in msg.PrefabWarnings)
                    DrawSelectableHelpPane(w);
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
            DrawSelectableLabel("❌ <b>生成失败</b>", _assistantBubbleStyle!);
            if (!string.IsNullOrEmpty(msg.ErrorMessage))
                DrawSelectableHelpPane(msg.ErrorMessage);
            
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
                case GenerateMode.SceneOps:
                    AddTextBubble("⏳ 正在生成场景操控列表，请稍候...");
                    GenerateSceneOpsAsync(context);
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

        private static GenerateMode MapRouteToMode(GenerationRoute route) => route switch
        {
            GenerationRoute.Code => GenerateMode.Code,
            GenerationRoute.Prefab => GenerateMode.Prefab,
            GenerationRoute.Both => GenerateMode.Combined,
            GenerationRoute.SceneOps => GenerateMode.SceneOps,
            _ => GenerateMode.Code
        };

        private static string ModeDecisionLabel(GenerateMode mode) => mode switch
        {
            GenerateMode.Code => "生成代码",
            GenerateMode.Prefab => "生成预制体",
            GenerateMode.Combined => "联合生成（代码 + 预制体）",
            GenerateMode.SceneOps => "场景操控（unity-ops）",
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

                var response = await AIRequestRetry.SendWithRetryAsync(service, _config, systemPrompt, userPrompt);

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

                var resolvedMode = MapRouteToMode(intent.Route);
                context.Mode = resolvedMode;
                context.CodeType = intent.CodeType;

                _chatHistory.RemoveAll(m => m.Role == ChatRole.Assistant && m.Type == MessageTypeEnum.Text && m.Content.StartsWith("⏳"));

                var label = ModeDecisionLabel(resolvedMode);
                var codeHint = resolvedMode != GenerateMode.Prefab && resolvedMode != GenerateMode.SceneOps
                    ? $"（代码类型：{PromptBuilder.CodeTypeLabels[(int)intent.CodeType]}）"
                    : "";
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
                    case GenerateMode.SceneOps:
                        AddTextBubble("⏳ 正在生成场景操控列表，请稍候...");
                        GenerateSceneOpsAsync(context);
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

                var response = await AIRequestRetry.SendWithRetryAsync(service, _config, systemPrompt, userPrompt);

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

                var response = await AIRequestRetry.SendWithRetryAsync(service, _config, systemPrompt, userPrompt);

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

                var response = await AIRequestRetry.SendWithRetryAsync(service, _config, systemPrompt, userPrompt);

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
                AddResultBubble(context);
            }
            catch (Exception ex)
            {
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

        private void ExecuteSceneOps(ChatMessage msg)
        {
            if (msg.SceneOpsEnvelope == null)
            {
                EditorUtility.DisplayDialog("场景操控", "内部错误：未找到已解析的操作列表。", "确定");
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
                EditorUtility.DisplayDialog("场景操控", "operations 为空。", "确定");
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
            EditorUtility.DisplayDialog(
                "场景操控执行失败",
                $"第 {batch.FailedAtIndex + 1} 步失败（0-based 下标 {batch.FailedAtIndex}）\n\n{detail}\n\n可修正场景或 Hierarchy 后再次点击「执行场景操作」，或使用「重试此任务」重新问 AI。",
                "确定");
            Repaint();
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
            var result = PrefabGenerator.Generate(msg.PrefabDescription, ResolvePrefabSaveFolder());

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
