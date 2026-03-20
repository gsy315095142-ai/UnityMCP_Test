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

        private static readonly string[] PROVIDER_NAMES = { "Ollama（本地模型）", "OpenAI", "Claude", "Azure OpenAI" };

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
                "配置 AI 服务连接信息。当前 Phase 1 仅支持 Ollama，其他服务商将在后续版本中支持。",
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
                    _config.provider = newProvider;
                    _testResult = "";
                }

                if (_config.provider != AIProvider.Ollama)
                {
                    EditorGUILayout.HelpBox(
                        $"{PROVIDER_NAMES[(int)_config.provider]} 将在 Phase 2 中支持，当前请使用 Ollama。",
                        MessageType.Warning);
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

                if (_config.provider != AIProvider.Ollama)
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
            }
        }

        private void DrawParameterSection()
        {
            EditorGUILayout.LabelField("生成参数", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _config.temperature = EditorGUILayout.Slider(
                    "Temperature", _config.temperature, 0f, 1f);
                EditorGUILayout.HelpBox(
                    "越低越确定性（适合代码生成），越高越有创造性",
                    MessageType.None);

                _config.maxTokens = EditorGUILayout.IntSlider(
                    "最大 Token 数", _config.maxTokens, 512, 16384);

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
