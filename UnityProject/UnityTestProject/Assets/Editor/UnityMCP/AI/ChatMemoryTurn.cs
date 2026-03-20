#nullable enable

namespace UnityMCP.AI
{
    /// <summary>
    /// 发往聊天 API 的一轮对话（OpenAI / Ollama 风格的 role + content）。
    /// </summary>
    public sealed class ChatMemoryTurn
    {
        public string Role = "";
        public string Content = "";

        public ChatMemoryTurn()
        {
        }

        public ChatMemoryTurn(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }
}
