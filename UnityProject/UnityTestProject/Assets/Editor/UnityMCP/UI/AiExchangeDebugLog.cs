#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityMCP.AI;

namespace UnityMCP.UI
{
    /// <summary>
    /// 记录 AI 请求/响应摘要（供主窗口侧栏排查空内容、解析失败等）。
    /// 仅在编辑器主线程使用。内容写入 SessionState，脚本编译等域重载后仍保留。
    /// 每条日志作为独立 entry 存储，UI 可按条目渲染并提供单条复制按钮。
    /// </summary>
    public static class AiExchangeDebugLog
    {
        private const string SessionKey    = "UnityMCP.AiExchangeDebugLog.v1";
        private const int    MaxEntries    = 200;
        private const int    MaxContentPreview = 16_000;

        /// <summary>各条日志正文（不含尾部分隔线）。</summary>
        private static readonly List<string> _entries = new();

        /// <summary>每次追加或清空时递增，供 UI 判断是否与视图缓存同步。</summary>
        public static int Revision { get; private set; }

        private static void BumpRevision() => Revision++;

        // ── 初始化：从 SessionState 恢复 ──────────────────────────────────────
        static AiExchangeDebugLog()
        {
            var saved = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(saved)) return;

            // 按分隔线切割，每段为一条 entry
            const string sep = "────────────────────────────────────────";
            var parts = saved.Split(new[] { sep }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var entry = p.Trim('\n', '\r', ' ');
                if (!string.IsNullOrEmpty(entry))
                    _entries.Add(entry);
            }
        }

        // ── 公开 API ──────────────────────────────────────────────────────────

        /// <summary>所有日志条目（只读视图，从旧到新排列）。</summary>
        public static IReadOnlyList<string> GetEntries() => _entries;

        /// <summary>所有日志拼接为单一字符串（供"全部复制"使用）。</summary>
        public static string GetText()
        {
            if (_entries.Count == 0) return "";
            const string sep = "────────────────────────────────────────";
            var sb = new StringBuilder();
            foreach (var e in _entries)
            {
                sb.AppendLine(e);
                sb.AppendLine(sep);
            }
            return sb.ToString();
        }

        public static void Clear()
        {
            _entries.Clear();
            SessionState.SetString(SessionKey, "");
            BumpRevision();
        }

        public static void AppendExchange(string phase, AIResponse response, string? note = null)
        {
            var sb  = new StringBuilder();
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            sb.Append('[').Append(now).Append("] ").AppendLine(phase);
            if (!string.IsNullOrEmpty(note))
                sb.AppendLine(note);
            sb.Append("Success=").Append(response.Success)
              .Append(", Duration=").Append(response.Duration.ToString("F2")).Append("s")
              .Append(", Tokens=").AppendLine(response.TokensUsed.ToString());
            if (!string.IsNullOrEmpty(response.Error))
                sb.Append("Error: ").AppendLine(response.Error);
            var c = response.Content ?? "";
            sb.Append("ContentLength=").AppendLine(c.Length.ToString());
            if (c.Length > 0)
            {
                sb.AppendLine("Content (preview):");
                if (c.Length <= MaxContentPreview)
                    sb.Append(c);
                else
                {
                    sb.Append(c.Substring(0, MaxContentPreview));
                    sb.AppendLine("\n… [已截断，总长度 " + c.Length + "]");
                }
            }

            AddEntry(sb.ToString().TrimEnd());
        }

        public static void AppendException(string phase, Exception ex)
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
              .Append(phase).AppendLine(" — 异常:");
            sb.Append(ex.ToString());
            AddEntry(sb.ToString().TrimEnd());
        }

        // ── 内部辅助 ──────────────────────────────────────────────────────────

        private static void AddEntry(string entry)
        {
            _entries.Add(entry);

            // 超出上限时丢弃最旧的条目
            while (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);

            PersistSession();
            BumpRevision();
        }

        private static void PersistSession()
        {
            const string sep = "────────────────────────────────────────";
            var sb = new StringBuilder();
            foreach (var e in _entries)
            {
                sb.AppendLine(e);
                sb.AppendLine(sep);
            }
            SessionState.SetString(SessionKey, sb.ToString());
        }
    }
}
