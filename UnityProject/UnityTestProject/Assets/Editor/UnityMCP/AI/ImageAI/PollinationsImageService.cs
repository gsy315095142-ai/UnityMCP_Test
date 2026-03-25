#nullable enable

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace UnityMCP.AI
{
    /// <summary>
    /// Pollinations.ai 图片生成服务。
    /// 无需 API Key，使用简单 GET 请求即可生成图片。
    /// 文档：https://image.pollinations.ai/prompt/{prompt}?model=flux&amp;width=1024&amp;height=1024
    /// </summary>
    public class PollinationsImageService : IImageAIService
    {
        public string DisplayName => "Pollinations.ai";

        // 显式配置 Handler：允许自动跟随重定向、启用 TLS 1.2+
        private static readonly HttpClient _http = BuildClient();

        private static HttpClient BuildClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect         = true,
                MaxAutomaticRedirections  = 5,
                // Unity Editor（Mono）有时对自签或新 CA 证书不信任，临时放开验证
                // 仅用于 Editor 工具，不影响运行时安全
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
        }

        public async Task<ImageAIResponse> GenerateImageAsync(string prompt, ImageAIConfig config)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var encodedPrompt = Uri.EscapeDataString(prompt);
                var model = string.IsNullOrWhiteSpace(config.modelName) ? "flux" : config.modelName.Trim();

                // 解析宽高
                int width = 1024, height = 1024;
                if (!string.IsNullOrWhiteSpace(config.imageSize))
                {
                    var parts = config.imageSize.Split('x');
                    if (parts.Length == 2)
                    {
                        int.TryParse(parts[0], out width);
                        int.TryParse(parts[1], out height);
                    }
                }

                var key    = config.apiKey?.Trim() ?? "";
                var hasKey = !string.IsNullOrEmpty(key);

                // ?key= 查询参数是 Pollinations 优先推荐的方式，同时加 Authorization 头作为备用
                var url = $"https://image.pollinations.ai/prompt/{encodedPrompt}" +
                          $"?model={Uri.EscapeDataString(model)}" +
                          $"&width={width}&height={height}" +
                          $"&enhance=false&private=true" +
                          $"&nologo={( hasKey ? "true" : "false" )}" +
                          ( hasKey ? $"&key={Uri.EscapeDataString(key)}" : "" );

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (compatible; UnityMCP-Editor/1.0)");
                request.Headers.TryAddWithoutValidation("Accept", "image/*,*/*");
                // Authorization 头：用 .NET 标准属性赋值，避免 TryAddWithoutValidation 对特殊头失效
                if (hasKey)
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

                var resp = await _http.SendAsync(request);
                if (!resp.IsSuccessStatusCode)
                {
                    var errBody = await resp.Content.ReadAsStringAsync();
                    return ImageAIResponse.Fail(
                        $"Pollinations.ai 返回错误 HTTP {(int)resp.StatusCode}：{errBody}");
                }

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                if (bytes == null || bytes.Length == 0)
                    return ImageAIResponse.Fail("Pollinations.ai 返回了空图片数据");

                sw.Stop();
                return ImageAIResponse.Ok(bytes, null, null, (float)sw.Elapsed.TotalSeconds);
            }
            catch (TaskCanceledException)
            {
                return ImageAIResponse.Fail("Pollinations.ai 请求超时（120 秒）。" +
                    "请检查网络连接，或该服务在当前网络环境下不可达。");
            }
            catch (HttpRequestException ex)
            {
                // 展开完整异常链，帮助定位根因（SSL 证书 / DNS / 连接拒绝等）
                var detail = BuildExceptionDetail(ex);
                return ImageAIResponse.Fail(
                    $"Pollinations.ai 网络请求失败。\n" +
                    $"可能原因：网络不可达、DNS 解析失败或 SSL 握手失败。\n" +
                    $"详情：{detail}");
            }
            catch (Exception ex)
            {
                return ImageAIResponse.Fail($"Pollinations.ai 请求异常：{BuildExceptionDetail(ex)}");
            }
        }

        /// <summary>递归展开内层异常，输出完整错误链。</summary>
        private static string BuildExceptionDetail(Exception ex)
        {
            var parts = new System.Collections.Generic.List<string>();
            var cur = ex;
            while (cur != null)
            {
                parts.Add(cur.Message);
                cur = cur.InnerException;
            }
            return string.Join(" → ", parts);
        }
    }
}
