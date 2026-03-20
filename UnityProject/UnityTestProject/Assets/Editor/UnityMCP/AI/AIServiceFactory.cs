#nullable enable

using System;

namespace UnityMCP.AI
{
    /// <summary>
    /// AI 服务工厂。
    /// 根据配置创建对应的 AI 服务实例。
    /// </summary>
    public static class AIServiceFactory
    {
        /// <summary>
        /// 根据配置创建 AI 服务实例
        /// </summary>
        /// <param name="config">AI 服务配置</param>
        /// <returns>对应的 AI 服务实例</returns>
        /// <exception cref="NotSupportedException">当前服务商尚未实现</exception>
        public static IAIService Create(AIServiceConfig config)
        {
            return config.provider switch
            {
                AIProvider.Ollama => new OllamaService(config),
                AIProvider.Moonshot => new MoonshotOpenAiService(config),
                AIProvider.OpenAI => throw new NotSupportedException(
                    "OpenAI 服务将在后续版本实现，请使用 Ollama 或月之暗面（Moonshot）。"),
                AIProvider.Claude => throw new NotSupportedException(
                    "Claude 服务将在后续版本实现，请使用 Ollama 或月之暗面（Moonshot）。"),
                AIProvider.Azure => throw new NotSupportedException(
                    "Azure OpenAI 服务将在后续版本实现，请使用 Ollama 或月之暗面（Moonshot）。"),
                _ => throw new NotSupportedException($"未知的 AI 服务商: {config.provider}")
            };
        }

        /// <summary>
        /// 创建 AI 服务并测试连接
        /// </summary>
        public static IAIService CreateAndValidate(AIServiceConfig config)
        {
            var service = Create(config);
            return service;
        }
    }
}
