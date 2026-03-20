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

        /// <summary>
        /// 切换 Moonshot 时填入的默认 model（Kimi <b>K2.5</b>：编号里是 <b>点号</b> <c>k2.5</c>）。
        /// 勿写成 <c>kimi-k2-5</c>（连字符）——与官方 model id 不一致，会 404。
        /// 若账户仅开放 K2（非 2.5）线路，可在设置里改为控制台列出的 ID，例如 <c>kimi-k2-0905-preview</c>、<c>kimi-k2-turbo-preview</c>。
        /// </summary>
        public const string DefaultModel = "kimi-k2.5";

        /// <summary>Kimi K2.5 系列：开放平台仅接受 <c>temperature = 1</c>，否则会 400。</summary>
        public static bool ModelLocksTemperatureToOne(string modelId)
        {
            return !string.IsNullOrEmpty(modelId) &&
                   modelId.Contains("k2.5", StringComparison.OrdinalIgnoreCase);
        }

        private static float ResolveRequestTemperature(string modelId, float configured)
        {
            return ModelLocksTemperatureToOne(modelId) ? 1f : configured;
        }

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

            var modelId = _config.GetEffectiveModel();
            var body = new ChatCompletionRequest
            {
                model = modelId,
                messages = new[]
                {
                    new ChatMessage { role = "system", content = systemPrompt },
                    new ChatMessage { role = "user", content = userMessage }
                },
                temperature = ResolveRequestTemperature(modelId, _config.temperature),
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
                if (string.IsNullOrWhiteSpace(content))
                    content = OpenAiCompatibleResponseParser.ExtractAssistantText(responseText);

                var duration = Time.realtimeSinceStartup - startTime;
                var tokens = parsed.usage?.total_tokens ?? 0;
                if (tokens <= 0)
                    tokens = OpenAiCompatibleResponseParser.TryParseTotalTokens(responseText);

                if (string.IsNullOrWhiteSpace(content))
                {
                    var snippetLen = Math.Min(1200, responseText.Length);
                    var snippet = responseText.Length > snippetLen
                        ? responseText.Substring(0, snippetLen) + "…"
                        : responseText;
                    Debug.LogWarning($"[UnityMCP] Moonshot 响应正文为空。请确认模型返回的 message.content 格式；原始片段：\n{snippet}");
                    return AIResponse.Fail(
                        "Moonshot 返回的正文为空。常见原因：API 将回复放在 content 数组/其它字段，而 JsonUtility 只能读字符串。\n已尝试兼容解析仍失败时，请把 Console 中警告里的响应片段发给开发者对照。\n" +
                        $"响应片段：\n{snippet}");
                }

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
