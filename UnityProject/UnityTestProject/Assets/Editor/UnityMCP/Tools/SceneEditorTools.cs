#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityMCP.Core;
using UnityMCP.Generators;

namespace UnityMCP.Tools
{
    /// <summary>
    /// Unity 场景与层级原子操作（A.1），不依赖 AI。
    /// 所有方法应在主线程调用；若从未知线程调用请使用 <see cref="RunOnMainThread{T}"/>。
    /// 成功路径尽量注册 Undo，便于 Ctrl+Z。
    /// </summary>
    public static class SceneEditorTools
    {
        /// <summary>
        /// 若当前不在主线程，将 <paramref name="func"/> 投递到主线程执行；否则直接执行。
        /// </summary>
        public static SceneOperationResult RunOnMainThread(Func<SceneOperationResult> func)
        {
            if (MainThread.IsMainThread)
                return func();
            return MainThread.Run(func);
        }

        /// <summary>
        /// 在活动场景下创建空物体。父节点为 null 时挂在场景根。
        /// </summary>
        /// <param name="name">物体名称</param>
        /// <param name="parent">父物体，可为 null</param>
        public static SceneOperationResult CreateEmptyGameObject(string name, GameObject? parent = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return SceneOperationResult.Fail("物体名称不能为空。");

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return SceneOperationResult.Fail("没有有效的活动场景。");

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Create GameObject {name}");

            try
            {
                var go = new GameObject(name.Trim());
                Undo.RegisterCreatedObjectUndo(go, $"Create {name}");

                if (parent != null)
                {
                    Undo.SetTransformParent(go.transform, parent.transform, "Set Parent");
                    Undo.RecordObject(go.transform, "Reset local TRS");
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                }

                Undo.CollapseUndoOperations(group);
                return SceneOperationResult.Ok(go);
            }
            catch (Exception ex)
            {
                Undo.RevertAllDownToGroup(group);
                return SceneOperationResult.Fail($"创建物体失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 按层级路径创建空物体：<paramref name="parentPath"/> 为空则挂场景根；
        /// 为 <see cref="HierarchyLocator.ParentUsesSelection"/> 时用当前选中物体为父。
        /// </summary>
        public static SceneOperationResult CreateEmptyGameObjectAt(string name, string? parentPath)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return SceneOperationResult.Fail("没有有效的活动场景。");

            var pr = HierarchyLocator.TryResolveParent(parentPath, scene, out var parent);
            if (!pr.Success)
                return pr;

            return CreateEmptyGameObject(name, parent);
        }

        /// <summary>
        /// 设置子物体的父节点（本地坐标保持不变由 <paramref name="worldPositionStays"/> 控制，默认 false 与编辑器默认一致）。
        /// </summary>
        public static SceneOperationResult SetParent(GameObject child, GameObject newParent, bool worldPositionStays = false)
        {
            if (child == null) return SceneOperationResult.Fail("child 为空。");
            if (newParent == null) return SceneOperationResult.Fail("newParent 为空。");
            if (IsDescendantOf(newParent.transform, child.transform))
                return SceneOperationResult.Fail("不能将物体设置为自己的子孙的子节点。");

            Undo.RecordObject(child.transform, "Set Parent");
            child.transform.SetParent(newParent.transform, worldPositionStays);
            return SceneOperationResult.Ok(child);
        }

        /// <summary>
        /// 将预制体资源实例化到当前活动场景。
        /// </summary>
        public static SceneOperationResult InstantiatePrefab(
            string prefabAssetPath,
            GameObject? parent,
            Vector3? localPosition = null,
            Vector3? localEulerAngles = null,
            Vector3? localScale = null)
        {
            if (!ScenePathSecurity.TryValidatePrefabAssetPath(prefabAssetPath, out var path, out var err))
                return SceneOperationResult.Fail(err ?? "路径无效");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                return SceneOperationResult.Fail($"找不到预制体: {path}");

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return SceneOperationResult.Fail("没有有效的活动场景。");

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Instantiate {Path.GetFileName(path)}");

            try
            {
                GameObject instance;
                if (parent != null)
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                else
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

                if (instance == null)
                {
                    Undo.RevertAllDownToGroup(group);
                    return SceneOperationResult.Fail("PrefabUtility.InstantiatePrefab 返回 null。");
                }

                Undo.RegisterFullObjectHierarchyUndo(instance, "Instantiate Prefab");
                var t = instance.transform;
                if (localPosition.HasValue) t.localPosition = localPosition.Value;
                if (localEulerAngles.HasValue) t.localRotation = Quaternion.Euler(localEulerAngles.Value);
                if (localScale.HasValue) t.localScale = localScale.Value;

                Undo.CollapseUndoOperations(group);
                return SceneOperationResult.Ok(instance);
            }
            catch (Exception ex)
            {
                Undo.RevertAllDownToGroup(group);
                return SceneOperationResult.Fail($"实例化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 按路径解析父节点并实例化预制体（父路径规则同 <see cref="CreateEmptyGameObjectAt"/>）。
        /// </summary>
        public static SceneOperationResult InstantiatePrefabAtPath(
            string prefabAssetPath,
            string? parentPath,
            Vector3? localPosition = null,
            Vector3? localEulerAngles = null,
            Vector3? localScale = null)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return SceneOperationResult.Fail("没有有效的活动场景。");

            var pr = HierarchyLocator.TryResolveParent(parentPath, scene, out var parent);
            if (!pr.Success)
                return pr;

            return InstantiatePrefab(prefabAssetPath, parent, localPosition, localEulerAngles, localScale);
        }

        /// <summary>
        /// 为物体添加组件（无额外属性）。使用 <see cref="Undo.AddComponent"/> 以支持撤销。
        /// </summary>
        public static SceneOperationResult AddComponentToGameObject(GameObject go, string typeName)
        {
            if (go == null) return SceneOperationResult.Fail("GameObject 为空。");
            if (string.IsNullOrWhiteSpace(typeName))
                return SceneOperationResult.Fail("组件类型名为空。");

            var type = ComponentConfigurator.ResolveComponentTypeForTools(typeName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                return SceneOperationResult.Fail($"无法解析为组件类型: {typeName}");

            try
            {
                Undo.AddComponent(go, type);
                return SceneOperationResult.Ok(go);
            }
            catch (Exception ex)
            {
                return SceneOperationResult.Fail($"添加组件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 按层级路径查找物体并添加组件。
        /// </summary>
        public static SceneOperationResult AddComponentByHierarchyPath(string hierarchyPath, string typeName)
        {
            var go = HierarchyLocator.FindByHierarchyPath(hierarchyPath);
            if (go == null)
                return SceneOperationResult.Fail($"未找到物体: \"{hierarchyPath}\"");
            return AddComponentToGameObject(go, typeName);
        }

        /// <summary>
        /// 设置本地 Transform；仅对传入的非 null 字段生效（null = 不修改）。
        /// </summary>
        public static SceneOperationResult SetTransformLocal(
            GameObject go,
            Vector3? localPosition = null,
            Vector3? localEulerAngles = null,
            Vector3? localScale = null)
        {
            if (go == null) return SceneOperationResult.Fail("GameObject 为空。");

            Undo.RecordObject(go.transform, "Set Transform (Local)");
            var t = go.transform;
            if (localPosition.HasValue) t.localPosition = localPosition.Value;
            if (localEulerAngles.HasValue) t.localRotation = Quaternion.Euler(localEulerAngles.Value);
            if (localScale.HasValue) t.localScale = localScale.Value;
            return SceneOperationResult.Ok(go);
        }

        /// <summary>
        /// 按层级路径设置本地 Transform。
        /// </summary>
        public static SceneOperationResult SetTransformLocalByPath(
            string hierarchyPath,
            Vector3? localPosition = null,
            Vector3? localEulerAngles = null,
            Vector3? localScale = null)
        {
            var go = HierarchyLocator.FindByHierarchyPath(hierarchyPath);
            if (go == null)
                return SceneOperationResult.Fail($"未找到物体: \"{hierarchyPath}\"");
            return SetTransformLocal(go, localPosition, localEulerAngles, localScale);
        }

        private static bool IsDescendantOf(Transform possibleDescendant, Transform ancestor)
        {
            var p = possibleDescendant;
            while (p != null)
            {
                if (p == ancestor) return true;
                p = p.parent;
            }

            return false;
        }

    }
}
