#nullable enable

using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityMCP.AI
{
    /// <summary>
    /// 月之暗面 Moonshot（Kimi）开放平台，OpenAI 兼容 Chat Completions。
    /// 文档：<see href="https://platform.moonshot.cn/docs"/>（中国区一般为 <c>https://api.moonshot.cn/v1</c>）
    /// </summary>
    public sealed class MoonshotOpenAiService : IAIService
    {
        /// <summary>中国区默认 API 根路径（含 /v1）。</summary>
        public const string DefaultBaseUrl = "https://api.moonshot.cn/v1";

        /// <summary>国际区可选：<c>https://api.moonshot.ai/v1</c>。</summary>
        public const string InternationalBaseUrl = "https://api.moonshot.ai/v1";

        /// <summary>Kimi K2.5 等模型在开放平台中的常见 model 标识（以控制台为准）。</summary>
        public const string DefaultModelKimiK25 = "kimi-k2-5";

        private readonly AIServiceConfig _config;

        public string DisplayName => $"Moonshot / Kimi ({_config.GetEffectiveModel()})";

        public MoonshotOpenAiService(AIServiceConfig config)
        {
            _config = config;
        }

        public async Task<AIResponse> SendMessageAsync(string systemPrompt, string userMessage)
        {
            if (string.IsNullOrWhiteSpace(_config.apiKey))
                return AIResponse.Fail("请先填写 Moonshot API Key（platform.moonshot.cn）。");

            var startTime = Time.realtimeSinceStartup;
            var url = BuildUrl("/chat/completions");

            var body = new ChatCompletionRequest
            {
                model = _config.GetEffectiveModel(),
                messages = new[]
                {
                    new ChatMessage { role = "system", content = systemPrompt },
                    new ChatMessage { role = "user", content = userMessage }
                },
                temperature = _config.temperature,
                max_tokens = _config.maxTokens
            };

            var jsonBody = JsonUtility.ToJson(body);

            try
            {
                var responseText = await SendPostJsonAsync(url, jsonBody);
                var parsed = JsonUtility.FromJson<ChatCompletionResponse>(responseText);
                if (parsed?.choices == null || parsed.choices.Length == 0)
                {
                    var err = TryParseErrorMessage(responseText);
                    return AIResponse.Fail(string.IsNullOrEmpty(err)
                        ? "Moonshot 返回了空 choices，请检查模型名称与账户权限。"
                        : err);
                }

                var content = parsed.choices[0].message?.content ?? "";
                var duration = Time.realtimeSinceStartup - startTime;
                var tokens = parsed.usage?.total_tokens ?? 0;

                return AIResponse.Ok(content, duration, tokens);
            }
            catch (Exception ex)
            {
                return AIResponse.Fail($"Moonshot 请求失败: {ex.Message}");
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (string.IsNullOrWhiteSpace(_config.apiKey))
                return false;

            var url = BuildUrl("/models");
            try
            {
                var request = UnityWebRequest.Get(url);
                request.SetRequestHeader("Authorization", $"Bearer {_config.apiKey}");
                request.timeout = 20;
                var text = await SendRequestAsync(request);
                return !string.IsNullOrEmpty(text);
            }
            catch
            {
                return false;
            }
        }

        private string BuildUrl(string pathAfterV1)
        {
            var root = _config.GetEffectiveEndpoint().Trim().TrimEnd('/');
            if (!root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                root += "/v1";
            pathAfterV1 = pathAfterV1.StartsWith('/') ? pathAfterV1 : "/" + pathAfterV1;
            return root + pathAfterV1;
        }

        private static string? TryParseErrorMessage(string json)
        {
            try
            {
                var err = JsonUtility.FromJson<ErrorEnvelope>(json);
                return string.IsNullOrEmpty(err?.error?.message) ? null : err!.error!.message;
            }
            catch
            {
                return null;
            }
        }

        private Task<string> SendPostJsonAsync(string url, string jsonBody)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {_config.apiKey}");
            request.timeout = 120;
            return SendRequestAsync(request);
        }

        private static Task<string> SendRequestAsync(UnityWebRequest request)
        {
            var tcs = new TaskCompletionSource<string>();
            var operation = request.SendWebRequest();

            operation.completed += _ =>
            {
                try
                {
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        tcs.TrySetResult(request.downloadHandler.text);
                    }
                    else
                    {
                        var errorMsg = !string.IsNullOrEmpty(request.downloadHandler?.text)
                            ? $"{request.error}\n{request.downloadHandler.text}"
                            : request.error;
                        tcs.TrySetException(new Exception(errorMsg));
                    }
                }
                finally
                {
                    request.Dispose();
                }
            };

            return tcs.Task;
        }

        #region OpenAI-compatible JSON

        [Serializable]
        private class ChatCompletionRequest
        {
            public string model = "";
            public ChatMessage[] messages = Array.Empty<ChatMessage>();
            public float temperature;
            public int max_tokens;
        }

        [Serializable]
        private class ChatMessage
        {
            public string role = "";
            public string content = "";
        }

        [Serializable]
        private class ChatCompletionResponse
        {
            public ChatChoice[] choices = Array.Empty<ChatChoice>();
            public UsageStats? usage;
        }

        [Serializable]
        private class ChatChoice
        {
            public ChatMessage? message;
        }

        [Serializable]
        private class UsageStats
        {
            public int total_tokens;
        }

        [Serializable]
        private class ErrorEnvelope
        {
            public ErrorBody? error;
        }

        [Serializable]
        private class ErrorBody
        {
            public string message = "";
        }

        #endregion
    }
}
