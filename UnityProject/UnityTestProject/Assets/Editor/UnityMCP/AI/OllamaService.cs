#nullable enable

using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityMCP.AI
{
    /// <summary>
    /// Ollama AI 服务实现。
    /// 使用 Ollama 原生 /api/chat 接口与本地或局域网模型通信。
    /// </summary>
    public class OllamaService : IAIService
    {
        private readonly AIServiceConfig _config;

        public string DisplayName => $"Ollama ({_config.GetEffectiveModel()})";

        public OllamaService(AIServiceConfig config)
        {
            _config = config;
        }

        public async Task<AIResponse> SendMessageAsync(string systemPrompt, string userMessage)
        {
            var startTime = Time.realtimeSinceStartup;
            var url = $"{_config.GetEffectiveEndpoint()}/api/chat";

            var requestBody = new OllamaChatRequest
            {
                model = _config.GetEffectiveModel(),
                messages = new[]
                {
                    new OllamaMessage { role = "system", content = systemPrompt },
                    new OllamaMessage { role = "user", content = userMessage }
                },
                stream = false,
                options = new OllamaOptions
                {
                    temperature = _config.temperature,
                    num_predict = _config.maxTokens
                }
            };

            var jsonBody = JsonUtility.ToJson(requestBody);

            try
            {
                var responseText = await SendHttpPostAsync(url, jsonBody);
                var response = JsonUtility.FromJson<OllamaChatResponse>(responseText);

                if (response?.message == null)
                    return AIResponse.Fail("Ollama 返回了空响应");

                var content = response.message.content ?? "";
                var duration = Time.realtimeSinceStartup - startTime;
                var tokens = response.eval_count;

                return AIResponse.Ok(content, duration, tokens);
            }
            catch (Exception ex)
            {
                return AIResponse.Fail($"Ollama 请求失败: {ex.Message}");
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            var url = $"{_config.GetEffectiveEndpoint()}/api/tags";

            try
            {
                var request = UnityWebRequest.Get(url);
                request.timeout = 10;
                var responseText = await SendRequestAsync(request);
                return !string.IsNullOrEmpty(responseText);
            }
            catch
            {
                return false;
            }
        }

        private static Task<string> SendHttpPostAsync(string url, string jsonBody)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
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

        #region Ollama API 数据结构

        [Serializable]
        private class OllamaChatRequest
        {
            public string model = "";
            public OllamaMessage[] messages = Array.Empty<OllamaMessage>();
            public bool stream;
            public OllamaOptions options = new();
        }

        [Serializable]
        private class OllamaMessage
        {
            public string role = "";
            public string content = "";
        }

        [Serializable]
        private class OllamaOptions
        {
            public float temperature = 0.7f;
            public int num_predict = 4000;
        }

        [Serializable]
        private class OllamaChatResponse
        {
            public string model = "";
            public OllamaMessage? message;
            public bool done;
            public int eval_count;
        }

        #endregion
    }
}
