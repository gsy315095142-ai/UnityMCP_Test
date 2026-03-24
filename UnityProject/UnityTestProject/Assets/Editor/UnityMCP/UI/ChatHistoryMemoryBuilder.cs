#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityMCP.AI;
using UnityMCP.Tools;

namespace UnityMCP.UI
{
    /// <summary>
    /// 将窗口内 <see cref="ChatMessage"/> 转为带滑动窗口的多轮记忆（发往模型）。
    /// </summary>
    public static class ChatHistoryMemoryBuilder
    {
        /// <summary>单条 user/assistant 正文上限（字符）。</summary>
        public const int MaxCharsPerTurn = 8000;

        /// <summary>
        /// 当前请求之前应附带的 user/assistant 轮次（不含本轮用户句）。
        /// </summary>
        /// <param name="history">完整历史（末尾为本轮用户消息时可与 <paramref name="currentUserText"/> 对齐并剔除）。</param>
        /// <param name="maxPairs">最多保留几组「用户 + 助手」；0 表示关闭。</param>
        public static List<ChatMemoryTurn> BuildPriorTurns(
            IReadOnlyList<ChatMessage> history,
            int maxPairs,
            string currentUserText)
        {
            if (maxPairs <= 0 || history.Count == 0)
                return new List<ChatMemoryTurn>();

            var slice = history.ToList();
            if (slice.Count > 0 &&
                slice[^1].Role == ChatRole.User &&
                string.Equals(
                    (slice[^1].Content ?? "").Trim(),
                    (currentUserText ?? "").Trim(),
                    StringComparison.Ordinal))
                slice.RemoveAt(slice.Count - 1);

            var flat = new List<ChatMemoryTurn>();
            string? pendingUser = null;
            var assistantParts = new List<string>();

            void FlushPair()
            {
                if (string.IsNullOrEmpty(pendingUser))
                {
                    assistantParts.Clear();
                    return;
                }

                if (assistantParts.Count == 0)
                {
                    pendingUser = null;
                    return;
                }

                var merged = string.Join("\n---\n", assistantParts.Where(s => !string.IsNullOrWhiteSpace(s)));
                flat.Add(new ChatMemoryTurn("user", ClampContent(pendingUser)));
                flat.Add(new ChatMemoryTurn("assistant", ClampContent(merged)));
                pendingUser = null;
                assistantParts.Clear();
            }

            foreach (var m in slice)
            {
                if (m.Role == ChatRole.User)
                {
                    FlushPair();
                    var u = (m.Content ?? "").Trim();
                    if (!string.IsNullOrEmpty(u))
                        pendingUser = u;
                }
                else if (m.Role == ChatRole.Assistant)
                {
                    var s = SummarizeAssistant(m);
                    if (!string.IsNullOrWhiteSpace(s))
                        assistantParts.Add(s);
                }
            }

            FlushPair();

            var maxMessages = maxPairs * 2;
            if (flat.Count > maxMessages)
                flat = flat.Skip(flat.Count - maxMessages).ToList();

            return flat;
        }

        private static string SummarizeAssistant(ChatMessage m)
        {
            switch (m.Type)
            {
                case MessageTypeEnum.Text:
                {
                    var t = (m.Content ?? "").Trim();
                    if (t.StartsWith("⏳", StringComparison.Ordinal))
                        return "";
                    return StripSimpleRichText(t);
                }
                case MessageTypeEnum.CodeGenerated:
                    return $"[代码] 已生成 C# 草案，类名: {m.ScriptName}（可在窗口中预览或保存）。";
                case MessageTypeEnum.WaitingCompile:
                    return $"[联合生成] 脚本已保存: {m.SavedScriptPath}，等待编译后继续预制体。";
                case MessageTypeEnum.PrefabGenerated:
                    return $"[预制体] 已生成描述，预制体名: {m.PrefabName}。";
                case MessageTypeEnum.SceneOpsReady:
                    return "[场景操控] 已生成 unity-ops 步骤: " + SummarizeSceneOpsEnvelope(m.SceneOpsEnvelope);
                case MessageTypeEnum.AssetDeleteReady:
                    return "[删除资源] 待删除资源 " + (m.AssetDeletePaths?.Count ?? 0) + " 个（待确认）。";
                case MessageTypeEnum.AssetOpsReady:
                    return "[资源整理] 已生成 asset-ops 步骤: " + SummarizeAssetOpsEnvelope(m.AssetOpsEnvelope);
                case MessageTypeEnum.SuccessResult:
                {
                    var sb = new StringBuilder("[完成]");
                    if (!string.IsNullOrEmpty(m.SavedScriptPath))
                        sb.Append(" 脚本: ").Append(m.SavedScriptPath);
                    if (!string.IsNullOrEmpty(m.SavedPrefabPath))
                        sb.Append(" 预制体: ").Append(m.SavedPrefabPath);
                    if (m.Mode == GenerateMode.SceneOps)
                        sb.Append($" 场景操控已执行 {m.SceneOpsExecutedStepCount} 步。");
                    if (m.Mode == GenerateMode.AssetOps)
                        sb.Append($" 资源整理已执行 {m.AssetOpsExecutedStepCount} 步。");
                    return sb.ToString();
                }
                case MessageTypeEnum.Error:
                    return "[错误] " + ClampContent(m.ErrorMessage ?? m.Content ?? "", 2000);
                default:
                    return "";
            }
        }

        private static string SummarizeSceneOpsEnvelope(SceneOpsEnvelopeDto? env)
        {
            if (env?.operations == null || env.operations.Length == 0)
                return "（无步骤）";
            var parts = env.operations
                .Select(o => string.IsNullOrWhiteSpace(o.op) ? "?" : o.op.Trim());
            return string.Join(" → ", parts);
        }

        private static string SummarizeAssetOpsEnvelope(AssetOpsEnvelopeDto? env)
        {
            if (env?.operations == null || env.operations.Length == 0)
                return "（无步骤）";
            var parts = env.operations
                .Select(o => string.IsNullOrWhiteSpace(o.op) ? "?" : o.op.Trim());
            return string.Join(" → ", parts);
        }

        private static string StripSimpleRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return Regex.Replace(s, @"</?b>", "", RegexOptions.IgnoreCase).Trim();
        }

        private static string ClampContent(string s, int max = MaxCharsPerTurn)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "\n…（已截断）";
        }
    }
}
