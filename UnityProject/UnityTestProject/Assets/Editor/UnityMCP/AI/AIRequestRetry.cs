#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMCP.AI
{
    /// <summary>
    /// AI 请求自动重试（网络超时、5xx、429 等瞬时失败，Phase 2-B）。
    /// </summary>
    public static class AIRequestRetry
    {
        /// <summary>
        /// 发送消息并在失败时按配置重试。
        /// </summary>
        public static async Task<AIResponse> SendWithRetryAsync(
            IAIService service,
            AIServiceConfig config,
            string systemPrompt,
            string userMessage,
            IReadOnlyList<ChatMemoryTurn>? priorTurns = null)
        {
            var extra = Mathf.Clamp(config.requestRetries, 0, 6);
            var attempts = 1 + extra;
            AIResponse? last = null;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                last = await service.SendMessageAsync(systemPrompt, priorTurns, userMessage);
                if (last.Success)
                    return last;

                if (attempt >= attempts || !ShouldRetry(last.Error))
                    return last;

                var delay = Mathf.Max(0.1f, config.requestRetryDelaySeconds) * attempt;
                await Task.Delay(TimeSpan.FromSeconds(delay));
            }

            return last ?? AIResponse.Fail("未知错误");
        }

        private static bool ShouldRetry(string? error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return true;

            var e = error.ToLowerInvariant();

            if (e.Contains("timeout") || e.Contains("timed out"))
                return true;
            if (e.Contains("connection") || e.Contains("refused") || e.Contains("reset") ||
                e.Contains("network") || (e.Contains("unable to") && e.Contains("connect")))
                return true;
            if (e.Contains(" 500") || e.Contains(" 502") || e.Contains(" 503") || e.Contains(" 504"))
                return true;
            if (e.Contains("500") || e.Contains("502") || e.Contains("503") || e.Contains("504"))
                return true;
            if (e.Contains("429") || e.Contains("rate limit"))
                return true;
            if (e.Contains("空响应") || e.Contains("empty response"))
                return true;

            // 常见 HTTP / UnityWebRequest 文案
            if (e.Contains("request failed") || (e.Contains("failed to") && e.Contains("receive")))
                return true;

            // 明确不可重试
            if (e.Contains("not found") && (e.Contains("model") || e.Contains("404")))
                return false;
            if (e.Contains("401") || e.Contains("403") || e.Contains("unauthorized"))
                return false;

            return false;
        }
    }
}
