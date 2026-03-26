#nullable enable

using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMCP.Tools
{
    /// <summary>
    /// 判断单步 scene-ops 是否落在工作区内（供交互式执行前校验）。
    /// </summary>
    public static class SceneWorkspaceEvaluator
    {
        /// <summary>
        /// 若返回 false，表示超出工作区或无法自动判定为安全，需用户确认后再执行。
        /// </summary>
        public static bool IsWithinWorkspace(
            SceneOperationDto op,
            Scene scene,
            SceneWorkspaceSettings settings,
            out string summary)
        {
            summary = "";
            if (!settings.Enforce)
                return true;

            if (!scene.IsValid())
            {
                summary = "活动场景无效。";
                return false;
            }

            var root = NormalizeHierarchyPath(settings.HierarchyRoot);
            var prefabPrefix = SceneWorkspaceSettings.CanonicalAssetFolderPath(settings.PrefabAssetPrefix);
            var hasSubtreeRoot = !string.IsNullOrEmpty(root);
            var hasPrefabPrefix = !string.IsNullOrEmpty(prefabPrefix);
            var hasHierarchyCoverage = settings.HierarchyUseEntireActiveScene || hasSubtreeRoot;

            if (!hasHierarchyCoverage && !hasPrefabPrefix)
            {
                summary =
                    "已启用工作区限制，请勾选「当前活动场景（整场景）」或填写「层级根路径」，或填写「预制体路径前缀」。";
                return false;
            }

            // 整场景：不做子树前缀限制；仅子树根 + 非整场景时按 root 收敛。
            var applySubtreeCap = hasSubtreeRoot && !settings.HierarchyUseEntireActiveScene;

            var kind = NormalizeOp(op.op);
            return kind switch
            {
                "createempty" => CheckCreateEmpty(op, scene, root, applySubtreeCap, out summary),
                "createprimitive" => CheckCreatePrimitive(op, out summary),
                "setparent" => CheckSetParent(op, scene, root, applySubtreeCap, out summary),
                "addcomponent" => CheckPathOnly(op.path, "addComponent", scene, root, applySubtreeCap, out summary),
                "settransform" => CheckPathOnly(op.path, "setTransform", scene, root, applySubtreeCap, out summary),
                "instantiateprefab" => CheckInstantiatePrefab(op, scene, root, applySubtreeCap, prefabPrefix, hasPrefabPrefix,
                    out summary),
                "openscene" => CheckOpenScene(out summary),
                "savescene" => CheckSaveScene(out summary),
                "destroy" => CheckPathOnly(op.path, "destroy", scene, root, applySubtreeCap, out summary),
                "duplicate" => CheckPathOnly(op.path, "duplicate", scene, root, applySubtreeCap, out summary),
                "setactive" => CheckPathOnly(op.path, "setActive", scene, root, applySubtreeCap, out summary),
                "setlayer" => CheckPathOnly(op.path, "setLayer", scene, root, applySubtreeCap, out summary),
                "settag" => CheckPathOnly(op.path, "setTag", scene, root, applySubtreeCap, out summary),
                "setcomponentproperty" => CheckPathOnly(op.path, "setComponentProperty", scene, root, applySubtreeCap,
                    out summary),
                "setrecttransform" => CheckPathOnly(op.path, "setRectTransform", scene, root, applySubtreeCap, out summary),
                "setuitext" => CheckPathOnly(op.path, "setUiText", scene, root, applySubtreeCap, out summary),
                _ => FallbackUnknown(op, out summary)
            };
        }

        private static bool CheckOpenScene(out string summary)
        {
            summary = "";
            return true;
        }

        private static bool CheckSaveScene(out string summary)
        {
            summary = "";
            return true;
        }

        private static bool FallbackUnknown(SceneOperationDto op, out string summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"未知或空 op: \"{op.op}\"");
            sb.AppendLine("为安全起见需您确认是否执行。");
            summary = sb.ToString().TrimEnd();
            return false;
        }

        private static bool CheckPathOnly(
            string path,
            string opLabel,
            Scene scene,
            string root,
            bool applySubtreeCap,
            out string summary)
        {
            summary = "";
            if (!applySubtreeCap)
                return true;

            if (string.IsNullOrWhiteSpace(path))
            {
                summary = $"{opLabel}: path 为空。";
                return false;
            }

            var p = path.Trim();
            if (!IsUnderHierarchyRoot(p, root))
            {
                summary = $"{opLabel}: 目标层级 \"{p}\" 不在工作区根 \"{root}\" 下。";
                return false;
            }

            return true;
        }

        private static bool CheckCreatePrimitive(SceneOperationDto op, out string summary)
        {
            if (string.IsNullOrWhiteSpace(op.primitiveType))
            {
                summary = "createPrimitive: primitiveType 为空（可用值：Cube / Sphere / Capsule / Cylinder / Plane / Quad）。";
                return false;
            }
            summary = $"createPrimitive — {op.primitiveType}" +
                      (string.IsNullOrWhiteSpace(op.name) ? "" : $"，名称: {op.name}") +
                      $"，父: {(string.IsNullOrEmpty(op.parentPath) ? "（场景根）" : op.parentPath)}";
            return true;
        }

        private static bool CheckCreateEmpty(
            SceneOperationDto op,
            Scene scene,
            string root,
            bool applySubtreeCap,
            out string summary)
        {
            summary = "";
            if (string.IsNullOrWhiteSpace(op.name))
            {
                summary = "createEmpty: name 为空。";
                return false;
            }

            var parentSpec = string.IsNullOrWhiteSpace(op.parentPath) ? null : op.parentPath.Trim();
            var childName = op.name.Trim();

            if (!applySubtreeCap)
                return true;

            string childPath;
            if (parentSpec != null)
            {
                if (!TryResolveHierarchyRef(scene, parentSpec, out var parentPath, out var err))
                {
                    summary = $"createEmpty: 无法解析父节点 ({parentSpec}): {err}";
                    return false;
                }

                if (!IsUnderHierarchyRoot(parentPath, root))
                {
                    childPath = CombineHierarchyPath(parentPath, childName);
                    summary = $"createEmpty: 父路径 \"{parentPath}\" 不在工作区 \"{root}\" 下。\n将新建的物体路径: \"{childPath}\"";
                    return false;
                }

                childPath = CombineHierarchyPath(parentPath, childName);
            }
            else
                childPath = childName;

            if (!IsUnderHierarchyRoot(childPath, root))
            {
                summary = $"createEmpty: 新物体路径 \"{childPath}\" 不在工作区 \"{root}\" 下。";
                return false;
            }

            return true;
        }

        private static bool CheckSetParent(
            SceneOperationDto op,
            Scene scene,
            string root,
            bool applySubtreeCap,
            out string summary)
        {
            summary = "";
            if (!applySubtreeCap)
                return true;

            if (string.IsNullOrWhiteSpace(op.path) || string.IsNullOrWhiteSpace(op.newParentPath))
            {
                summary = "setParent: path 或 newParentPath 为空。";
                return false;
            }

            var childPath = op.path.Trim();
            if (!IsUnderHierarchyRoot(childPath, root))
            {
                summary = $"setParent: 子物体 \"{childPath}\" 不在工作区 \"{root}\" 下。";
                return false;
            }

            if (!TryResolveHierarchyRef(scene, op.newParentPath.Trim(), out var newParentPath, out var err))
            {
                summary = $"setParent: 无法解析新父节点: {err}";
                return false;
            }

            if (!IsUnderHierarchyRoot(newParentPath, root))
            {
                summary = $"setParent: 新父路径 \"{newParentPath}\" 不在工作区 \"{root}\" 下。（子: \"{childPath}\"）";
                return false;
            }

            return true;
        }

        private static bool CheckInstantiatePrefab(
            SceneOperationDto op,
            Scene scene,
            string root,
            bool applySubtreeCap,
            string prefabPrefix,
            bool hasPrefabPrefix,
            out string summary)
        {
            summary = "";
            if (string.IsNullOrWhiteSpace(op.prefabAssetPath))
            {
                summary = "instantiatePrefab: prefabAssetPath 为空。";
                return false;
            }

            var asset = op.prefabAssetPath.Trim().Replace('\\', '/');
            if (hasPrefabPrefix && !IsUnderAssetPrefix(asset, prefabPrefix))
            {
                summary =
                    $"instantiatePrefab: 预制体路径 \"{asset}\" 不以允许的前缀 \"{prefabPrefix}\" 开头。";
                return false;
            }

            if (!applySubtreeCap)
                return true;

            var parentSpec = string.IsNullOrWhiteSpace(op.parentPath) ? null : op.parentPath.Trim();
            if (parentSpec == null)
            {
                summary =
                    $"instantiatePrefab: 将在场景根实例化资源 \"{asset}\"，不在工作区层级 \"{root}\" 内（parentPath 为空）。";
                return false;
            }

            if (!TryResolveHierarchyRef(scene, parentSpec, out var parentPath, out var err))
            {
                summary = $"instantiatePrefab: 无法解析父节点 ({parentSpec}): {err}";
                return false;
            }

            if (!IsUnderHierarchyRoot(parentPath, root))
            {
                summary =
                    $"instantiatePrefab: 父路径 \"{parentPath}\" 不在工作区 \"{root}\" 下。\n预制体: \"{asset}\"";
                return false;
            }

            return true;
        }

        private static string CombineHierarchyPath(string? parentSpec, string childName)
        {
            if (string.IsNullOrWhiteSpace(parentSpec))
                return childName.Trim();
            return $"{parentSpec.Trim().TrimEnd('/')}/{childName.Trim()}";
        }

        private static bool TryResolveHierarchyRef(Scene scene, string spec, out string path, out string error)
        {
            path = "";
            error = "";
            if (string.Equals(spec, HierarchyLocator.ParentUsesSelection, StringComparison.OrdinalIgnoreCase))
            {
                var go = Selection.activeGameObject;
                if (go == null)
                {
                    error = "使用了 __selection__ 但未选中物体。";
                    return false;
                }

                path = HierarchyLocator.GetHierarchyPath(scene, go) ?? "";
                if (string.IsNullOrEmpty(path))
                {
                    error = "无法计算选中物体的层级路径。";
                    return false;
                }

                return true;
            }

            path = spec.Trim();
            return true;
        }

        private static bool IsUnderHierarchyRoot(string path, string root)
        {
            path = NormalizeHierarchyPath(path);
            root = NormalizeHierarchyPath(root);
            if (string.IsNullOrEmpty(path))
                return false;
            return string.Equals(path, root, StringComparison.Ordinal) ||
                   path.StartsWith(root + "/", StringComparison.Ordinal);
        }

        private static bool IsUnderAssetPrefix(string assetPath, string prefix)
        {
            assetPath = NormalizeAssetPrefix(assetPath);
            prefix = NormalizeAssetPrefix(prefix);
            return string.Equals(assetPath, prefix, StringComparison.OrdinalIgnoreCase) ||
                   assetPath.StartsWith(prefix.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeHierarchyPath(string? p)
        {
            if (string.IsNullOrWhiteSpace(p))
                return "";
            return p.Trim().Replace('\\', '/').Trim('/');
        }

        private static string NormalizeAssetPrefix(string? p)
        {
            if (string.IsNullOrWhiteSpace(p))
                return "";
            return p.Trim().Replace('\\', '/').TrimEnd('/');
        }

        private static string NormalizeOp(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            return raw.Trim().ToLowerInvariant().Replace("_", "");
        }

        /// <summary>供确认对话框展示的简短步骤说明。</summary>
        public static string DescribeOperation(SceneOperationDto op)
        {
            var kind = NormalizeOp(op.op);
            return kind switch
            {
                "createempty" => $"createEmpty — 名称: {op.name}, 父: {(string.IsNullOrEmpty(op.parentPath) ? "（场景根）" : op.parentPath)}",
                "createprimitive" => $"createPrimitive — {op.primitiveType}" +
                    (string.IsNullOrWhiteSpace(op.name) ? "" : $"，名称: {op.name}") +
                    $"，父: {(string.IsNullOrEmpty(op.parentPath) ? "（场景根）" : op.parentPath)}",
                "setparent" => $"setParent — {op.path} → 父: {op.newParentPath}",
                "addcomponent" => $"addComponent — {op.path} 添加 {op.typeName}",
                "settransform" => $"setTransform — {op.path}",
                "instantiateprefab" => $"instantiatePrefab — {op.prefabAssetPath} → 父: {(string.IsNullOrEmpty(op.parentPath) ? "（场景根）" : op.parentPath)}",
                "destroy" => $"destroy — {op.path}",
                "duplicate" => $"duplicate — {op.path}",
                "setactive" => $"setActive — {op.path} = {op.active}",
                "setlayer" => $"setLayer — {op.path}",
                "settag" => $"setTag — {op.path} → {op.gameObjectTag}",
                "openscene" => $"openScene — {op.sceneAssetPath} (additive={op.openSceneAdditive})",
                "savescene" => "saveScene — 保存当前活动场景到磁盘",
                "setcomponentproperty" => $"setComponentProperty — {op.path} :: {op.typeName} . {op.serializedPropertyPath}",
                "setrecttransform" => $"setRectTransform — {op.path}",
                "setuitext" => $"setUiText — {op.path}",
                _ => $"op={op.op}"
            };
        }
    }
}
