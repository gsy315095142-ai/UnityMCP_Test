#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
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

            if (_userBubbleStyle == null)
            {
                _userBubbleStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    richText = true,
                    fontSize = 13,
                    wordWrap = true,
                    margin = new RectOffset(0, 0, 2, 2),
                    padding = new RectOffset(0, 0, 0, 0)
                };
                _userBubbleStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.92f, 0.95f, 1f)
                    : new Color(0.05f, 0.12f, 0.32f);
            }
        }

        /// <summary>
        /// IMGUI 的 LabelField / HelpBox 无法选中复制；用 SelectableLabel 支持鼠标拖选与 Ctrl+C/Ctrl+V（在可编辑区内）。
        /// 按宽度计算多行高度，避免 SelectableLabel 默认单行裁切。
        /// </summary>
        private void DrawSelectableLabel(string text, GUIStyle style, float contentWidth)
        {
            if (string.IsNullOrEmpty(text)) return;
            var w = Mathf.Max(64f, contentWidth);
            // miniLabel 等默认不换行时，CalcHeight 只算一行，需与绘制使用同一套 wordWrap
            var drawStyle = style.wordWrap ? style : new GUIStyle(style) { wordWrap = true };
            var h = drawStyle.CalcHeight(new GUIContent(text), w);
            h = Mathf.Max(h, EditorGUIUtility.singleLineHeight);
            EditorGUILayout.SelectableLabel(text, drawStyle, GUILayout.Width(w), GUILayout.Height(h));
        }

        private void DrawSelectableHelpPane(string text, float contentWidth)
        {
            if (string.IsNullOrEmpty(text)) return;
            var st = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { wordWrap = true };
            var w = Mathf.Max(64f, contentWidth);
            var h = st.CalcHeight(new GUIContent(text), w);
            h = Mathf.Max(h, EditorGUIUtility.singleLineHeight);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.SelectableLabel(text, st, GUILayout.Width(w), GUILayout.Height(h));
            EditorGUILayout.EndVertical();
        }

        /// <summary>用户消息气泡可用宽度（较大 MinWidth，避免竖条过长）</summary>
        private float ChatUserBubbleMinWidth()
        {
            var max = ChatUserBubbleMaxWidth();
            return Mathf.Clamp(max * 0.55f, 200f, max);
        }

        /// <summary>左侧聊天区域宽度（勿用整窗 position.width，否则会与右侧日志列重叠）。</summary>
        private float ChatAreaWidthForLayout()
        {
            return Mathf.Max(_chatColumnInnerWidth, 200f);
        }

        private float ChatUserBubbleMaxWidth()
        {
            var w = Mathf.Max(ChatAreaWidthForLayout(), minSize.x);
            // 随列宽变宽，接近聊天列全宽（仅留边距）
            return Mathf.Clamp(w - 48f, 280f, 4096f);
        }

        private float ChatAssistantBubbleMinWidth()
        {
            var max = ChatAssistantBubbleMaxWidth();
            return Mathf.Clamp(max * 0.45f, 200f, max);
        }

        private float ChatAssistantBubbleMaxWidth()
        {
            var w = Mathf.Max(ChatAreaWidthForLayout(), minSize.x);
            return Mathf.Clamp(w - 48f, 280f, 4096f);
        }

        /// <summary>气泡内正文可用宽度（扣除 helpBox 左右 padding）。</summary>
        private float ChatBubbleContentWidth(float bubbleOuterMaxWidth)
        {
            InitStyles();
            var pad = _chatBubbleFrameStyle!.padding.left + _chatBubbleFrameStyle.padding.right;
            return Mathf.Max(80f, bubbleOuterMaxWidth - pad);
        }

        private float UserBubbleTextWidth() => ChatBubbleContentWidth(ChatUserBubbleMaxWidth());

        private float AssistantBubbleTextWidth() => ChatBubbleContentWidth(ChatAssistantBubbleMaxWidth());

        private static Color ChatUserBubbleTint()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.48f, 0.95f, 0.58f)
                : new Color(0.55f, 0.76f, 1f, 0.92f);
        }

        private static Color ChatAssistantBubbleTint()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.52f, 0.38f, 0.68f, 0.52f)
                : new Color(0.98f, 0.94f, 0.86f, 1f);
        }

        private void OnGUI()
        {
            InitStyles();
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawToolbar();
            if (!_isMinimized)
            {
                // 须在工具栏之后：切换「API 日志」会改窗口宽度与 _showAiDebugPanel，再算聊天列宽才一致。
                ApplyChatColumnWidthForCurrentLayout();
                EditorGUILayout.Space(5);
                DrawWorkspaceScopePanel();
                EditorGUILayout.Space(5);
                DrawChatHistory();
                EditorGUILayout.Space(5);
                DrawInputArea();
            }
            EditorGUILayout.EndVertical();
            if (!_isMinimized && _showAiDebugPanel)
            {
                DrawDebugPanelSplitter();
                DrawAiDebugSidePanel();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private const float MinimizedHeight = 26f;

        private void ToggleMinimized()
        {
            var r = position;
            if (_isMinimized)
            {
                // 还原
                _isMinimized = false;
                minSize = new Vector2(600f, 500f);
                maxSize = new Vector2(4000f, 4000f);
                r.size = _normalSize.sqrMagnitude > 0.1f
                    ? _normalSize
                    : new Vector2(Mathf.Max(600f, r.width), 600f);
                position = r;
            }
            else
            {
                // 折叠
                _normalSize = r.size;
                _isMinimized = true;
                // 必须先改 min/maxSize 再设 position，否则 Unity 会把高度 clamp 回 minSize
                minSize = new Vector2(300f, MinimizedHeight);
                maxSize = new Vector2(4000f, MinimizedHeight);
                r.height = MinimizedHeight;
                position = r;
            }
            Repaint();
        }

        private const float DebugPanelMinWidth   = 160f;
        private const float DebugPanelMaxWidth   = 800f;
        private const float SplitterGutterWidth  = 10f;   // 拖拽热区宽度
        private static readonly int SplitterCtrlId =
            "UnityMCP.DebugPanelSplitter".GetHashCode();

        /// <summary>右侧日志列占用的水平宽度（面板外框 + 分割条），用于加/减宽窗口。</summary>
        private float DebugPanelTotalHorizontalWidth() =>
            SplitterGutterWidth + _debugPanelWidth + 8f;

        /// <summary>打开/关闭 API 日志时加宽或收回窗口，使左侧聊天区宽度与打开前一致。</summary>
        private void SetShowAiDebugPanel(bool show)
        {
            if (show == _showAiDebugPanel)
                return;

            var extra = DebugPanelTotalHorizontalWidth();
            var r = position;
            if (show)
            {
                r.width += extra;
                position = r;
                minSize = new Vector2(600f + extra, 500f);
            }
            else
            {
                r.width = Mathf.Max(600f, r.width - extra);
                position = r;
                minSize = new Vector2(600f, 500f);
            }

            _showAiDebugPanel = show;
            Repaint();
        }

        private void ApplyChatColumnWidthForCurrentLayout()
        {
            const float hPad = 10f;
            var full = Mathf.Max(position.width, minSize.x);
            _chatColumnInnerWidth = _showAiDebugPanel
                ? Mathf.Max(260f, full - hPad * 2 - DebugPanelTotalHorizontalWidth())
                : Mathf.Max(260f, full - hPad * 2);
        }

        /// <summary>
        /// 在聊天列与日志列之间绘制可拖拽的分割条。
        /// 拖动时实时更新 _debugPanelWidth，宽度夹在 [DebugPanelMinWidth, DebugPanelMaxWidth] 之间。
        /// </summary>
        private void DrawDebugPanelSplitter()
        {
            var r = GUILayoutUtility.GetRect(SplitterGutterWidth, 1f, GUILayout.ExpandHeight(true));

            // 分割线视觉
            var lineCol = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.18f)
                : new Color(0f, 0f, 0f, 0.14f);
            EditorGUI.DrawRect(
                new Rect(r.x + r.width * 0.5f - 0.5f, r.yMin, 1f, r.height), lineCol);

            // 鼠标悬停时显示水平缩放光标
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);

            var evt = Event.current;
            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button == 0 && r.Contains(evt.mousePosition))
                    {
                        GUIUtility.hotControl = SplitterCtrlId;
                        evt.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == SplitterCtrlId)
                    {
                        // 向右拖 → 日志面板变窄；向左拖 → 日志面板变宽
                        _debugPanelWidth = Mathf.Clamp(
                            _debugPanelWidth - evt.delta.x,
                            DebugPanelMinWidth,
                            Mathf.Min(DebugPanelMaxWidth, position.width - 420f));
                        ApplyChatColumnWidthForCurrentLayout();
                        evt.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == SplitterCtrlId)
                    {
                        GUIUtility.hotControl = 0;
                        // 拖拽结束立即持久化
                        EditorPrefs.SetFloat(DebugPanelWidthPrefKey, _debugPanelWidth);
                        evt.Use();
                    }
                    break;
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            EditorGUILayout.LabelField("模式:", GUILayout.Width(40));
            var toolbarIdx = GetToolbarPopupIndex();
            var newToolbarIdx = EditorGUILayout.Popup(toolbarIdx, MODE_LABELS, EditorStyles.toolbarPopup, GUILayout.Width(128));
            if (newToolbarIdx != toolbarIdx)
                SetModeFromToolbarPopupIndex(newToolbarIdx);

            if (_currentMode == GenerateMode.Code || _currentMode == GenerateMode.Combined ||
                _currentMode == GenerateMode.CombinedPrefabFirst)
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
            else if (_currentMode == GenerateMode.ProjectQuery)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("(基于项目扫描数据回答)", EditorStyles.miniLabel, GUILayout.Width(160));
            }
            else if (_currentMode == GenerateMode.AssetDelete)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("(删除 Project 内资源，非写代码)", EditorStyles.miniLabel, GUILayout.Width(200));
            }
            else if (_currentMode == GenerateMode.AssetOps)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("(移动/复制/建文件夹，asset-ops)", EditorStyles.miniLabel, GUILayout.Width(220));
            }

            GUILayout.FlexibleSpace();

            // 当前生效模型名称（右对齐，点击可跳转设置）
            var modelLabel = BuildModelLabel();
            if (GUILayout.Button(modelLabel, EditorStyles.toolbarButton, GUILayout.Width(modelLabel.text.Length * 6 + 10)))
                SettingsWindow.ShowWindow();

            if (!_isMinimized)
            {
                var debugToggle = GUILayout.Toggle(
                    _showAiDebugPanel,
                    new GUIContent("API 日志", "在窗口右侧通顶通底一列；窗口会加宽，聊天区域宽度保持不变"),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(72));
                if (debugToggle != _showAiDebugPanel)
                    SetShowAiDebugPanel(debugToggle);

                if (GUILayout.Button("清空历史", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    ResetAll();
            }

            var minimizeLabel = new GUIContent(
                _isMinimized ? "▲ 展开" : "▼ 最小化",
                _isMinimized ? "展开窗口" : "折叠为工具栏，点击再展开");
            if (GUILayout.Button(minimizeLabel, EditorStyles.toolbarButton, GUILayout.Width(72)))
                ToggleMinimized();

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>构建工具栏中显示的模型标签（名称 + Tooltip）。</summary>
        private GUIContent BuildModelLabel()
        {
            if (_config == null)
                return new GUIContent("⚙ 未配置", "点击打开 AI 设置");

            var provider  = _config.provider.ToString();
            var modelName = _config.GetEffectiveModel();

            // 缩短模型名以适应工具栏宽度（超过 20 字符就截断）
            var displayName = modelName.Length > 20
                ? modelName.Substring(0, 18) + "…"
                : modelName;

            // MCP 模式标识（Moonshot/OpenAI 支持 function-calling）
            var mcpTag = (_config.provider == AIProvider.Moonshot || _config.provider == AIProvider.OpenAI)
                ? " [MCP]" : "";

            var tooltip = $"服务商：{provider}\n模型：{modelName}{(mcpTag.Length > 0 ? "\n模式：MCP 工具调用" : "\n模式：意图路由（不支持 MCP）")}\n点击打开 AI 设置";
            return new GUIContent($"⚙ {displayName}{mcpTag}", tooltip);
        }

        private int GetToolbarPopupIndex()
        {
            if (_currentMode == GenerateMode.CombinedPrefabFirst)
                return (int)GenerateMode.Combined;
            if (_currentMode == GenerateMode.ProjectQuery)
                return 5;
            if (_currentMode == GenerateMode.AssetDelete)
                return 6;
            if (_currentMode == GenerateMode.AssetOps)
                return 7;
            return (int)_currentMode;
        }

        private void SetModeFromToolbarPopupIndex(int idx)
        {
            _currentMode = idx switch
            {
                5 => GenerateMode.ProjectQuery,
                6 => GenerateMode.AssetDelete,
                7 => GenerateMode.AssetOps,
                _ => (GenerateMode)idx
            };
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

        private void LogAiExchange(string phase, AIResponse response, string? note = null)
        {
            AiExchangeDebugLog.AppendExchange(phase, response, note);
            Repaint();
        }

        private void DrawAiDebugSidePanel()
        {
            var panelOuterW = _debugPanelWidth + 8f;
            var headerBg = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.24f, 0.95f)
                : new Color(0.93f, 0.93f, 0.95f, 1f);

            EditorGUILayout.BeginVertical(GUILayout.Width(panelOuterW), GUILayout.ExpandHeight(true));

            // ── 标题栏 ──────────────────────────────────────────────────────────
            var headerRect = EditorGUILayout.GetControlRect(false, 26f);
            EditorGUI.DrawRect(headerRect, headerBg);
            GUI.Label(
                new Rect(headerRect.x + 8f, headerRect.y + 4f, headerRect.width - 16f, 18f),
                "API 请求日志",
                EditorStyles.boldLabel);

            var rev        = AiExchangeDebugLog.Revision;
            var hasPending = rev != _aiDebugLogRevisionSynced;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(6f);
            if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(44)))
            {
                AiExchangeDebugLog.Clear();
                _debugLogEntryTexts.Clear();
                _aiDebugLogRevisionSynced = AiExchangeDebugLog.Revision;
                Repaint();
            }
            if (GUILayout.Button("全部复制", EditorStyles.miniButton, GUILayout.Width(60)))
                EditorGUIUtility.systemCopyBuffer = AiExchangeDebugLog.GetText();

            // 手动刷新：生成中也可随时按，不影响其他条目的选区
            var refreshLabel = hasPending ? "⟳ 刷新 ●" : "⟳ 刷新";
            var refreshStyle = hasPending
                ? new GUIStyle(EditorStyles.miniButton) { fontStyle = FontStyle.Bold }
                : EditorStyles.miniButton;
            if (GUILayout.Button(refreshLabel, refreshStyle, GUILayout.Width(hasPending ? 64 : 52)))
                SyncDebugLogEntries(AiExchangeDebugLog.GetEntries(), rev);

            GUILayout.FlexibleSpace();
            GUILayout.Space(6f);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                $"共 {_debugLogEntryTexts.Count} 条  |  生成完成自动刷新，或手动点「刷新」",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(4f);

            // 生成中不自动刷新（防止 TextArea 选区被重置）；生成结束后自动同步一次
            if (hasPending && !_isGenerating)
                SyncDebugLogEntries(AiExchangeDebugLog.GetEntries(), rev);

            // ── 条目列表区 ───────────────────────────────────────────────────────
            var contentW  = Mathf.Max(64f, _debugPanelWidth - 12f);
            var entryStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                fontSize = 10,
                richText = false,
            };
            var titleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle  = FontStyle.Bold,
                clipping   = TextClipping.Clip,
            };

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            _debugLogScroll = EditorGUILayout.BeginScrollView(_debugLogScroll, GUILayout.ExpandHeight(true));

            if (_debugLogEntryTexts.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "尚无记录。\n发送需求后，此处会追加每次网络返回的摘要，便于排查空内容或解析失败。",
                    EditorStyles.wordWrappedMiniLabel, GUILayout.Width(contentW));
            }
            else
            {
                var prevEnabled = GUI.enabled;
                GUI.enabled = true;

                // 最新的条目排在最上面（倒序显示）
                for (var i = _debugLogEntryTexts.Count - 1; i >= 0; i--)
                {
                    var entry = _debugLogEntryTexts[i];

                    // 提取第一行作为标题（格式：[datetime] phase）
                    var nlIdx = entry.IndexOf('\n');
                    var title = nlIdx > 0 ? entry.Substring(0, nlIdx).Trim() : entry.Trim();

                    // 条目标题行：标题 + 序号 + 复制按钮
                    // 固定行宽 = contentW，防止 ScrollView 内部水平无界导致按钮被挤出
                    const float kNumW  = 28f;
                    const float kCopyW = 36f;
                    const float kPad   = 8f;
                    var titleMaxW = Mathf.Max(20f, contentW - kNumW - kCopyW - kPad);
                    EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Width(contentW));
                    EditorGUILayout.LabelField(title, titleStyle,
                        GUILayout.MinWidth(20f), GUILayout.MaxWidth(titleMaxW));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        $"#{i + 1}",
                        EditorStyles.centeredGreyMiniLabel,
                        GUILayout.Width(kNumW));
                    if (GUILayout.Button("复制", EditorStyles.miniButton, GUILayout.Width(kCopyW)))
                        EditorGUIUtility.systemCopyBuffer = entry;
                    EditorGUILayout.EndHorizontal();

                    // 条目正文（可拖选，Ctrl+C 复制）
                    var h = entryStyle.CalcHeight(new GUIContent(entry), contentW);
                    h = Mathf.Clamp(h, 20f, 8000f);
                    _debugLogEntryTexts[i] = EditorGUILayout.TextArea(
                        _debugLogEntryTexts[i], entryStyle,
                        GUILayout.Width(contentW), GUILayout.Height(h));

                    EditorGUILayout.Space(6f);
                }

                GUI.enabled = prevEnabled;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        /// <summary>将 AiExchangeDebugLog 的条目列表同步到 _debugLogEntryTexts。
        /// 采用「只追加」策略：已有条目的 TextArea 状态不受影响。</summary>
        private void SyncDebugLogEntries(IReadOnlyList<string> entries, int rev)
        {
            // 如果来源条目更少（发生了 Clear），全量重建
            if (entries.Count < _debugLogEntryTexts.Count)
                _debugLogEntryTexts.Clear();

            // 追加新增的条目
            for (var i = _debugLogEntryTexts.Count; i < entries.Count; i++)
                _debugLogEntryTexts.Add(entries[i]);

            _aiDebugLogRevisionSynced = rev;
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
            GUI.backgroundColor = Color.Lerp(Color.white, ChatUserBubbleTint(), EditorGUIUtility.isProSkin ? 0.9f : 0.62f);

            EditorGUILayout.BeginVertical(_chatBubbleFrameStyle!,
                GUILayout.MaxWidth(ChatUserBubbleMaxWidth()),
                GUILayout.MinWidth(ChatUserBubbleMinWidth()));
            GUI.backgroundColor = bg;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("用户", _chatTitleUserStyle!, GUILayout.ExpandWidth(false));
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(msg.Content))
                DrawSelectableLabel(msg.Content, _userBubbleStyle!, UserBubbleTextWidth());

            // 拖入附件缩略图区
            if (msg.DroppedAssets != null && msg.DroppedAssets.Count > 0)
                DrawDroppedAssetsInBubble(msg.DroppedAssets);

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDroppedAssetsInBubble(List<string> assets)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("📎 附件", EditorStyles.miniBoldLabel);

            foreach (var path in assets)
            {
                var ext     = System.IO.Path.GetExtension(path).ToLowerInvariant();
                var isImage = ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" or ".exr" or ".psd";

                EditorGUILayout.BeginHorizontal();
                if (isImage && path.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex != null)
                    {
                        GUILayout.Label(tex, GUILayout.Width(48), GUILayout.Height(48));
                    }
                }

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(System.IO.Path.GetFileName(path), EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(path, EditorStyles.miniLabel);

                // 点击可在 Project 中定位
                if (path.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    if (GUILayout.Button("在 Project 中定位", EditorStyles.miniButton, GUILayout.Width(110)))
                    {
                        var obj = AssetDatabase.LoadMainAssetAtPath(path);
                        if (obj != null) { Selection.activeObject = obj; EditorGUIUtility.PingObject(obj); }
                    }
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawAssistantMessage(ChatMessage msg)
        {
            InitStyles();
            EditorGUILayout.BeginHorizontal();

            var bg = GUI.backgroundColor;
            GUI.backgroundColor = Color.Lerp(Color.white, ChatAssistantBubbleTint(), EditorGUIUtility.isProSkin ? 0.88f : 0.58f);

            EditorGUILayout.BeginVertical(_chatBubbleFrameStyle!,
                GUILayout.MaxWidth(ChatAssistantBubbleMaxWidth()),
                GUILayout.MinWidth(ChatAssistantBubbleMinWidth()));
            GUI.backgroundColor = bg;

            EditorGUILayout.LabelField("LumiAI助手", _chatTitleAssistantStyle!, GUILayout.ExpandWidth(false));

            switch (msg.Type)
            {
                case MessageTypeEnum.Text:
                    DrawSelectableLabel(msg.Content, _assistantBubbleStyle!, AssistantBubbleTextWidth());
                    DrawAssetDeleteResolvedBarForText(msg);
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
                case MessageTypeEnum.AssetDeleteReady:
                    DrawAssetDeleteReadyState(msg);
                    break;
                case MessageTypeEnum.AssetOpsReady:
                    DrawAssetOpsReadyState(msg);
                    break;
                case MessageTypeEnum.TextureGenerated:
                    DrawTextureGeneratedState(msg);
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
            // ── 附件栏（有附件或始终显示 + 按钮）──
            DrawAttachmentBar();

            // ── 输入行（与原来完全一致）──
            EditorGUILayout.BeginHorizontal();

            string hint = _currentMode switch
            {
                GenerateMode.AiJudge        => "用自然语言描述即可；也可用左侧 + 或拖拽附件一起发送。",
                GenerateMode.Code           => "描述你需要的脚本，如：创建一个包含WASD移动的Player脚本",
                GenerateMode.Prefab         => "描述你需要的预制体，如：创建一个包含碰撞体的玩家预制体",
                GenerateMode.Combined       => "描述需要的功能，如：创建一个可拾取的道具(包含脚本和预制体)",
                GenerateMode.SceneOps       => "描述在当前场景要做的事情，如：在根下建空物体 Door，加 BoxCollider",
                GenerateMode.ProjectQuery   => "根据当前工程真实数据提问，如：项目里有哪些预制体、脚本大概有多少",
                GenerateMode.AssetDelete    => "说明要删除的资源：完整路径，或脚本写 类名.cs（如 ObjectColorChanger.cs）",
                GenerateMode.AssetOps       => "说明要如何整理 Assets：例如把某文件夹下材质移到 Archive、批量重命名",
                GenerateMode.TextureGenerate => "描述要生成的图片：如「生成一张无缝草地贴图」「帮我画一个角色头像图标」",
                _ => ""
            };

            GUI.SetNextControlName("ChatInput");
            _userInput = EditorGUILayout.TextArea(_userInput, GUILayout.Height(60), GUILayout.ExpandWidth(true));

            // 输入框刚获得焦点时 Unity 会全选文本；在下一帧清除选区，只保留光标位置。
            var inputFocusedNow = GUI.GetNameOfFocusedControl() == "ChatInput";
            if (inputFocusedNow && !_inputWasFocused)
            {
                var kbCtrl = GUIUtility.keyboardControl;
                EditorApplication.delayCall += () =>
                {
                    if (GUIUtility.keyboardControl != kbCtrl) return;
                    if (GUIUtility.GetStateObject(typeof(TextEditor), kbCtrl) is TextEditor editor)
                        editor.selectIndex = editor.cursorIndex;
                    Repaint();
                };
            }
            _inputWasFocused = inputFocusedNow;

            if (string.IsNullOrEmpty(_userInput))
            {
                var rect = GUILayoutUtility.GetLastRect();
                rect.x += 4;
                rect.y += 2;
                GUI.Label(rect, hint, EditorStyles.centeredGreyMiniLabel);
            }

            bool canSend = (!string.IsNullOrWhiteSpace(_userInput) || _pendingDroppedAssets.Count > 0) && !_isGenerating;
            GUI.enabled = canSend;

            if (GUILayout.Button("发送\n(Ctrl+Enter)", GUILayout.Width(90), GUILayout.Height(60)))
                StartNewTask();

            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Return &&
                Event.current.control && canSend)
            {
                StartNewTask();
                Event.current.Use();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            // ── 拖拽检测（覆盖整个输入区）──
            HandleDragDrop();
        }

        /// <summary>
        /// 附件栏：始终显示一行，包含「+」按钮和已添加的附件 chip（横向滚动）。
        /// </summary>
        private void DrawAttachmentBar()
        {
            EditorGUILayout.BeginHorizontal();

            // ＋ 按钮：固定 22×22 小方块
            if (GUILayout.Button("+", GUILayout.Width(22), GUILayout.Height(22)))
                OpenAttachFilePicker();

            // 附件 chip 列表（横向，超出自然换行）
            string? toRemove = null;
            foreach (var path in _pendingDroppedAssets)
            {
                var ext     = Path.GetExtension(path).ToLowerInvariant();
                var isImage = ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" or ".exr" or ".psd";
                Texture2D? thumb = null;
                if (isImage && path.StartsWith("Assets/", StringComparison.Ordinal))
                    thumb = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                // chip 外框
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox,
                    GUILayout.Height(22), GUILayout.MaxWidth(180));

                // 缩略图 or 类型文字
                if (thumb != null)
                    GUILayout.Label(thumb, GUILayout.Width(20), GUILayout.Height(20));
                else
                {
                    var label = ext switch
                    {
                        ".prefab" => "Prefab",
                        ".mat"    => "Mat",
                        ".cs"     => "CS",
                        ".fbx" or ".obj" => "3D",
                        ".mp3" or ".wav" or ".ogg" or ".aiff" or ".aif" or ".flac" => "Audio",
                        ".mp4" or ".mov" or ".avi" or ".webm" or ".asf" or ".mpg" or ".mpeg" => "Video",
                        _ => ext.TrimStart('.').ToUpper()
                    };
                    EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel,
                        GUILayout.Width(Mathf.Clamp(label.Length * 7, 24, 48)));
                }

                // 文件名（截断）
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Length > 12) name = name.Substring(0, 10) + "…";
                EditorGUILayout.LabelField(name, EditorStyles.miniLabel, GUILayout.MinWidth(30));

                // × 删除
                if (GUILayout.Button("×", EditorStyles.miniLabel, GUILayout.Width(14), GUILayout.Height(18)))
                    toRemove = path;

                EditorGUILayout.EndHorizontal();
            }

            if (toRemove != null) { _pendingDroppedAssets.Remove(toRemove); Repaint(); }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 监听拖拽事件：接受工程内路径直接使用；工程外文件询问后自动导入。
        /// </summary>
        private void HandleDragDrop()
        {
            var ev = Event.current;
            if (ev.type != EventType.DragUpdated && ev.type != EventType.DragPerform)
                return;

            var paths = DragAndDrop.paths;
            if (paths == null || paths.Length == 0) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (ev.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var rawPath in paths)
                {
                    if (string.IsNullOrEmpty(rawPath)) continue;
                    AddFileAsAttachment(rawPath);
                }
                Repaint();
            }
            ev.Use();
        }

        /// <summary>
        /// 点击「+」按钮弹出文件选择框；支持选择工程内外的文件。
        /// </summary>
        private void OpenAttachFilePicker()
        {
            var picked = EditorUtility.OpenFilePanelWithFilters(
                "选择附件",
                Application.dataPath,
                new[]
                {
                    "图片",   "png,jpg,jpeg,tga,bmp,exr,psd,hdr",
                    "音频",   "mp3,wav,ogg,aiff,aif,flac",
                    "视频",   "mp4,mov,avi,webm,asf,mpg,mpeg",
                    "资源文件", "prefab,mat,cs,fbx,obj,anim,unity",
                    "所有文件", "*"
                });

            if (string.IsNullOrEmpty(picked)) return;
            AddFileAsAttachment(picked);
            Repaint();
        }

        /// <summary>
        /// 统一处理「添加附件」：工程内直接使用；工程外图片询问导入目录后自动复制并导入。
        /// </summary>
        private void AddFileAsAttachment(string rawPath)
        {
            var assetPath = ToAssetPath(rawPath);

            // ── 已在工程内，直接用 ──
            if (!string.IsNullOrEmpty(assetPath))
            {
                TryAddPending(assetPath);
                return;
            }

            // ── 工程外文件：支持图片 / 音频 / 视频自动导入 ──
            var ext     = Path.GetExtension(rawPath).ToLowerInvariant();
            var isImage = ext is ".png" or ".jpg" or ".jpeg" or ".tga"
                                 or ".bmp" or ".exr" or ".psd" or ".hdr";
            var isAudio = ext is ".mp3" or ".wav" or ".ogg" or ".aiff" or ".aif" or ".flac";
            var isVideo = ext is ".mp4" or ".mov" or ".avi" or ".webm" or ".asf" or ".mpg" or ".mpeg";

            if (!isImage && !isAudio && !isVideo)
            {
                EditorUtility.DisplayDialog(
                    "无法自动导入",
                    $"该文件类型暂不支持从工程外自动导入：\n{rawPath}\n\n" +
                    "支持自动导入的类型：图片、音频（mp3/wav/ogg 等）、视频（mp4/mov/webm 等）。\n" +
                    "其他类型请先手动复制到 Assets 目录内。",
                    "确定");
                return;
            }

            // 默认目标文件夹按类型区分
            var defaultSubFolder = isImage ? "Textures/Imported"
                                 : isAudio ? "Audio/Imported"
                                 :           "Video/Imported";

            // 使用 delayCall 延迟到下一帧打开文件夹选择框，避免两个原生对话框连续弹出时
            // Windows 在第二个对话框关闭后将焦点归还给第一个对话框的问题。
            var capturedRawPath      = rawPath;
            var capturedDefaultSub   = defaultSubFolder;
            var capturedIsImage      = isImage;
            var capturedIsAudio      = isAudio;
            EditorApplication.delayCall += () => ImportExternalFileDelayed(
                capturedRawPath, capturedDefaultSub, capturedIsImage, capturedIsAudio);
        }

        private void ImportExternalFileDelayed(string rawPath, string defaultSubFolder, bool isImage, bool isAudio)
        {
            var typeName      = isImage ? "图片" : isAudio ? "音频" : "视频";
            var fileName      = Path.GetFileName(rawPath);
            var defaultFolder = Path.Combine(
                Application.dataPath,
                defaultSubFolder.Replace('/', Path.DirectorySeparatorChar));
            var defaultAssetFolder = "Assets/" + defaultSubFolder;

            // 用 DisplayDialogComplex 三选一：导入默认位置 / 自定义位置 / 取消
            // 这样避免直接弹 OpenFolderPanel，规避 Unity 原生文件夹对话框会继承上次文件
            // 选择路径（而非我们期望的默认路径）的问题。
            var choice = EditorUtility.DisplayDialogComplex(
                $"导入{typeName}到工程",
                $"文件「{fileName}」位于工程目录之外，需要将其复制到 Assets 文件夹内才能使用。\n\n" +
                $"默认导入位置：{defaultAssetFolder}",
                "导入到默认位置",   // 0
                "取消",             // 1
                "自定义位置…");     // 2

            if (choice == 1) return; // 取消

            string destFolderAsset;
            if (choice == 2)
            {
                // 自定义：确保默认目录先存在，再打开文件夹选择器
                if (!Directory.Exists(defaultFolder))
                    Directory.CreateDirectory(defaultFolder);

                var destFolder = EditorUtility.OpenFolderPanel(
                    $"将{typeName}「{fileName}」导入到哪个文件夹？", defaultFolder, "");

                if (string.IsNullOrEmpty(destFolder)) return;

                destFolderAsset = ToAssetPath(destFolder);
                if (string.IsNullOrEmpty(destFolderAsset))
                {
                    EditorUtility.DisplayDialog(
                        "目标文件夹无效",
                        $"请选择工程 Assets 目录内的文件夹。\n选中的是：{destFolder}",
                        "确定");
                    return;
                }
            }
            else
            {
                // 默认位置
                destFolderAsset = defaultAssetFolder;
            }

            // 确保目标文件夹存在
            var absDest = Path.Combine(
                Application.dataPath.Replace('/', Path.DirectorySeparatorChar),
                destFolderAsset.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absDest))
                Directory.CreateDirectory(absDest);

            // 复制文件（如同名则加 _1 _2 ...）
            var ext          = Path.GetExtension(rawPath).ToLowerInvariant();
            var destFileName = fileName;
            var destFile     = Path.Combine(absDest, destFileName);
            if (File.Exists(destFile))
            {
                var nameNoExt = Path.GetFileNameWithoutExtension(destFileName);
                for (var i = 1; i <= 999; i++)
                {
                    var candidate = Path.Combine(absDest, $"{nameNoExt}_{i}{ext}");
                    if (!File.Exists(candidate)) { destFile = candidate; destFileName = Path.GetFileName(candidate); break; }
                }
            }

            File.Copy(rawPath, destFile);

            // 导入并配置
            var importedPath = $"{destFolderAsset}/{destFileName}";
            AssetDatabase.ImportAsset(importedPath, ImportAssetOptions.ForceUpdate);
            if (isImage && AssetImporter.GetAtPath(importedPath) is TextureImporter ti)
            {
                ti.textureType        = TextureImporterType.Default;
                ti.maxTextureSize     = 2048;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.SaveAndReimport();
            }
            else if (isAudio && AssetImporter.GetAtPath(importedPath) is AudioImporter ai)
            {
                var sampleSettings = ai.defaultSampleSettings;
                sampleSettings.loadType        = AudioClipLoadType.DecompressOnLoad;
                sampleSettings.compressionFormat = AudioCompressionFormat.Vorbis;
                ai.defaultSampleSettings = sampleSettings;
                ai.SaveAndReimport();
            }
            // VideoClipImporter 使用默认设置即可，无需额外配置

            TryAddPending(importedPath);
            ShowNotification(new GUIContent($"已导入：{importedPath}"));
        }

        private void TryAddPending(string assetPath)
        {
            if (!_pendingDroppedAssets.Contains(assetPath))
                _pendingDroppedAssets.Add(assetPath);
        }

        /// <summary>
        /// 将绝对路径或 Unity 内部路径统一转为 Assets/... 相对路径。
        /// 若路径在工程外则返回空字符串。
        /// </summary>
        private static string ToAssetPath(string rawPath)
        {
            if (rawPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                rawPath.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase))
                return rawPath.Replace('\\', '/');

            var dataPath    = Application.dataPath.Replace('\\', '/');
            var projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length);
            var normalized  = rawPath.Replace('\\', '/');
            if (normalized.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalized.Substring(projectRoot.Length);
                if (relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    return relative;
            }
            return "";
        }

        #endregion
    }
}
