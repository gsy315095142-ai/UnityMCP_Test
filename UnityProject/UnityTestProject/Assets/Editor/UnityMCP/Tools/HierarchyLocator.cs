#nullable enable

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMCP.Tools
{
    /// <summary>
    /// 在活动场景中按层级路径查找 GameObject（A.0）。
    /// 路径格式：从根节点一级级拼接，例如 <c>Canvas/Panel/Buttons</c>；
    /// 每一级取<strong>同名首个</strong>子物体。
    /// </summary>
    public static class HierarchyLocator
    {
        /// <summary>
        /// 父物体解析关键字：使用当前 <see cref="Selection.activeGameObject"/> 作为父节点。
        /// </summary>
        public const string ParentUsesSelection = "__selection__";

        /// <summary>
        /// 在 <paramref name="scene"/> 中解析父节点。若 <paramref name="parentPath"/> 为空或 null，返回 null（表示场景根下新建）。
        /// </summary>
        public static SceneOperationResult TryResolveParent(string? parentPath, Scene scene, out GameObject? parent)
        {
            parent = null;
            if (string.IsNullOrWhiteSpace(parentPath))
                return SceneOperationResult.Ok();

            var p = parentPath.Trim();
            if (string.Equals(p, ParentUsesSelection, StringComparison.OrdinalIgnoreCase))
            {
                var sel = Selection.activeGameObject;
                if (sel == null)
                    return SceneOperationResult.Fail($"使用了 \"{ParentUsesSelection}\"，但未选中任何 GameObject。");
                parent = sel;
                return SceneOperationResult.Ok(sel);
            }

            if (!scene.IsValid())
                return SceneOperationResult.Fail("当前场景无效。");

            var found = FindByHierarchyPath(scene, p);
            if (found == null)
                return SceneOperationResult.Fail($"未找到层级路径: \"{p}\"（从活动场景根节点逐层匹配名称）。");
            parent = found;
            return SceneOperationResult.Ok(found);
        }

        /// <summary>
        /// 在活动场景中按路径查找物体。
        /// </summary>
        public static GameObject? FindByHierarchyPath(string hierarchyPath)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return null;
            return FindByHierarchyPath(scene, hierarchyPath);
        }

        /// <summary>
        /// 在指定场景中按路径查找物体。
        /// </summary>
        public static GameObject? FindByHierarchyPath(Scene scene, string hierarchyPath)
        {
            if (!scene.IsValid() || string.IsNullOrWhiteSpace(hierarchyPath))
                return null;

            var normalized = hierarchyPath.Trim();
            var parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return null;

            GameObject? current = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == parts[0])
                {
                    current = root;
                    break;
                }
            }

            if (current == null)
                return TryFindByShortNameFallback(scene, normalized);

            var t = current.transform;
            for (var i = 1; i < parts.Length; i++)
            {
                Transform? next = null;
                for (var c = 0; c < t.childCount; c++)
                {
                    var child = t.GetChild(c);
                    if (child.name == parts[i])
                    {
                        next = child;
                        break;
                    }
                }

                if (next == null)
                    return TryFindByShortNameFallback(scene, normalized);
                t = next;
            }

            return t.gameObject;
        }

        /// <summary>
        /// 路径匹配失败时的兜底：仅当输入不含 "/" 时，按短名匹配。
        /// </summary>
        private static GameObject? TryFindByShortNameFallback(Scene scene, string normalizedInput)
        {
            if (normalizedInput.IndexOf('/') >= 0)
                return null;

            // 用户要求的 fallback：尝试 GameObject.Find(name)。
            var byName = GameObject.Find(normalizedInput);
            if (byName != null && byName.scene == scene)
                return byName;

            return null;
        }

        /// <summary>
        /// 在活动场景中，返回物体相对场景根的层级路径（与 <see cref="FindByHierarchyPath"/> 规则一致）。
        /// </summary>
        public static string? GetHierarchyPath(Scene scene, GameObject go)
        {
            if (!scene.IsValid() || go == null)
                return null;
            if (!go.scene.IsValid() || go.scene != scene)
                return null;

            var names = new System.Collections.Generic.List<string>();
            Transform? t = go.transform;
            while (t != null)
            {
                names.Add(t.name);
                t = t.parent;
            }

            if (names.Count == 0)
                return null;

            names.Reverse();
            return string.Join("/", names);
        }
    }
}
