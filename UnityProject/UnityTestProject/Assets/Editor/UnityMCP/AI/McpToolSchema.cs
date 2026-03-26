#nullable enable

using System.Collections.Generic;

namespace UnityMCP.AI
{
    /// <summary>AI 发起的单次工具调用（AI → 插件）。</summary>
    public class McpToolCall
    {
        public string Id           = "";
        public string FunctionName = "";
        /// <summary>AI 传来的参数 JSON 字符串，如 <c>{"path":"LoginUI/Panel"}</c>。</summary>
        public string ArgumentsJson = "{}";
    }

    /// <summary>插件执行工具后向 AI 返回的结果（插件 → AI）。</summary>
    public class McpToolResult
    {
        public string ToolCallId = "";
        /// <summary>结果文本（JSON 或可读描述），AI 会把它当做上下文继续推理。</summary>
        public string Content  = "";
        public bool   IsError;

        public static McpToolResult Ok(string id, string content)   => new() { ToolCallId = id, Content = content };
        public static McpToolResult Fail(string id, string error)   => new() { ToolCallId = id, Content = $"执行失败：{error}", IsError = true };
    }

    /// <summary>AI 在工具调用模式下对一轮请求的完整响应。</summary>
    public class McpAIResponse
    {
        public bool    Success;
        public string? Error;

        /// <summary>true = AI 给出文字回复（无 tool_calls，循环终止）。</summary>
        public bool   IsTextReply;
        public string TextContent = "";

        /// <summary>AI 要调用的工具列表（IsTextReply = false 时有值）。</summary>
        public List<McpToolCall> ToolCalls = new();

        /// <summary>此轮 assistant 消息的原始 JSON 片段，用于追加到对话历史并发回 API。</summary>
        public string AssistantMessageJson = "";

        public float Duration;
        public int   TokensUsed;

        public static McpAIResponse TextOk(string text, string assistantJson, float dur, int tokens) => new()
        {
            Success = true, IsTextReply = true, TextContent = text,
            AssistantMessageJson = assistantJson, Duration = dur, TokensUsed = tokens
        };

        public static McpAIResponse ToolCallsOk(List<McpToolCall> calls, string assistantJson, float dur, int tokens) => new()
        {
            Success = true, IsTextReply = false, ToolCalls = calls,
            AssistantMessageJson = assistantJson, Duration = dur, TokensUsed = tokens
        };

        public static McpAIResponse Fail(string error) => new() { Success = false, Error = error };
    }
}
