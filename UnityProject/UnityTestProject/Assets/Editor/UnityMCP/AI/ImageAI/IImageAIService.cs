#nullable enable

using System.Threading.Tasks;

namespace UnityMCP.AI
{
    /// <summary>
    /// 图片 AI 服务的生成结果
    /// </summary>
    public class ImageAIResponse
    {
        public bool    Success  { get; set; }
        public string? Error    { get; set; }
        /// <summary>下载 / 解码后的原始图片字节（PNG/JPG）</summary>
        public byte[]? ImageBytes { get; set; }
        /// <summary>模型返回的原始 URL（若有）</summary>
        public string? ImageUrl   { get; set; }
        /// <summary>模型对 prompt 的修订版本（DALL-E 3 会返回）</summary>
        public string? RevisedPrompt { get; set; }
        public float   Duration  { get; set; }

        public static ImageAIResponse Ok(byte[] bytes, string? url = null, string? revised = null, float duration = 0) => new()
        {
            Success      = true,
            ImageBytes   = bytes,
            ImageUrl     = url,
            RevisedPrompt = revised,
            Duration     = duration
        };

        public static ImageAIResponse Fail(string error) => new()
        {
            Success = false,
            Error   = error
        };
    }

    /// <summary>
    /// 图片生成服务统一接口
    /// </summary>
    public interface IImageAIService
    {
        string DisplayName { get; }

        /// <summary>
        /// 根据文本 prompt 生成一张图片，返回图片字节数据。
        /// </summary>
        Task<ImageAIResponse> GenerateImageAsync(string prompt, ImageAIConfig config);
    }
}
