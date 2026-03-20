#nullable enable

namespace UnityMCP.Core
{
    /// <summary>
    /// LumiAI 产品名称与版本（窗口标题等统一引用）。
    /// </summary>
    public static class LumiAIProductInfo
    {
        public const string WindowBaseTitle = "LumiAI操控";
        public const string Version = "v1.0.26032001";

        /// <summary>标题栏显示用（含版本号）。</summary>
        public static string WindowTitleWithVersion => $"{WindowBaseTitle}  {Version}";
    }
}
