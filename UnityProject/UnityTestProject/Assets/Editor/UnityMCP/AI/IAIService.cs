#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnityMCP.AI
{
    /// <summary>
    /// AI 服务响应数据
    /// </summary>
    public class AIResponse
    {
        /// <summary>是否成功</summary>
        public bool Success { get; set; }

        /// <summary>AI 返回的内容</summary>
        public string Content { get; set; } = "";

        /// <summary>错误信息（失败时）</summary>
        public string? Error { get; set; }

        /// <summary>请求耗时（秒）</summary>
        public float Duration { get; set; }

        /// <summary>使用的 Token 数量（如果 API 返回）</summary>
        public int TokensUsed { get; set; }

        public static AIResponse Ok(string content, float duration = 0, int tokens = 0) => new()
        {
            Success = true,
            Content = content,
            Duration = duration,
            TokensUsed = tokens
        };

        public static AIResponse Fail(string error) => new()
        {
            Success = false,
            Error = error
        };
    }

    /// <summary>
    /// AI 服务接口。
    /// 所有 AI 服务商（Ollama、Claude、OpenAI 等）的统一抽象。
    /// </summary>
    public interface IAIService
    {
        /// <summary>
        /// 发送消息并获取 AI 响应
        /// </summary>
        /// <param name="systemPrompt">系统提示词（指导 AI 行为）</param>
        /// <param name="userMessage">用户消息</param>
        /// <returns>AI 响应</returns>
        Task<AIResponse> SendMessageAsync(string systemPrompt, string userMessage);

        /// <summary>
        /// 发送消息（含多轮记忆）。<paramref name="priorTurns"/> 为 system 之后、本轮 user 之前的 user/assistant 交替内容。
        /// </summary>
        Task<AIResponse> SendMessageAsync(
            string systemPrompt,
            IReadOnlyList<ChatMemoryTurn>? priorTurns,
            string userMessage);

        /// <summary>
        /// 测试与 AI 服务的连接是否正常
        /// </summary>
        /// <returns>连接是否成功</returns>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// 获取当前服务的显示名称
        /// </summary>
        string DisplayName { get; }
    }
}
