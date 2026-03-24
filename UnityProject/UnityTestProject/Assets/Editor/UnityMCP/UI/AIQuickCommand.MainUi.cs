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
            // 根纵向占满窗口高度，再横向分栏，右侧日志才能与左列同高、通顶通底。
            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            // 整窗横向：左列工具栏/工作区/聊天/输入；右列 API 日志通顶通底（与左列同高）。
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawToolbar();
            // 须在工具栏之后：切换「API 日志」会改窗口宽度与 _showAiDebugPanel，再算聊天列宽才一致。
            ApplyChatColumnWidthForCurrentLayout();
            EditorGUILayout.Space(5);
            DrawWorkspaceScopePanel();
            EditorGUILayout.Space(5);
            DrawChatHistory();
            EditorGUILayout.Space(5);
            DrawInputArea();
            EditorGUILayout.EndVertical();
            if (_showAiDebugPanel)
            {
                DrawChatDebugColumnSeparator();
                DrawAiDebugSidePanel();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private const float DebugPanelContentWidth = 276f;
        private const float MainColumnGutter = 12f;

        /// <summary>右侧日志列占用的水平宽度（与 DrawAiDebugSidePanel + 分隔条一致，用于加宽窗口）。</summary>
        private static float DebugPanelTotalHorizontalWidth()
        {
            var panelOuter = DebugPanelContentWidth + 8f;
            return MainColumnGutter + panelOuter;
        }

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
            if (_showAiDebugPanel)
            {
                _chatColumnInnerWidth = Mathf.Max(
                    260f,
                    full - hPad * 2 - DebugPanelTotalHorizontalWidth());
            }
            else
            {
                _chatColumnInnerWidth = Mathf.Max(260f, full - hPad * 2);
            }
        }

        private void DrawChatDebugColumnSeparator()
        {
            var r = GUILayoutUtility.GetRect(MainColumnGutter, 1f, GUILayout.ExpandHeight(true));
            var h = r.height > 4f ? r.height : Mathf.Max(100f, position.height - 140f);
            var y0 = r.yMin;
            var lineX = r.x + MainColumnGutter * 0.5f - 0.5f;
            var col = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.14f)
                : new Color(0f, 0f, 0f, 0.12f);
            EditorGUI.DrawRect(new Rect(lineX, y0, 1f, h), col);
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

            var debugToggle = GUILayout.Toggle(
                _showAiDebugPanel,
                new GUIContent("API 日志", "在窗口右侧通顶通底一列；窗口会加宽，聊天区域宽度保持不变"),
                EditorStyles.toolbarButton,
                GUILayout.Width(72));
            if (debugToggle != _showAiDebugPanel)
                SetShowAiDebugPanel(debugToggle);

            if (GUILayout.Button("清空历史", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                ResetAll();
            }

            EditorGUILayout.EndHorizontal();
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
            var panelOuterW = DebugPanelContentWidth + 8f;
            var headerBg = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.24f, 0.95f)
                : new Color(0.93f, 0.93f, 0.95f, 1f);

            EditorGUILayout.BeginVertical(GUILayout.Width(panelOuterW), GUILayout.ExpandHeight(true));
            var headerRect = EditorGUILayout.GetControlRect(false, 26f);
            EditorGUI.DrawRect(headerRect, headerBg);
            GUI.Label(
                new Rect(headerRect.x + 8f, headerRect.y + 4f, headerRect.width - 16f, 18f),
                "API 请求日志",
                EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(6f);
            if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(44)))
            {
                AiExchangeDebugLog.Clear();
                _aiDebugLogRevisionSynced = -1;
                Repaint();
            }
            if (GUILayout.Button("全部复制", EditorStyles.miniButton, GUILayout.Width(60)))
                EditorGUIUtility.systemCopyBuffer = AiExchangeDebugLog.GetText();
            GUILayout.FlexibleSpace();
            GUILayout.Space(6f);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("每次调用的 Success / 错误 / 正文长度与预览（可拖选后 Ctrl+C 复制）", EditorStyles.miniLabel);
            EditorGUILayout.Space(4f);

            var rev = AiExchangeDebugLog.Revision;
            if (rev != _aiDebugLogRevisionSynced)
            {
                _aiDebugLogPanelText = AiExchangeDebugLog.GetText();
                if (string.IsNullOrEmpty(_aiDebugLogPanelText))
                    _aiDebugLogPanelText =
                        "尚无记录。\n\n发送需求后，此处会追加每次网络返回的摘要，便于排查空内容或解析失败。";
                _aiDebugLogRevisionSynced = rev;
            }

            var st = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                fontSize = 10,
                richText = false
            };
            var contentW = Mathf.Max(64f, DebugPanelContentWidth - 12f);
            var h = st.CalcHeight(new GUIContent(_aiDebugLogPanelText), contentW);
            h = Mathf.Clamp(h, 120f, 120000f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            _debugLogScroll = EditorGUILayout.BeginScrollView(_debugLogScroll, GUILayout.ExpandHeight(true));
            _aiDebugLogPanelText = EditorGUILayout.TextArea(_aiDebugLogPanelText, st, GUILayout.Width(contentW), GUILayout.Height(h));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
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

            DrawSelectableLabel(msg.Content, _userBubbleStyle!, UserBubbleTextWidth());

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
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
                GenerateMode.ProjectQuery => "根据当前工程真实数据提问，如：项目里有哪些预制体、脚本大概有多少、用了哪些包",
                GenerateMode.AssetDelete => "说明要删除的资源：路径或名称，例如：删掉 Assets/Prefabs/Generated/Old.prefab 或某张贴图",
                GenerateMode.AssetOps => "说明要如何整理 Assets：例如把某文件夹下材质移到 Archive、批量重命名、复制 prefab、新建 Assets/.../UI 文件夹",
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
    }
}
