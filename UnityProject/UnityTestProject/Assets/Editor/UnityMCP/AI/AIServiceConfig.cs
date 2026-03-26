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

        [Tooltip("生成创造性参数 (0-1)，固定为最大值 1")]
        [Range(0f, 1f)]
        public float temperature = 1f;

        [Tooltip("最大生成 Token 数，固定为最大值")]
        public int maxTokens = 16384;

        [Tooltip("请求失败时额外重试次数（暂不启用）")]
        [Range(0, 6)]
        public int requestRetries = 0;

        [Tooltip("重试等待基数（秒）")]
        [Range(0.2f, 10f)]
        public float requestRetryDelaySeconds = 1.25f;

        [Tooltip("聊天记忆：发往模型的「用户+助手」轮数上限，固定为最大值")]
        [Range(0, 32)]
        public int chatMemoryMaxTurns = 32;

        private const string PREFS_KEY = "UnityMCP_AIServiceConfig";
        private const string API_KEY_PREFS_KEY = "UnityMCP_APIKey_Encrypted";

        /// <summary>
        /// 根据当前选择的服务商返回有效的 API 端点。
        /// 若 customEndpoint 明显属于其他服务商（如 Ollama 地址用在 Moonshot），则忽略并使用 provider 默认值。
        /// </summary>
        public string GetEffectiveEndpoint()
        {
            var defaultForProvider = provider switch
            {
                AIProvider.Ollama    => "",                              // Ollama 端点因人而异，不设全局默认
                AIProvider.OpenAI    => "https://api.openai.com/v1",
                AIProvider.Claude    => "https://api.anthropic.com",
                AIProvider.Azure     => "",
                AIProvider.Moonshot  => MoonshotOpenAiService.DefaultBaseUrl,
                _                    => ""
            };

            if (string.IsNullOrEmpty(customEndpoint))
                return defaultForProvider;

            // 防止用 Ollama 地址（http://...） 误用于 Moonshot / OpenAI（https://...api）
            // 规则：如果当前 provider 是云端服务但 customEndpoint 明显是本地地址，优先用 provider 默认值
            bool customIsLocal = customEndpoint.Contains("localhost") ||
                                 customEndpoint.Contains("127.0.0.1") ||
                                 (customEndpoint.StartsWith("http://") &&
                                  !customEndpoint.StartsWith("http://api."));

            bool providerIsCloud = provider == AIProvider.Moonshot ||
                                   provider == AIProvider.OpenAI   ||
                                   provider == AIProvider.Claude;

            if (providerIsCloud && customIsLocal && !string.IsNullOrEmpty(defaultForProvider))
                return defaultForProvider;

            return customEndpoint;
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
                AIProvider.Moonshot => MoonshotOpenAiService.DefaultModel,
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
                    modelName = MoonshotOpenAiService.DefaultModel;
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

            // 易错：K2.5 的 model id 为 kimi-k2.5（点号），写成 kimi-k2-5（横杠）会 404
            if (config.provider == AIProvider.Moonshot &&
                string.Equals(config.modelName?.Trim(), "kimi-k2-5", StringComparison.Ordinal))
            {
                config.modelName = MoonshotOpenAiService.DefaultModel;
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
            temperature = 1f;
            maxTokens = 16384;
            requestRetries = 0;
            requestRetryDelaySeconds = 1.25f;
            chatMemoryMaxTurns = 32;
        }
    }
}
