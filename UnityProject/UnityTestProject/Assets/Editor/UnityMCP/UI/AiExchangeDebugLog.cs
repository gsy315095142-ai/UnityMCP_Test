#nullable enable

using System;
using System.Text;
using UnityEditor;
using UnityMCP.AI;

namespace UnityMCP.UI
{
    /// <summary>
    /// 记录 AI 请求/响应摘要（供主窗口侧栏排查空内容、解析失败等）。
    /// 仅在编辑器主线程使用。内容写入 SessionState，脚本编译等域重载后仍保留。
    /// </summary>
    public static class AiExchangeDebugLog
    {
        private const string SessionKey = "UnityMCP.AiExchangeDebugLog.v1";
        private static readonly StringBuilder Buffer = new();
        private const int MaxTotalChars = 200_000;
        private const int MaxContentPreview = 16_000;

        /// <summary>每次追加或清空时递增，供 UI 判断是否与视图缓存同步（避免每帧覆盖导致无法拖选复制）。</summary>
        public static int Revision { get; private set; }

        private static void BumpRevision() => Revision++;

        static AiExchangeDebugLog()
        {
            var s = SessionState.GetString(SessionKey, "");
            if (s.Length > 0)
                Buffer.Append(s);
        }

        public static void Clear()
        {
            Buffer.Clear();
            SessionState.SetString(SessionKey, "");
            BumpRevision();
        }

        private static void PersistSession()
        {
            TrimIfNeeded();
            SessionState.SetString(SessionKey, Buffer.ToString());
        }

        public static string GetText() => Buffer.Length == 0 ? "" : Buffer.ToString();

        public static void AppendExchange(string phase, AIResponse response, string? note = null)
        {
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Buffer.Append('[').Append(now).Append("] ").AppendLine(phase);
            if (!string.IsNullOrEmpty(note))
                Buffer.AppendLine(note);
            Buffer.Append("Success=").Append(response.Success)
                .Append(", Duration=").Append(response.Duration.ToString("F2")).Append("s")
                .Append(", Tokens=").AppendLine(response.TokensUsed.ToString());
            if (!string.IsNullOrEmpty(response.Error))
                Buffer.Append("Error: ").AppendLine(response.Error);
            var c = response.Content ?? "";
            Buffer.Append("ContentLength=").AppendLine(c.Length.ToString());
            if (c.Length > 0)
            {
                Buffer.AppendLine("Content (preview):");
                if (c.Length <= MaxContentPreview)
                    Buffer.AppendLine(c);
                else
                {
                    Buffer.AppendLine(c.Substring(0, MaxContentPreview));
                    Buffer.AppendLine("… [已截断，总长度 " + c.Length + "]");
                }
            }

            Buffer.AppendLine("────────────────────────────────────────");
            PersistSession();
            BumpRevision();
        }

        public static void AppendException(string phase, Exception ex)
        {
            Buffer.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                .Append(phase).Append(" — 异常:").AppendLine();
            Buffer.AppendLine(ex.ToString());
            Buffer.AppendLine("────────────────────────────────────────");
            PersistSession();
            BumpRevision();
        }

        private static void TrimIfNeeded()
        {
            if (Buffer.Length <= MaxTotalChars)
                return;
            var cut = Buffer.Length - MaxTotalChars + 8_000;
            if (cut <= 0)
                return;
            Buffer.Remove(0, Math.Min(cut, Buffer.Length));
            Buffer.Insert(0, "… [较早日志已丢弃]\n");
        }
    }
}
