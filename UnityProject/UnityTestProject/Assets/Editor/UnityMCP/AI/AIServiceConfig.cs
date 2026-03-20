#nullable enable

using System;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.AI
{
    /// <summary>
    /// AI 服务提供商类型
    /// </summary>
    public enum AIProvider
    {
        Ollama = 0,
        OpenAI = 1,
        Claude = 2,
        Azure = 3,
        /// <summary>月之暗面 Moonshot（Kimi），OpenAI 兼容接口</summary>
        Moonshot = 4
    }

    /// <summary>
    /// AI 服务配置数据。
    /// 存储用户配置的 AI 服务商、API Key、模型等信息。
    /// </summary>
    [Serializable]
    public class AIServiceConfig
    {
        [Tooltip("AI 服务提供商")]
        public AIProvider provider = AIProvider.Ollama;

        [Tooltip("API Key（Ollama 无需填写）")]
        public string apiKey = "";

        [Tooltip("模型名称")]
        public string modelName = "qwen3.5:35b";

        [Tooltip("API 端点地址")]
        public string customEndpoint = "http://192.168.0.34:11434";

        [Tooltip("生成创造性参数 (0-1)")]
        [Range(0f, 1f)]
        public float temperature = 0.7f;

        [Tooltip("最大生成 Token 数")]
        public int maxTokens = 4000;

        [Tooltip("请求失败时额外重试次数（不含首次）。可用于网络抖动或 Ollama 短暂无响应。")]
        [Range(0, 6)]
        public int requestRetries = 2;

        [Tooltip("重试等待基数（秒），第 n 次重试约等待 n×该值。")]
        [Range(0.2f, 10f)]
        public float requestRetryDelaySeconds = 1.25f;

        private const string PREFS_KEY = "UnityMCP_AIServiceConfig";
        private const string API_KEY_PREFS_KEY = "UnityMCP_APIKey_Encrypted";

        /// <summary>
        /// 根据当前选择的服务商返回默认端点
        /// </summary>
        public string GetEffectiveEndpoint()
        {
            if (!string.IsNullOrEmpty(customEndpoint))
                return customEndpoint;

            return provider switch
            {
                AIProvider.Ollama => "http://localhost:11434",
                AIProvider.OpenAI => "https://api.openai.com/v1",
                AIProvider.Claude => "https://api.anthropic.com",
                AIProvider.Azure => "",
                AIProvider.Moonshot => MoonshotOpenAiService.DefaultBaseUrl,
                _ => ""
            };
        }

        /// <summary>
        /// 根据服务商返回默认模型名
        /// </summary>
        public string GetEffectiveModel()
        {
            if (!string.IsNullOrEmpty(modelName))
                return modelName;

            return provider switch
            {
                AIProvider.Ollama => "qwen3.5:35b",
                AIProvider.OpenAI => "gpt-4o",
                AIProvider.Claude => "claude-3-5-sonnet-20241022",
                AIProvider.Azure => "gpt-4o",
                AIProvider.Moonshot => MoonshotOpenAiService.DefaultModelKimiK25,
                _ => ""
            };
        }

        /// <summary>
        /// 切换服务商时写入该厂商常用的 API 根路径与默认模型（会覆盖端点与模型输入框）。
        /// </summary>
        public void ApplyProviderDefaults(AIProvider newProvider)
        {
            provider = newProvider;
            switch (newProvider)
            {
                case AIProvider.Ollama:
                    customEndpoint = "http://192.168.0.34:11434";
                    modelName = "qwen3.5:35b";
                    break;
                case AIProvider.Moonshot:
                    customEndpoint = MoonshotOpenAiService.DefaultBaseUrl;
                    modelName = MoonshotOpenAiService.DefaultModelKimiK25;
                    break;
                case AIProvider.OpenAI:
                    customEndpoint = "https://api.openai.com/v1";
                    modelName = "gpt-4o";
                    break;
                case AIProvider.Claude:
                    customEndpoint = "https://api.anthropic.com";
                    modelName = "claude-3-5-sonnet-20241022";
                    break;
                case AIProvider.Azure:
                    customEndpoint = "";
                    modelName = "gpt-4o";
                    break;
            }
        }

        /// <summary>
        /// 保存配置到 EditorPrefs
        /// </summary>
        public void Save()
        {
            var apiKeyBackup = apiKey;
            apiKey = "";
            var json = JsonUtility.ToJson(this);
            EditorPrefs.SetString(PREFS_KEY, json);
            apiKey = apiKeyBackup;

            if (!string.IsNullOrEmpty(apiKeyBackup))
            {
                var encoded = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(apiKeyBackup));
                EditorPrefs.SetString(API_KEY_PREFS_KEY, encoded);
            }
            else
            {
                EditorPrefs.DeleteKey(API_KEY_PREFS_KEY);
            }
        }

        /// <summary>
        /// 从 EditorPrefs 加载配置
        /// </summary>
        public static AIServiceConfig Load()
        {
            var json = EditorPrefs.GetString(PREFS_KEY, "");
            AIServiceConfig config;

            if (string.IsNullOrEmpty(json))
            {
                config = new AIServiceConfig();
            }
            else
            {
                try
                {
                    config = JsonUtility.FromJson<AIServiceConfig>(json) ?? new AIServiceConfig();
                }
                catch
                {
                    config = new AIServiceConfig();
                }
            }

            var encodedKey = EditorPrefs.GetString(API_KEY_PREFS_KEY, "");
            if (!string.IsNullOrEmpty(encodedKey))
            {
                try
                {
                    config.apiKey = System.Text.Encoding.UTF8.GetString(
                        Convert.FromBase64String(encodedKey));
                }
                catch
                {
                    config.apiKey = "";
                }
            }

            return config;
        }

        /// <summary>
        /// 重置为默认配置
        /// </summary>
        public void ResetToDefault()
        {
            provider = AIProvider.Ollama;
            apiKey = "";
            modelName = "qwen3.5:35b";
            customEndpoint = "http://192.168.0.34:11434";
            temperature = 0.7f;
            maxTokens = 4000;
            requestRetries = 2;
            requestRetryDelaySeconds = 1.25f;
        }
    }
}
