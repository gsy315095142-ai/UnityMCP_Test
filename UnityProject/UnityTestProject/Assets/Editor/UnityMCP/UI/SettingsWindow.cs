#nullable enable

using System;
using UnityEditor;
using UnityEngine;
using UnityMCP.AI;

namespace UnityMCP.UI
{
    /// <summary>
    /// AI 服务配置窗口。
    /// 用于配置 AI 服务商、API Key、模型等参数。
    /// </summary>
    public class SettingsWindow : EditorWindow
    {
        private AIServiceConfig   _config    = new();
        private ImageAIConfig     _imgConfig = new();
        private string _testResult  = "";
        private string _imgTestResult = "";
        private bool _isTesting;
        private bool _showApiKey;
        private bool _showImgApiKey;

        private static readonly string[] PROVIDER_NAMES =
        {
            "Ollama（本地模型）",
            "OpenAI",
            "Claude",
            "Azure OpenAI",
            "月之暗面（Moonshot / Kimi）"
        };

        private static readonly string[] IMG_PROVIDER_NAMES =
        {
            "禁用（不使用图片 AI）",
            "OpenAI DALL-E",
            "Stability AI",
            "Pollinations.ai（免费，无需 Key）",
        };

        [MenuItem("Window/AI 助手/设置 %#,", priority = 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<SettingsWindow>();
            window.titleContent = new GUIContent("AI 助手 - 设置");
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        private void OnEnable()
        {
            _config    = AIServiceConfig.Load();
            _imgConfig = ImageAIConfig.Load();
        }

        private void OnGUI()
        {
            var scrollPos = Vector2.zero;
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            DrawHeader();
            EditorGUILayout.Space(10);
            DrawProviderSection();
            EditorGUILayout.Space(10);
            DrawConnectionSection();
            EditorGUILayout.Space(10);
            DrawModelSection();
            EditorGUILayout.Space(10);
            EditorGUILayout.Space(20);
            DrawActionButtons();
            EditorGUILayout.Space(10);
            DrawTestResult();

            EditorGUILayout.Space(20);
            DrawImageAISection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("AI 服务配置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "配置 AI 服务连接信息。已支持：Ollama、月之暗面 Moonshot（Kimi，OpenAI 兼容接口）。\n" +
                "OpenAI / Claude / Azure 仍为占位，后续接入。",
                MessageType.Info);
        }

        private void DrawProviderSection()
        {
            EditorGUILayout.LabelField("服务商", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                var newProvider = (AIProvider)EditorGUILayout.Popup(
                    "AI 服务商",
                    (int)_config.provider,
                    PROVIDER_NAMES);

                if (newProvider != _config.provider)
                {
                    _config.ApplyProviderDefaults(newProvider);
                    _testResult = "";
                }

                if (_config.provider is AIProvider.OpenAI or AIProvider.Claude or AIProvider.Azure)
                {
                    EditorGUILayout.HelpBox(
                        $"{PROVIDER_NAMES[(int)_config.provider]} 暂未接入，请改用 Ollama 或月之暗面（Moonshot）。",
                        MessageType.Warning);
                }
                else if (_config.provider == AIProvider.Moonshot)
                {
                    EditorGUILayout.HelpBox(
                        "请在 platform.moonshot.cn 创建 API Key。默认端点为中国区 https://api.moonshot.cn/v1；" +
                        "国际区可改为 https://api.moonshot.ai/v1。\n" +
                        $"默认模型：{MoonshotOpenAiService.DefaultModel}（K2.5 为 k2「点」5，勿写成 kimi-k2-5）。其它模型名以控制台为准。",
                        MessageType.None);
                }
            }
        }

        private void DrawConnectionSection()
        {
            EditorGUILayout.LabelField("连接配置", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _config.customEndpoint = EditorGUILayout.TextField(
                    "API 端点", _config.customEndpoint);

                if (_config.provider != AIProvider.Ollama) // Moonshot / OpenAI / … 均需 Key（当前仅 Moonshot 已实现）
                {
                    EditorGUILayout.BeginHorizontal();
                    if (_showApiKey)
                    {
                        _config.apiKey = EditorGUILayout.TextField("API Key", _config.apiKey);
                    }
                    else
                    {
                        _config.apiKey = EditorGUILayout.PasswordField("API Key", _config.apiKey);
                    }
                    if (GUILayout.Button(_showApiKey ? "隐藏" : "显示", GUILayout.Width(50)))
                    {
                        _showApiKey = !_showApiKey;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.HelpBox("Ollama 无需 API Key", MessageType.None);
                }
            }
        }

        private void DrawModelSection()
        {
            EditorGUILayout.LabelField("模型配置", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _config.modelName = EditorGUILayout.TextField("模型名称", _config.modelName);

                if (_config.provider == AIProvider.Ollama)
                {
                    EditorGUILayout.HelpBox(
                        "输入 Ollama 中已安装的模型名称，如 qwen3.5:35b、llama3:8b 等",
                        MessageType.None);
                }
                else if (_config.provider == AIProvider.Moonshot)
                {
                    EditorGUILayout.HelpBox(
                        $"切换为月之暗面时会自动填入默认模型 {MoonshotOpenAiService.DefaultModel}；" +
                        "若 404，请核对控制台模型列表（K2 线可能与 K2.5 的 id 不同）。",
                        MessageType.None);
                }
            }
        }


        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !_isTesting;
            if (GUILayout.Button("测试连接", GUILayout.Height(30)))
            {
                TestConnection();
            }

            if (GUILayout.Button("保存配置", GUILayout.Height(30)))
            {
                _config.Save();
                _testResult = "✅ 配置已保存";
                ShowNotification(new GUIContent("配置已保存"));
            }

            if (GUILayout.Button("重置默认", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog("确认重置", "是否将所有配置重置为默认值？", "确认", "取消"))
                {
                    _config.ResetToDefault();
                    _testResult = "已重置为默认配置";
                }
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTestResult()
        {
            if (string.IsNullOrEmpty(_testResult)) return;

            if (_isTesting)
            {
                EditorGUILayout.HelpBox("正在测试连接...", MessageType.None);
            }
            else if (_testResult.StartsWith("✅"))
            {
                EditorGUILayout.HelpBox(_testResult, MessageType.Info);
            }
            else if (_testResult.StartsWith("❌"))
            {
                EditorGUILayout.HelpBox(_testResult, MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(_testResult, MessageType.None);
            }
        }

        private void DrawImageAISection()
        {
            EditorGUILayout.LabelField("─── 图片 AI（可选）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "图片 AI 用于「生成贴图 / 图标」功能。所有服务商均需填写 API Key。\n" +
                "• Pollinations.ai：免费注册 enter.pollinations.ai 即可获取 Key（sk_ 开头）。\n" +
                "• DALL-E：需要 OpenAI 账号。  • Stability AI：需要 Stability AI 账号。",
                MessageType.None);

            using (new EditorGUI.IndentLevelScope())
            {
                // 服务商选择
                var newProv = (ImageAIProvider)EditorGUILayout.Popup(
                    "图片 AI 服务商", (int)_imgConfig.provider, IMG_PROVIDER_NAMES);
                if (newProv != _imgConfig.provider)
                {
                    _imgConfig.ApplyProviderDefaults(newProv);
                    _imgTestResult = "";
                }

                if (_imgConfig.provider != ImageAIProvider.None)
                {
                    // Pollinations 特别说明
                    if (_imgConfig.provider == ImageAIProvider.Pollinations)
                    {
                        EditorGUILayout.HelpBox(
                            "Pollinations.ai 现需注册账号并填入 API Key 才可调用。\n" +
                            "免费注册地址：https://enter.pollinations.ai\n" +
                            "注册后在控制台创建 Secret Key（sk_ 开头），填入下方即可。\n\n" +
                            "当前有效图片模型（填模型名称栏）：\n" +
                            "  flux（推荐）、kontext、gpt-image、gpt-image-large\n" +
                            "  seedream、seedream-pro、z-image、z-image-turbo\n" +
                            "  grok-imagine、grok-aurora、aurora、pruna",
                            MessageType.Warning);
                    }

                    var keyLabel = _imgConfig.provider == ImageAIProvider.Pollinations
                        ? "API Key（sk_ 开头）"
                        : "API Key";
                    EditorGUILayout.BeginHorizontal();
                    if (_showImgApiKey)
                        _imgConfig.apiKey = EditorGUILayout.TextField(keyLabel, _imgConfig.apiKey);
                    else
                        _imgConfig.apiKey = EditorGUILayout.PasswordField(keyLabel, _imgConfig.apiKey);
                    if (GUILayout.Button(_showImgApiKey ? "隐藏" : "显示", GUILayout.Width(50)))
                        _showImgApiKey = !_showImgApiKey;
                    EditorGUILayout.EndHorizontal();

                    // 模型名
                    var modelTooltip = _imgConfig.provider switch
                    {
                        ImageAIProvider.Pollinations => "flux（推荐）/ kontext / gpt-image / gpt-image-large / seedream / z-image / z-image-turbo / aurora 等（注意：turbo 已无效）",
                        ImageAIProvider.DallE        => "dall-e-3 / dall-e-2",
                        _                            => "stable-diffusion-3-5-large 等"
                    };
                    _imgConfig.modelName = EditorGUILayout.TextField(
                        new GUIContent("模型名称", modelTooltip), _imgConfig.modelName);

                    // 图片参数
                    _imgConfig.imageSize = EditorGUILayout.TextField(
                        new GUIContent("图片尺寸", "格式：宽x高，如 1024x1024 / 1280x720"),
                        _imgConfig.imageSize);

                    if (_imgConfig.provider == ImageAIProvider.DallE)
                    {
                        _imgConfig.imageQuality = EditorGUILayout.TextField(
                            new GUIContent("图片质量", "standard / hd"), _imgConfig.imageQuality);
                        _imgConfig.imageStyle = EditorGUILayout.TextField(
                            new GUIContent("图片风格", "vivid（生动）/ natural（自然写实）"), _imgConfig.imageStyle);
                    }

                    // 保存目录
                    EditorGUILayout.BeginHorizontal();
                    _imgConfig.saveFolder = EditorGUILayout.TextField(
                        new GUIContent("保存目录", "相对工程根，以 Assets/ 开头"),
                        _imgConfig.saveFolder);
                    if (GUILayout.Button("浏览", GUILayout.Width(50)))
                    {
                        var picked = EditorUtility.OpenFolderPanel(
                            "选择图片保存目录",
                            _imgConfig.saveFolder.Replace("Assets", Application.dataPath),
                            "");
                        if (!string.IsNullOrEmpty(picked) && picked.Contains(Application.dataPath))
                        {
                            _imgConfig.saveFolder = "Assets" + picked.Substring(Application.dataPath.Length).Replace('\\', '/');
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.Space(6);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("保存图片 AI 配置", GUILayout.Height(28)))
                {
                    _imgConfig.Save();
                    _imgTestResult = "✅ 图片 AI 配置已保存";
                    ShowNotification(new GUIContent("图片 AI 配置已保存"));
                }
                if (GUILayout.Button("重置", GUILayout.Width(60), GUILayout.Height(28)))
                {
                    _imgConfig.ResetToDefault();
                    _imgTestResult = "";
                }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_imgTestResult))
                {
                    var mtype = _imgTestResult.StartsWith("✅") ? MessageType.Info :
                                _imgTestResult.StartsWith("❌") ? MessageType.Error : MessageType.None;
                    EditorGUILayout.HelpBox(_imgTestResult, mtype);
                }
            }
        }

        private async void TestConnection()
        {
            _isTesting = true;
            _testResult = "正在测试连接...";
            Repaint();

            try
            {
                var service = AIServiceFactory.Create(_config);
                var success = await service.TestConnectionAsync();
                _testResult = success
                    ? $"✅ 连接成功！服务: {service.DisplayName}"
                    : "❌ 连接失败，请检查端点地址和网络连接";
            }
            catch (Exception ex)
            {
                _testResult = $"❌ 连接失败: {ex.Message}";
            }
            finally
            {
                _isTesting = false;
                Repaint();
            }
        }
    }
}
