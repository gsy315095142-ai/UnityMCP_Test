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
        Azure = 3
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
                AIProvider.OpenAI => "https://api.openai.com",
                AIProvider.Claude => "https://api.anthropic.com",
                AIProvider.Azure => "",
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
                _ => ""
            };
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
        }
    }
}
