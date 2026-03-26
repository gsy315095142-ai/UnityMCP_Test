#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
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

        // ── IAIService.SendWithToolsAsync ──────────────────────────────────────

        public async Task<McpAIResponse> SendWithToolsAsync(
            List<string> conversationMessages,
            string toolsJson)
        {
            if (string.IsNullOrWhiteSpace(_config.apiKey))
                return McpAIResponse.Fail("请先填写 Moonshot API Key。");

            var startTime = Time.realtimeSinceStartup;
            var url       = BuildUrl("/chat/completions");
            var modelId   = _config.GetEffectiveModel();
            var temp      = ResolveRequestTemperature(modelId, _config.temperature);

            // 手动拼接请求 JSON（JsonUtility 无法处理 tools 数组的复杂结构）
            var messagesArray = "[" + string.Join(",", conversationMessages) + "]";
            var body = "{"
                + $"\"model\":{QuoteJson(modelId)},"
                + $"\"messages\":{messagesArray},"
                + $"\"temperature\":{temp.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},"
                + $"\"max_tokens\":{_config.maxTokens},"
                + $"\"tools\":{toolsJson},"
                + "\"tool_choice\":\"auto\""
                + "}";

            try
            {
                // 工具调用请求体较大（含 tools schema），使用 HttpWebRequest 以获得
                // 最佳的 Unity/Mono TLS 兼容性。
                var rawResponse = await SendPostJsonWithHttpClientAsync(url, body, _config.apiKey);
                var dur         = Time.realtimeSinceStartup - startTime;
                return ParseMcpResponse(rawResponse, dur);
            }
            catch (Exception ex)
            {
                // 展开完整异常链，方便诊断
                var chain = ex.Message;
                var inner = ex.InnerException;
                while (inner != null) { chain += " → " + inner.Message; inner = inner.InnerException; }
                return McpAIResponse.Fail($"Moonshot 工具调用请求失败: {chain}\n请求 URL：{url}");
            }
        }

        // ── 解析工具调用响应 ────────────────────────────────────────────────────

        private static McpAIResponse ParseMcpResponse(string raw, float dur)
        {
            int tokens = OpenAiCompatibleResponseParser.TryParseTotalTokens(raw);
            var finishReason = ExtractStringField(raw, "finish_reason");
            var assistantJson = ExtractAssistantMessageJson(raw);

            if (finishReason == "tool_calls" || raw.Contains("\"tool_calls\""))
            {
                var calls = ExtractToolCalls(raw);
                if (calls.Count > 0)
                    return McpAIResponse.ToolCallsOk(calls, assistantJson, dur, tokens);
            }

            // 普通文字回复
            var content = ExtractContentField(raw);
            if (string.IsNullOrWhiteSpace(content))
                content = OpenAiCompatibleResponseParser.ExtractAssistantText(raw);
            if (string.IsNullOrWhiteSpace(content))
                return McpAIResponse.Fail($"无法解析 AI 响应（finish_reason={finishReason}）：{raw.Substring(0, Math.Min(500, raw.Length))}");

            return McpAIResponse.TextOk(content, assistantJson, dur, tokens);
        }

        private static List<McpToolCall> ExtractToolCalls(string raw)
        {
            var result = new List<McpToolCall>();
            // 找到 "tool_calls": [ ... ] 数组
            var marker = "\"tool_calls\"";
            var idx = raw.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return result;

            var arrStart = raw.IndexOf('[', idx + marker.Length);
            if (arrStart < 0) return result;
            var arrEnd = FindMatchingBracket(raw, arrStart, '[', ']');
            if (arrEnd < 0) return result;

            var arrContent = raw.Substring(arrStart + 1, arrEnd - arrStart - 1);
            // 逐个解析 tool call 对象 { "id":..., "type":"function", "function":{...} }
            var pos = 0;
            while (pos < arrContent.Length)
            {
                var objStart = arrContent.IndexOf('{', pos);
                if (objStart < 0) break;
                var objEnd = FindMatchingBracket(arrContent, objStart, '{', '}');
                if (objEnd < 0) break;

                var obj = arrContent.Substring(objStart, objEnd - objStart + 1);
                var id        = ExtractStringField(obj, "id");
                var funcBlock = ExtractObjectField(obj, "function");
                if (!string.IsNullOrEmpty(funcBlock))
                {
                    var name = ExtractStringField(funcBlock, "name");
                    var args = ExtractStringField(funcBlock, "arguments");
                    result.Add(new McpToolCall { Id = id, FunctionName = name, ArgumentsJson = args });
                }

                pos = objEnd + 1;
            }

            return result;
        }

        /// <summary>提取 choices[0].message 的完整 JSON 对象，供历史回放时原样发回 API。</summary>
        private static string ExtractAssistantMessageJson(string raw)
        {
            // 找 "message": { ... }
            var marker = "\"message\"";
            var idx = raw.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return "{\"role\":\"assistant\",\"content\":\"\"}";

            var objStart = raw.IndexOf('{', idx + marker.Length);
            if (objStart < 0) return "{\"role\":\"assistant\",\"content\":\"\"}";

            var objEnd = FindMatchingBracket(raw, objStart, '{', '}');
            if (objEnd < 0) return "{\"role\":\"assistant\",\"content\":\"\"}";

            return raw.Substring(objStart, objEnd - objStart + 1);
        }

        private static string ExtractContentField(string raw)
        {
            // choices[0].message.content
            var marker = "\"message\"";
            var idx = raw.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return "";
            var snippet = raw.Substring(idx, Math.Min(4000, raw.Length - idx));
            return ExtractStringField(snippet, "content");
        }

        // ── 微型 JSON 工具 ──────────────────────────────────────────────────────

        /// <summary>提取 "key": "value" 中的 value（处理转义）。</summary>
        private static string ExtractStringField(string json, string key)
        {
            var needle = "\"" + key + "\"";
            var idx    = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return "";
            var colon = json.IndexOf(':', idx + needle.Length);
            if (colon < 0) return "";
            var after = json.IndexOf('"', colon + 1);
            if (after < 0) return "";
            var sb    = new StringBuilder();
            var i     = after + 1;
            while (i < json.Length)
            {
                var c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    var next = json[i + 1];
                    if (next == '"')  { sb.Append('"');  i += 2; continue; }
                    if (next == '\\') { sb.Append('\\'); i += 2; continue; }
                    if (next == 'n')  { sb.Append('\n'); i += 2; continue; }
                    if (next == 'r')  { sb.Append('\r'); i += 2; continue; }
                    if (next == 't')  { sb.Append('\t'); i += 2; continue; }
                    sb.Append(next); i += 2; continue;
                }
                if (c == '"') break;
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>提取 "key": { ... } 中的花括号对象原文。</summary>
        private static string ExtractObjectField(string json, string key)
        {
            var needle = "\"" + key + "\"";
            var idx    = json.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) return "";
            var objStart = json.IndexOf('{', idx + needle.Length);
            if (objStart < 0) return "";
            var objEnd = FindMatchingBracket(json, objStart, '{', '}');
            if (objEnd < 0) return "";
            return json.Substring(objStart, objEnd - objStart + 1);
        }

        private static int FindMatchingBracket(string s, int start, char open, char close)
        {
            var depth = 0;
            var inStr = false;
            for (var i = start; i < s.Length; i++)
            {
                if (inStr)
                {
                    if (s[i] == '\\') { i++; continue; }
                    if (s[i] == '"')  inStr = false;
                    continue;
                }
                if (s[i] == '"') { inStr = true; continue; }
                if (s[i] == open)  { depth++; continue; }
                if (s[i] == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static string QuoteJson(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // ── 原有消息发送 ────────────────────────────────────────────────────────

        public Task<AIResponse> SendMessageAsync(string systemPrompt, string userMessage) =>
            SendMessageAsync(systemPrompt, null, userMessage);

        public async Task<AIResponse> SendMessageAsync(
            string systemPrompt,
            IReadOnlyList<ChatMemoryTurn>? priorTurns,
            string userMessage)
        {
            if (string.IsNullOrWhiteSpace(_config.apiKey))
                return AIResponse.Fail("请先填写 Moonshot API Key（platform.moonshot.cn）。");

            var startTime = Time.realtimeSinceStartup;
            var url = BuildUrl("/chat/completions");

            var modelId = _config.GetEffectiveModel();
            var body = new ChatCompletionRequest
            {
                model = modelId,
                messages = BuildOpenAiMessages(systemPrompt, priorTurns, userMessage),
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

        /// <summary>
        /// 使用 <see cref="System.Net.HttpWebRequest"/> 发送 POST JSON 请求（供工具调用等大体积请求使用）。
        /// HttpWebRequest 在 Unity/Mono 环境下对 TLS 和大型请求体的兼容性最佳。
        /// </summary>
        private static async Task<string> SendPostJsonWithHttpClientAsync(string url, string jsonBody, string apiKey)
        {
            // 全局启用 TLS 1.2，并跳过证书验证（Unity Editor 内置证书链有时不完整）
            System.Net.ServicePointManager.SecurityProtocol =
                System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11;
            System.Net.ServicePointManager.ServerCertificateValidationCallback = (_, _, _, _) => true;

            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            request.Method          = "POST";
            request.ContentType     = "application/json";
            request.Timeout         = 120_000;
            request.ReadWriteTimeout = 120_000;
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.UserAgent = "UnityMCP/1.0";

            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            request.ContentLength = bodyBytes.Length;

            // 写入请求体
            using (var stream = await Task<System.IO.Stream>.Factory.FromAsync(
                       request.BeginGetRequestStream, request.EndGetRequestStream, null))
            {
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
            }

            // 读取响应
            System.Net.WebResponse webResp;
            try
            {
                webResp = await Task<System.Net.WebResponse>.Factory.FromAsync(
                    request.BeginGetResponse, request.EndGetResponse, null);
            }
            catch (System.Net.WebException wex) when (wex.Response != null)
            {
                // HTTP 4xx / 5xx：读出错误正文再抛出
                using var errReader = new System.IO.StreamReader(wex.Response.GetResponseStream()!);
                var errBody = await errReader.ReadToEndAsync();
                var code    = (int)((System.Net.HttpWebResponse)wex.Response).StatusCode;
                throw new Exception($"HTTP {code}: {errBody}");
            }

            using var reader = new System.IO.StreamReader(webResp.GetResponseStream()!);
            var raw = await reader.ReadToEndAsync();
            webResp.Close();
            return raw;
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

        private static ChatMessage[] BuildOpenAiMessages(
            string systemPrompt,
            IReadOnlyList<ChatMemoryTurn>? priorTurns,
            string userMessage)
        {
            var list = new List<ChatMessage>
            {
                new() { role = "system", content = systemPrompt }
            };

            if (priorTurns != null)
            {
                foreach (var t in priorTurns)
                {
                    var r = (t.Role ?? "").Trim().ToLowerInvariant();
                    if (r != "user" && r != "assistant") continue;
                    list.Add(new ChatMessage { role = r, content = t.Content ?? "" });
                }
            }

            list.Add(new ChatMessage { role = "user", content = userMessage });
            return list.ToArray();
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
