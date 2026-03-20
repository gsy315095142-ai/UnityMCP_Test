#nullable enable

using System;
using UnityEditor;

namespace UnityMCP.Tools
{
    /// <summary>
    /// 执行场景操控前的快速校验（例如 __selection__ 与当前选中是否一致）。
    /// </summary>
    public static class SceneOpsPreflight
    {
        /// <summary>
        /// 若操作里出现 <see cref="HierarchyLocator.ParentUsesSelection"/> 但 Hierarchy 未选中物体，则不可执行。
        /// </summary>
        public static bool TryValidateSelectionPlaceholder(SceneOpsEnvelopeDto envelope, out string message)
        {
            message = "";
            if (envelope.operations == null || envelope.operations.Length == 0)
                return true;
            if (Selection.activeGameObject != null)
                return true;

            foreach (var op in envelope.operations)
            {
                if (op == null) continue;
                if (IsSelectionToken(op.parentPath) || IsSelectionToken(op.newParentPath))
                {
                    message =
                        "操作列表中使用了 \"__selection__\"，但 Hierarchy 中没有选中任何 GameObject。\n\n" +
                        "请任选其一：\n" +
                        "· 在 Hierarchy 中选中要挂载到的父物体（例如 Panel），再点「执行场景操作」；或\n" +
                        "· 重新向 AI 描述需求，并写明层级路径（如 Canvas/Panel），不要使用 __selection__。";
                    return false;
                }
            }

            return true;
        }

        private static bool IsSelectionToken(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;
            return string.Equals(s.Trim(), HierarchyLocator.ParentUsesSelection, StringComparison.OrdinalIgnoreCase);
        }
    }
}
