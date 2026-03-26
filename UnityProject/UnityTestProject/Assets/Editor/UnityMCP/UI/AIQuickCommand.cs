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
    /// LumiAI 操控窗口（对话式生成与场景操作）。
    /// </summary>
    public partial class AIQuickCommand : EditorWindow
    {
        private static readonly string[] MODE_LABELS =
            { "AI判断", "生成代码", "生成预制体", "联合生成", "场景操控", "项目查询", "删除资源", "整理资源" };

        #region Fields

        private bool _workspaceFoldout = true;
        private GenerateMode _currentMode = GenerateMode.AiJudge;
        private CodeType _currentCodeType = CodeType.Auto;
        private string _userInput = "";
        /// <summary>用户已拖入但尚未发送的资源路径（发送后清空）。</summary>
        private List<string> _pendingDroppedAssets = new();

        private List<ChatMessage> _chatHistory = new();
        private Vector2 _chatScrollPos;
        /// <summary>是否在窗口右侧追加「API 日志」列（为 true 时加宽窗口，不压缩聊天列）。</summary>
        private bool _showAiDebugPanel;
        private Vector2 _debugLogScroll;
        private int _aiDebugLogRevisionSynced = -1;
        /// <summary>
        /// 每条日志的 TextArea 显示文本（对应 AiExchangeDebugLog.GetEntries()）。
        /// 只追加不整体替换，保留现有条目的 TextArea 选区状态。
        /// </summary>
        private List<string> _debugLogEntryTexts = new();
        /// <summary>当前帧左侧聊天列可用宽度（开启右侧日志后不含日志列），供气泡 MaxWidth 使用。</summary>
        private float _chatColumnInnerWidth = 600f;
        private AIServiceConfig? _config;
        
        // 运行状态：是否有后台任务正在执行
        private bool _isGenerating;
        private ChatMessage? _pendingMessage; // 当前正在处理的上下文

        // 编译回调相关
        private bool _compilationDetected;
        // 保存联合生成中步骤1耗时信息，用于最后汇总展示
        private float _combinedCodeGenTime;
        private int _combinedCodeTokens;
        private float _combinedPrefabGenTime;
        private int _combinedPrefabTokens;

        /// <summary>先预制体再脚本流程：用户要求「放入当前场景」时，在脚本保存成功后实例化此前保存的预制体。</summary>
        private string? _pendingPrefabPathForScenePlace;

        // UI 样式缓存（聊天气泡）
        private GUIStyle? _chatBubbleFrameStyle;
        private GUIStyle? _chatTitleUserStyle;
        private GUIStyle? _chatTitleAssistantStyle;
        private GUIStyle? _assistantBubbleStyle;
        private GUIStyle? _userBubbleStyle;

        /// <summary><see cref="AssetFolderLister"/> 缓存，工程变更时清空。</summary>
        private List<string>? _assetFoldersCache;

        // ── 输入栏焦点追踪（防止获焦时全选）──
        private bool _inputWasFocused;

        // ── 最小化（折叠）──
        private bool _isMinimized;
        /// <summary>折叠前保存的窗口尺寸，用于还原。</summary>
        private Vector2 _normalSize;

        #endregion

        #region 窗口管理

        [MenuItem("Window/AI 助手/LumiAI操控 %#.", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<AIQuickCommand>(utility: true);
            window.titleContent = new GUIContent(LumiAIProductInfo.WindowTitleWithVersion);
            window.minSize = new Vector2(600, 500);
            // 不设死 maxSize：原先 900×800 会限制无法纵向拉高；与 Unity 默认一致用较大上限，便于随显示器拉高
            window.maxSize = new Vector2(4000, 4000);
            window.ShowUtility();
            window.Focus();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(LumiAIProductInfo.WindowTitleWithVersion);
            _config = AIServiceConfig.Load();
            if (!TryRestoreChatHistory())
                _chatHistory.Clear();
            EditorApplication.projectChanged += InvalidateAssetFoldersCache;
        }

        private void OnDisable()
        {
            PersistChatHistory();
            EditorApplication.update -= OnCompileWaitUpdate;
            EditorApplication.projectChanged -= InvalidateAssetFoldersCache;
        }

        private bool TryRestoreChatHistory()
        {
            var loaded = ChatHistoryPersistence.TryLoad();
            if (loaded == null || loaded.Count == 0)
                return false;
            _chatHistory = loaded;
            return true;
        }

        private void PersistChatHistory()
        {
            if (_chatHistory.Count > 0)
                ChatHistoryPersistence.Save(_chatHistory);
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
            _chatHistory.Clear();
            ChatHistoryPersistence.Clear();
            _isGenerating = false;
            _pendingMessage = null;
            _compilationDetected = false;
            EditorApplication.update -= OnCompileWaitUpdate;
            _pendingPrefabPathForScenePlace = null;
        }

        #endregion

        private void ScrollToBottom()
        {
            _chatScrollPos.y = float.MaxValue;
        }
    }
}
