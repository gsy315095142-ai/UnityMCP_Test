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
        private AIServiceConfig _config = new();
        private string _testResult = "";
        private bool _isTesting;
        private bool _showApiKey;

        private static readonly string[] PROVIDER_NAMES =
        {
            "Ollama（本地模型）",
            "OpenAI",
            "Claude",
            "Azure OpenAI",
            "月之暗面（Moonshot / Kimi）"
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
            _config = AIServiceConfig.Load();
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
            DrawParameterSection();
            EditorGUILayout.Space(20);
            DrawActionButtons();
            EditorGUILayout.Space(10);
            DrawTestResult();

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

        private void DrawParameterSection()
        {
            EditorGUILayout.LabelField("生成参数", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _config.temperature = EditorGUILayout.Slider(
                    "Temperature", _config.temperature, 0f, 1f);
                if (_config.provider == AIProvider.Moonshot &&
                    MoonshotOpenAiService.ModelLocksTemperatureToOne(_config.GetEffectiveModel()))
                {
                    EditorGUILayout.HelpBox(
                        "当前 Moonshot 模型为 Kimi K2.5：接口要求 temperature 必须为 1，请求时会自动使用 1（与滑块无关）。",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "越低越确定性（适合代码生成），越高越有创造性",
                        MessageType.None);
                }

                _config.maxTokens = EditorGUILayout.IntSlider(
                    "最大 Token 数", _config.maxTokens, 512, 16384);

                _config.chatMemoryMaxTurns = EditorGUILayout.IntSlider(
                    new GUIContent(
                        "聊天记忆轮数",
                        "每次请求附带最近几轮「用户+助手」摘要；超出则从最早一轮丢弃。0 关闭。"),
                    _config.chatMemoryMaxTurns,
                    0,
                    32);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("稳定性（Phase 2-B）", EditorStyles.boldLabel);
                _config.requestRetries = EditorGUILayout.IntSlider(
                    "失败自动重试次数", _config.requestRetries, 0, 6);
                EditorGUILayout.HelpBox(
                    "不含首次请求。仅对超时、连接失败、5xx、429 等瞬时错误重试。",
                    MessageType.None);
                _config.requestRetryDelaySeconds = EditorGUILayout.Slider(
                    "重试间隔基数（秒）", _config.requestRetryDelaySeconds, 0.25f, 6f);
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
