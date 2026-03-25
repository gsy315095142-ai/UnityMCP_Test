#nullable enable

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMCP.AI
{
    /// <summary>
    /// OpenAI DALL-E（dall-e-3 / dall-e-2）图片生成服务。
    /// 调用 POST /v1/images/generations，返回 base64 PNG。
    /// </summary>
    public class DalleImageService : IImageAIService
    {
        public string DisplayName => "OpenAI DALL-E";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

        public async Task<ImageAIResponse> GenerateImageAsync(string prompt, ImageAIConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.apiKey))
                return ImageAIResponse.Fail("DALL-E API Key 未配置");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var endpoint = (string.IsNullOrWhiteSpace(config.GetEffectiveEndpoint())
                    ? "https://api.openai.com/v1"
                    : config.GetEffectiveEndpoint()).TrimEnd('/');

                var model   = string.IsNullOrWhiteSpace(config.modelName)    ? "dall-e-3"   : config.modelName;
                var size    = string.IsNullOrWhiteSpace(config.imageSize)    ? "1024x1024" : config.imageSize;
                var quality = string.IsNullOrWhiteSpace(config.imageQuality) ? "standard"  : config.imageQuality;
                var style   = string.IsNullOrWhiteSpace(config.imageStyle)   ? "vivid"     : config.imageStyle;

                var json = $"{{\"model\":\"{JsonUtilityHelper.EscapeJson(model)}\",\"prompt\":\"{JsonUtilityHelper.EscapeJson(prompt)}\",\"n\":1," +
                           $"\"size\":\"{JsonUtilityHelper.EscapeJson(size)}\",\"quality\":\"{JsonUtilityHelper.EscapeJson(quality)}\"," +
                           $"\"style\":\"{JsonUtilityHelper.EscapeJson(style)}\",\"response_format\":\"b64_json\"}}";
                var req  = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/images/generations");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.apiKey);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await _http.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return ImageAIResponse.Fail($"DALL-E 请求失败 {(int)resp.StatusCode}: {body}");

                // 解析响应
                var parsed = JsonUtilityHelper.ParseDalleResponse(body);
                if (parsed.b64 == null)
                    return ImageAIResponse.Fail($"无法解析 DALL-E 响应中的 b64_json: {body}");

                var bytes   = Convert.FromBase64String(parsed.b64);
                sw.Stop();
                return ImageAIResponse.Ok(bytes, null, parsed.revised, (float)sw.Elapsed.TotalSeconds);
            }
            catch (Exception ex)
            {
                return ImageAIResponse.Fail($"DALL-E 请求异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Stability AI（stable-diffusion-3-x）图片生成服务。
    /// 调用 POST /v2beta/stable-image/generate/core，返回 base64。
    /// </summary>
    public class StabilityAIService : IImageAIService
    {
        public string DisplayName => "Stability AI";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

        public async Task<ImageAIResponse> GenerateImageAsync(string prompt, ImageAIConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.apiKey))
                return ImageAIResponse.Fail("Stability AI API Key 未配置");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var endpoint = "https://api.stability.ai/v2beta/stable-image/generate/core";

                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(prompt),              "prompt");
                form.Add(new StringContent("none"),              "output_format");  // png
                if (!string.IsNullOrWhiteSpace(config.imageSize))
                {
                    var parts = config.imageSize.Split('x');
                    if (parts.Length == 2)
                    {
                        form.Add(new StringContent(parts[0]), "width");
                        form.Add(new StringContent(parts[1]), "height");
                    }
                }

                var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                req.Headers.Add("authorization", $"Bearer {config.apiKey}");
                req.Headers.Add("accept", "application/json");
                req.Content = form;

                var resp = await _http.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return ImageAIResponse.Fail($"Stability AI 请求失败 {(int)resp.StatusCode}: {body}");

                var b64 = JsonUtilityHelper.ParseStabilityResponse(body);
                if (b64 == null)
                    return ImageAIResponse.Fail($"无法解析 Stability AI 响应: {body}");

                var bytes = Convert.FromBase64String(b64);
                sw.Stop();
                return ImageAIResponse.Ok(bytes, null, null, (float)sw.Elapsed.TotalSeconds);
            }
            catch (Exception ex)
            {
                return ImageAIResponse.Fail($"Stability AI 请求异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 轻量 JSON 解析辅助（避免引入额外依赖，仅用于图片 AI 响应）。
    /// </summary>
    internal static class JsonUtilityHelper
    {
        internal static string EscapeJson(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

        public static (string? b64, string? revised) ParseDalleResponse(string json)
        {
            var b64     = ExtractStringField(json, "b64_json");
            var revised = ExtractStringField(json, "revised_prompt");
            return (b64, revised);
        }

        public static string? ParseStabilityResponse(string json) =>
            ExtractStringField(json, "image");

        private static string? ExtractStringField(string json, string field)
        {
            var key = $"\"{field}\":\"";
            var idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;
            var start = idx + key.Length;
            var end   = start;
            while (end < json.Length)
            {
                if (json[end] == '"' && json[end - 1] != '\\') break;
                end++;
            }
            return end <= start ? null : json.Substring(start, end - start);
        }
    }
}
