#nullable enable

using System;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.AI
{
    public enum ImageAIProvider
    {
        None = 0,
        DallE = 1,
        StabilityAI = 2,
        Pollinations = 3,
    }

    [Serializable]
    public class ImageAIConfig
    {
        [Tooltip("图片生成服务商；选 None 则禁用图片生成功能")]
        public ImageAIProvider provider = ImageAIProvider.None;

        [Tooltip("API Key")]
        public string apiKey = "";

        [Tooltip("模型名称（DALL-E：dall-e-3 / dall-e-2；Stability：stable-diffusion-3-5-large 等）")]
        public string modelName = "dall-e-3";

        [Tooltip("图片尺寸（DALL-E 3：1024x1024 / 1792x1024 / 1024x1792）")]
        public string imageSize = "1024x1024";

        [Tooltip("图片质量（DALL-E 3：standard / hd）")]
        public string imageQuality = "standard";

        [Tooltip("图片风格（DALL-E 3：vivid / natural）")]
        public string imageStyle = "vivid";

        [Tooltip("保存目录（相对 Assets）")]
        public string saveFolder = "Assets/Textures/Generated";

        private const string PREFS_KEY     = "UnityMCP_ImageAIConfig";
        private const string API_KEY_PREFS = "UnityMCP_ImageAPIKey_Encrypted";

        public bool IsConfigured =>
            provider != ImageAIProvider.None && !string.IsNullOrWhiteSpace(apiKey);

        public string GetEffectiveEndpoint() => provider switch
        {
            ImageAIProvider.DallE        => "https://api.openai.com/v1",
            ImageAIProvider.StabilityAI  => "https://api.stability.ai",
            ImageAIProvider.Pollinations => "https://image.pollinations.ai",
            _                            => ""
        };

        public void ApplyProviderDefaults(ImageAIProvider p)
        {
            provider = p;
            switch (p)
            {
                case ImageAIProvider.DallE:
                    modelName    = "dall-e-3";
                    imageSize    = "1024x1024";
                    imageQuality = "standard";
                    imageStyle   = "vivid";
                    break;
                case ImageAIProvider.StabilityAI:
                    modelName    = "stable-diffusion-3-5-large";
                    imageSize    = "1024x1024";
                    imageQuality = "";
                    imageStyle   = "";
                    break;
                case ImageAIProvider.Pollinations:
                    modelName    = "flux";   // 可选: flux / turbo / kontext / gpt-image-large
                    imageSize    = "1024x1024";
                    imageQuality = "";
                    imageStyle   = "";
                    break;
            }
        }

        public void Save()
        {
            var keyBackup = apiKey;
            apiKey = "";
            EditorPrefs.SetString(PREFS_KEY, JsonUtility.ToJson(this));
            apiKey = keyBackup;

            if (!string.IsNullOrEmpty(keyBackup))
            {
                var enc = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(keyBackup));
                EditorPrefs.SetString(API_KEY_PREFS, enc);
            }
            else
            {
                EditorPrefs.DeleteKey(API_KEY_PREFS);
            }
        }

        public static ImageAIConfig Load()
        {
            var json = EditorPrefs.GetString(PREFS_KEY, "");
            ImageAIConfig cfg;
            try { cfg = string.IsNullOrEmpty(json) ? new ImageAIConfig() : (JsonUtility.FromJson<ImageAIConfig>(json) ?? new ImageAIConfig()); }
            catch { cfg = new ImageAIConfig(); }

            var enc = EditorPrefs.GetString(API_KEY_PREFS, "");
            if (!string.IsNullOrEmpty(enc))
            {
                try { cfg.apiKey = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(enc)); }
                catch { cfg.apiKey = ""; }
            }
            return cfg;
        }

        public void ResetToDefault()
        {
            provider     = ImageAIProvider.None;
            apiKey       = "";
            modelName    = "dall-e-3";
            imageSize    = "1024x1024";
            imageQuality = "standard";
            imageStyle   = "vivid";
            saveFolder   = "Assets/Textures/Generated";
        }
    }
}
