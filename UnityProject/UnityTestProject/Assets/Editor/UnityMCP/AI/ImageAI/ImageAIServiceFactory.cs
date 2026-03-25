#nullable enable

using System;

namespace UnityMCP.AI
{
    public static class ImageAIServiceFactory
    {
        public static IImageAIService Create(ImageAIConfig config) => config.provider switch
        {
            ImageAIProvider.DallE        => new DalleImageService(),
            ImageAIProvider.StabilityAI  => new StabilityAIService(),
            ImageAIProvider.Pollinations => new PollinationsImageService(),
            _ => throw new NotSupportedException($"图片 AI 服务商 {config.provider} 不支持或未启用")
        };
    }
}
