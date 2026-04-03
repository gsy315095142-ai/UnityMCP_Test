#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityMCP.Core;
using UnityMCP.Generators;

namespace UnityMCP.Tools
{
    /// <summary>
    /// Unity 场景与层级原子操作（A.1），不依赖 AI。
    /// 所有方法应在主线程调用；若从未知线程调用请使用 <see cref="RunOnMainThread{T}" />。
    /// 成功路径尽量注册 Undo，便于 Ctrl+Z。
    /// </summary>
    public static class SceneEditorTools
    {
        /// <summary>
        /// 若当前不在主线程，将 <paramref name="func" /> 投递到主线程执行；否则直接执行。
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

                    // 若父节点是 UI 层级（带 RectTransform），子节点也应使用 RectTransform
                    // new GameObject() 默认只有 Transform，需手动替换
                    if (parent.GetComponent<RectTransform>() != null && go.GetComponent<RectTransform>() == null)
                    {
                        Undo.AddComponent<RectTransform>(go);
                        // RectTransform 添加后重置锚点与位置为居中
                        var rt = go.GetComponent<RectTransform>();
                        if (rt != null)
                        {
                            rt.anchorMin = new Vector2(0.5f, 0.5f);
                            rt.anchorMax = new Vector2(0.5f, 0.5f);
                            rt.anchoredPosition = Vector2.zero;
                            rt.sizeDelta = new Vector2(100f, 100f);
                        }
                    }
                    else
                    {
                        Undo.RecordObject(go.transform, "Reset local TRS");
                        go.transform.localPosition = Vector3.zero;
                        go.transform.localRotation = Quaternion.identity;
                        go.transform.localScale = Vector3.one;
                    }
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
        /// 按层级路径创建空物体：<paramref name="parentPath" /> 为空则挂场景根；
        /// 为 <see cref="HierarchyLocator.ParentUsesSelection" /> 时用当前选中物体为父。
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
        /// 在场景中创建 Unity 内置 Primitive（Sphere / Cube / Capsule 等），可指定父节点和位姿。
        /// </summary>
        public static SceneOperationResult CreatePrimitiveAt(
            PrimitiveType primitiveType,
            string name,
            string? parentPath,
            Vector3? localPosition = null,
            Vector3? localEulerAngles = null,
            Vector3? localScale = null)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return SceneOperationResult.Fail("没有有效的活动场景。");

            var pr = HierarchyLocator.TryResolveParent(parentPath, scene, out var parent);
            if (!pr.Success) return pr;

            Undo.IncrementCurrentGroup();
            var groupName = $"Create {primitiveType}";
            Undo.SetCurrentGroupName(groupName);

            var go = GameObject.CreatePrimitive(primitiveType);
            go.name = string.IsNullOrWhiteSpace(name) ? primitiveType.ToString() : name;
            Undo.RegisterCreatedObjectUndo(go, groupName);

            if (parent != null)
                go.transform.SetParent(parent.transform, false);

            if (localPosition.HasValue) go.transform.localPosition = localPosition.Value;
            if (localEulerAngles.HasValue) go.transform.localEulerAngles = localEulerAngles.Value;
            if (localScale.HasValue) go.transform.localScale = localScale.Value;

            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            return SceneOperationResult.Ok(go);
        }

        /// <summary>
        /// 设置子物体的父节点（本地坐标保持不变由 <paramref name="worldPositionStays" /> 控制，默认 false 与编辑器默认一致）。
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
        /// 按路径解析父节点并实例化预制体（父路径规则同 <see cref="CreateEmptyGameObjectAt" />）。
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
        /// 为物体添加组件（无额外属性）。使用 <see cref="Undo.AddComponent" /> 以支持撤销。
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
                // Button / Toggle 等可交互 UI 组件需要 Image 才能有背景视觉
                // 若对象上尚未有 Image，自动预添加一个
                EnsureUiGraphicPrerequisite(go, type);

                if (go.GetComponent(type) == null)
                    Undo.AddComponent(go, type);

                return SceneOperationResult.Ok(go);
            }
            catch (Exception ex)
            {
                return SceneOperationResult.Fail($"添加组件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 若目标组件类型是 Button / Toggle / Scrollbar / Slider 等需要 Image 的可交互 UI 组件，
        /// 且当前对象上尚无任何 Graphic，则自动添加一个默认的 Image 组件（白色，可见）。
        /// </summary>
        private static void EnsureUiGraphicPrerequisite(GameObject go, Type componentType)
        {
            // 判断是否属于需要 targetGraphic 的可交互 UGUI 组件
            var uiSelectableTypeName = "UnityEngine.UI.Selectable";
            var selectableType = ComponentConfigurator.ResolveComponentTypeForTools(uiSelectableTypeName)
                                 ?? ComponentConfigurator.ResolveComponentTypeForTools("Selectable");
            if (selectableType == null) return;
            if (!selectableType.IsAssignableFrom(componentType)) return;

            // 检查是否已有 Graphic（Image / RawImage / Text 等）
            var graphicTypeName = "UnityEngine.UI.Graphic";
            var graphicType = ComponentConfigurator.ResolveComponentTypeForTools(graphicTypeName)
                              ?? ComponentConfigurator.ResolveComponentTypeForTools("Graphic");
            if (graphicType != null && go.GetComponent(graphicType) != null) return;

            // 自动添加 Image
            var imageType = ComponentConfigurator.ResolveComponentTypeForTools("Image");
            if (imageType == null) return;

            var img = Undo.AddComponent(go, imageType);
            // 设置默认颜色为可见的中灰蓝色（比纯白更具视觉反馈）
            if (img is UnityEngine.Behaviour b)
            {
                // 用反射设置 color，避免直接引用 UGUI 程序集
                var colorProp = img.GetType().GetProperty("color", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                colorProp?.SetValue(img, new Color(0.22f, 0.48f, 0.80f, 1f));
            }
        }

        /// <summary>
        /// 按层级路径查找物体并添加组件。
        /// </summary>
        public static SceneOperationResult AddComponentByHierarchyPath(string hierarchyPath, string typeName)
        {
            var go = HierarchyLocator.FindByHierarchyPath(hierarchyPath);
            if (go == null)
                return SceneOperationResult.Fail(BuildHierarchyPathNotFoundMessage(hierarchyPath));
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
                return SceneOperationResult.Fail(BuildHierarchyPathNotFoundMessage(hierarchyPath));
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

        public static SceneOperationResult DestroyGameObjectByHierarchyPath(string hierarchyPath)
        {
            var go = HierarchyLocator.FindByHierarchyPath(hierarchyPath);
            if (go == null)
                return SceneOperationResult.Fail(BuildHierarchyPathNotFoundMessage(hierarchyPath));
            Undo.DestroyObjectImmediate(go);
            return SceneOperationResult.Ok(go);
        }

        public static SceneOperationResult DuplicateGameObjectByHierarchyPath(string hierarchyPath, string? newName)
        {
            var go = HierarchyLocator.FindByHierarchyPath(hierarchyPath);
            if (go == null)
                return SceneOperationResult.Fail(BuildHierarchyPathNotFoundMessage(hierarchyPath));

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Duplicate GameObject");

            try
            {
                var clone = UnityEngine.Object.Instantiate(go, go.transform.parent);
                Undo.RegisterCreatedObjectUndo(clone, "Duplicate");
                clone.transform.SetSiblingIndex(go.transform.GetSiblingIndex() + 1);
                if (!string.IsNullOrWhiteSpace(newName))
                    clone.name = newName.Trim();
                else
                    clone.name = go.name + " (1)";

                Undo.CollapseUndoOperations(group);
                return SceneOperationResult.Ok(clone);
            }
            catch (Exception ex)
            {
                Undo.RevertAllDownToGroup(group);
                return SceneOperationResult.Fail($"复制失败: {ex.Message}");
            }
        }

        public static SceneOperationResult SetActiveByHierarchyPath(string hierarchyPath, bool active)
        {
            var go = HierarchyLocator.FindByHierarchyPath(hierarchyPath);
            if (go == null)
                return SceneOperationResult.Fail(BuildHierarchyPathNotFoundMessage(hierarchyPath));
            Undo.RecordObject(go, "SetActive");
            go.SetActive(active);
            return SceneOperationResult.Ok(go);
        }

        /// <summary>
        /// 设置物体在其父节点子列表中的排序索引。index=0 表示移到最前面。
        /// </summary>
        public static SceneOperationResult SetSiblingIndexByHierarchyPath(string hierarchyPath, int index)
        {
            var go = HierarchyLocator.FindByHierarchyPath(hierarchyPath);
            if (go == null)
                return SceneOperationResult.Fail(BuildHierarchyPathNotFoundMessage(hierarchyPath));

            var parent = go.transform.parent;
            if (parent == null)
                return SceneOperationResult.Fail($"物体 \"{hierarchyPath}\" 没有父节点，无法设置 SiblingIndex。");

            if (index < 0 || index > parent.childCount - 1)
                return SceneOperationResult.Fail(
                    $"siblingIndex 超出范围：{index}，父节点共有 {parent.childCount} 个子物体（有效范围 0~{parent.childCount - 1}）。");

            Undo.RecordObject(go.transform, "Set SiblingIndex");
            go.transform.SetSiblingIndex(index);
            return SceneOperationResult.Ok(go);
        }

        public static SceneOperationResult SetLayerByHierarchyPath(string hierarchyPath, int layerIndex, string? layerName)
        {
            var go = HierarchyLocator.FindByHierarchyPath(hierarchyPath);
            if (go == null)
                return SceneOperationResult.Fail($"未找到物体: \"{hierarchyPath}\"");

            int layer;
            if (layerIndex >= 0 && layerIndex < 32)
                layer = layerIndex;
            else if (!string.IsNullOrWhiteSpace(layerName))
            {
                layer = LayerMask.NameToLayer(layerName.Trim());
                if (layer < 0)
                    return SceneOperationResult.Fail($"未知 Layer 名称: {layerName}");
            }
            else
                return SceneOperationResult.Fail("setLayer 需要 layerIndex（0–31）或 layerName。");

            Undo.RecordObject(go, "Set Layer");
            go.layer = layer;
            return SceneOperationResult.Ok(go);
        }

        public static SceneOperationResult SetTagByHierarchyPath(string hierarchyPath, string tagName)
        {
            var go = HierarchyLocator.FindByHierarchyPath(hierarchyPath);
            if (go == null)
                return SceneOperationResult.Fail(BuildHierarchyPathNotFoundMessage(hierarchyPath));
            if (string.IsNullOrWhiteSpace(tagName))
                return SceneOperationResult.Fail("gameObjectTag 为空。");

            try
            {
                Undo.RecordObject(go, "Set Tag");
                go.tag = tagName.Trim();
                return SceneOperationResult.Ok(go);
            }
            catch (Exception ex)
            {
                return SceneOperationResult.Fail($"设置 Tag 失败（请确认 Tag 已在 Project Settings 中定义）: {ex.Message}");
            }
        }

        public static SceneOperationResult OpenSceneByAssetPath(string sceneAssetPath, bool additive)
        {
            if (!AssetPathSecurity.TryValidateGenericAssetPath(sceneAssetPath, out var path, out var err))
                return SceneOperationResult.Fail(err ?? "场景路径无效");
            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return SceneOperationResult.Fail("sceneAssetPath 须为 Assets 下 .unity 场景资源。");

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                return SceneOperationResult.Fail($"找不到场景资源: {path}");

            try
            {
                EditorSceneManager.OpenScene(
                    path,
                    additive ? OpenSceneMode.Additive : OpenSceneMode.Single);
                return SceneOperationResult.Ok();
            }
            catch (Exception ex)
            {
                return SceneOperationResult.Fail($"打开场景失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 将当前活动场景写入磁盘（须已保存为 Assets 下 .unity；未命名场景会失败并提示）。
        /// </summary>
        public static SceneOperationResult SaveActiveSceneToDisk()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid())
                return SceneOperationResult.Fail("没有有效的活动场景。");

            if (string.IsNullOrEmpty(scene.path))
                return SceneOperationResult.Fail(
                    "当前场景尚未保存为 .unity 文件。请先用 File → Save As… 将场景存到 Assets 目录，再执行保存。");

            try
            {
                if (!EditorSceneManager.SaveScene(scene))
                    return SceneOperationResult.Fail("SaveScene 返回 false（场景可能只读或路径无效）。");
                return SceneOperationResult.Ok();
            }
            catch (Exception ex)
            {
                return SceneOperationResult.Fail($"保存场景失败: {ex.Message}");
            }
        }

        public static SceneOperationResult SetComponentPropertyByHierarchyPath(
            string hierarchyPath,
            string componentTypeName,
            string serializedPropertyPath,
            string propertyValue)
        {
            var go = HierarchyLocator.FindByHierarchyPath(hierarchyPath);
            if (go == null)
                return SceneOperationResult.Fail(BuildHierarchyPathNotFoundMessage(hierarchyPath));
            if (string.IsNullOrWhiteSpace(componentTypeName))
                return SceneOperationResult.Fail("setComponentProperty 需要 typeName（组件类型）。");
            if (string.IsNullOrWhiteSpace(serializedPropertyPath))
                return SceneOperationResult.Fail("setComponentProperty 需要 serializedPropertyPath。");

            var type = ComponentConfigurator.ResolveComponentTypeForTools(componentTypeName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                return SceneOperationResult.Fail($"无法解析组件类型: {componentTypeName}");

            var comp = go.GetComponent(type);
            if (comp == null)
                return SceneOperationResult.Fail($"物体上未找到组件: {type.Name}");

            // 兼容：一些外部调用会以 typeName=Canvas 来设置 RectTransform 的 localScale。
            // 该属性并不属于 Canvas 组件本身，走 SerializedObject(Canvas) 通道会不生效。
            // 对此做显式分流，直接写入 RectTransform。
            if (TrySetCanvasRectTransformScaleByPropertyPath(
                    go, comp, serializedPropertyPath, propertyValue ?? "", out var directScaleResult))
                return directScaleResult;

            try
            {
                using var so = new SerializedObject(comp);
                var requestedPath = serializedPropertyPath.Trim();
                var prop = FindSerializedPropertyCompat(so, requestedPath, out var resolvedPath);
                if (prop == null)
                    return SceneOperationResult.Fail($"找不到 SerializedProperty: {serializedPropertyPath}");

                var assign = TryAssignSerializedProperty(prop, propertyValue ?? "");
                if (!assign.Success)
                    return SceneOperationResult.Fail(assign.Error ?? "属性赋值失败");

                var shouldGuardCanvasScale = IsCanvasRenderModeProperty(comp, requestedPath, resolvedPath);
                var rt = shouldGuardCanvasScale ? go.GetComponent<RectTransform>() : null;
                var oldScale = rt != null ? rt.localScale : Vector3.one;
                var beforeSnap = shouldGuardCanvasScale ? BuildCanvasRenderModeDebugSnapshot(go, comp, resolvedPath, "before") : "";

                so.ApplyModifiedProperties();
                var afterSnap = shouldGuardCanvasScale ? BuildCanvasRenderModeDebugSnapshot(go, comp, resolvedPath, "after") : "";

                // Bug guard:
                // 某些情况下修改 Canvas RenderMode 后会意外把 RectTransform.localScale 置为 (0,0,0)。
                // 这里做最小保护：若应用前是非零，应用后变全零，则恢复旧值。
                if (rt != null &&
                    !IsNearZero(oldScale) &&
                    IsNearZero(rt.localScale))
                {
                    Undo.RecordObject(rt, "Restore RectTransform Scale After RenderMode");
                    rt.localScale = oldScale;
                    Debug.LogWarning(
                        $"[UnityMCP] 检测到 Canvas RenderMode 修改后 localScale 被意外重置为 0，已自动恢复。路径：\"{hierarchyPath}\"，属性：\"{resolvedPath}\"");
                    Debug.LogWarning("[UnityMCP] CanvasRenderMode snapshot\n" + beforeSnap + "\n" + afterSnap + "\n" +
                                     BuildCanvasRenderModeDebugSnapshot(go, comp, resolvedPath, "after-restore"));
                }
                else if (shouldGuardCanvasScale)
                {
                    // 常规记录：保留 RenderMode 前后关键字段，便于排查异常写入来源。
                    Debug.Log("[UnityMCP] CanvasRenderMode snapshot\n" + beforeSnap + "\n" + afterSnap);
                }

                if (!string.Equals(resolvedPath, requestedPath, StringComparison.Ordinal))
                    Debug.Log($"[UnityMCP] setComponentProperty 已兼容路径：\"{requestedPath}\" -> \"{resolvedPath}\"");
                return SceneOperationResult.Ok(go);
            }
            catch (Exception ex)
            {
                return SceneOperationResult.Fail($"设置组件属性失败: {ex.Message}");
            }
        }

        private static bool TrySetCanvasRectTransformScaleByPropertyPath(
            GameObject go,
            Component comp,
            string serializedPropertyPath,
            string propertyValue,
            out SceneOperationResult result)
        {
            result = SceneOperationResult.Fail("unhandled");
            if (!(comp is Canvas))
                return false;

            var p = (serializedPropertyPath ?? "").Trim();
            if (!IsRectTransformScalePath(p))
                return false;

            var rt = go.GetComponent<RectTransform>();
            if (rt == null)
            {
                result = SceneOperationResult.Fail("Canvas 对象缺少 RectTransform，无法设置 localScale。");
                return true;
            }

            var scale = rt.localScale;
            if (IsScaleWholePath(p))
            {
                var v3 = SceneOpsVectorParser.TryParseVector3(propertyValue ?? "");
                if (v3 == null)
                {
                    result = SceneOperationResult.Fail($"localScale 需要 Vector3 格式（如 \"1,1,1\"），当前: {propertyValue}");
                    return true;
                }
                scale = v3.Value;
            }
            else
            {
                var axis = GetScaleAxisFromPath(p);
                if (axis < 0)
                {
                    result = SceneOperationResult.Fail($"无法识别 localScale 分量路径: {serializedPropertyPath}");
                    return true;
                }
                if (!float.TryParse((propertyValue ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                {
                    result = SceneOperationResult.Fail($"localScale 分量需要浮点数，当前: {propertyValue}");
                    return true;
                }

                switch (axis)
                {
                    case 0: scale.x = f; break;
                    case 1: scale.y = f; break;
                    case 2: scale.z = f; break;
                }
            }

            Undo.RecordObject(rt, "Set RectTransform.localScale");
            rt.localScale = scale;
            result = SceneOperationResult.Ok(go);
            return true;
        }

        private static bool IsRectTransformScalePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return IsScaleWholePath(path) ||
                   path.EndsWith(".x", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".y", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".z", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".m_X", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".m_Y", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".m_Z", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsScaleWholePath(string path) =>
            string.Equals(path, "m_LocalScale", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "localScale", StringComparison.OrdinalIgnoreCase);

        private static int GetScaleAxisFromPath(string path)
        {
            if (path.EndsWith(".x", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".m_X", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (path.EndsWith(".y", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".m_Y", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (path.EndsWith(".z", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".m_Z", StringComparison.OrdinalIgnoreCase))
                return 2;
            return -1;
        }

        /// <summary>
        /// 兼容常见的 SerializedProperty 路径差异（例如 m_X / m_Y 与 x / y）。
        /// </summary>
        private static SerializedProperty? FindSerializedPropertyCompat(
            SerializedObject so,
            string requestedPath,
            out string resolvedPath)
        {
            resolvedPath = requestedPath;
            var prop = so.FindProperty(requestedPath);
            if (prop != null) return prop;

            var candidate1 = requestedPath
                .Replace(".m_X", ".x", StringComparison.OrdinalIgnoreCase)
                .Replace(".m_Y", ".y", StringComparison.OrdinalIgnoreCase)
                .Replace(".m_Z", ".z", StringComparison.OrdinalIgnoreCase)
                .Replace(".m_W", ".w", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(candidate1, requestedPath, StringComparison.Ordinal))
            {
                prop = so.FindProperty(candidate1);
                if (prop != null)
                {
                    resolvedPath = candidate1;
                    return prop;
                }
            }

            var candidate2 = requestedPath
                .Replace(".x", ".m_X", StringComparison.OrdinalIgnoreCase)
                .Replace(".y", ".m_Y", StringComparison.OrdinalIgnoreCase)
                .Replace(".z", ".m_Z", StringComparison.OrdinalIgnoreCase)
                .Replace(".w", ".m_W", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(candidate2, requestedPath, StringComparison.Ordinal))
            {
                prop = so.FindProperty(candidate2);
                if (prop != null)
                {
                    resolvedPath = candidate2;
                    return prop;
                }
            }

            // 兜底：大小写不敏感匹配（Unity FindProperty 区分大小写）。
            prop = FindPropertyIgnoreCase(so, requestedPath, out var foundPath);
            if (prop != null)
            {
                resolvedPath = foundPath;
                return prop;
            }

            if (!string.Equals(candidate1, requestedPath, StringComparison.Ordinal))
            {
                prop = FindPropertyIgnoreCase(so, candidate1, out foundPath);
                if (prop != null)
                {
                    resolvedPath = foundPath;
                    return prop;
                }
            }

            if (!string.Equals(candidate2, requestedPath, StringComparison.Ordinal))
            {
                prop = FindPropertyIgnoreCase(so, candidate2, out foundPath);
                if (prop != null)
                {
                    resolvedPath = foundPath;
                    return prop;
                }
            }

            return null;
        }

        private static SerializedProperty? FindPropertyIgnoreCase(
            SerializedObject so,
            string targetPath,
            out string foundPath)
        {
            foundPath = targetPath;
            if (string.IsNullOrWhiteSpace(targetPath))
                return null;

            var it = so.GetIterator();
            if (!it.NextVisible(true))
                return null;

            do
            {
                if (string.Equals(it.propertyPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    foundPath = it.propertyPath;
                    return it.Copy();
                }
            } while (it.NextVisible(false));

            return null;
        }

        private static bool IsCanvasRenderModeProperty(Component comp, string requestedPath, string resolvedPath)
        {
            if (comp == null) return false;
            if (!(comp is Canvas)) return false;
            return IsRenderModePath(requestedPath) || IsRenderModePath(resolvedPath);
        }

        private static bool IsRenderModePath(string path) =>
            string.Equals(path?.Trim(), "m_RenderMode", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path?.Trim(), "renderMode", StringComparison.OrdinalIgnoreCase);

        private static bool IsNearZero(Vector3 v)
        {
            const float eps = 1e-6f;
            return Mathf.Abs(v.x) < eps && Mathf.Abs(v.y) < eps && Mathf.Abs(v.z) < eps;
        }

        private static string BuildCanvasRenderModeDebugSnapshot(
            GameObject go,
            Component comp,
            string propertyPath,
            string phase)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            sb.Append(phase);
            sb.Append("] path=");
            sb.Append(go != null ? go.name : "(null)");
            sb.Append(" property=");
            sb.Append(propertyPath);

            var canvas = comp as Canvas ?? go.GetComponent<Canvas>();
            if (canvas != null)
            {
                sb.Append(" | Canvas(renderMode=");
                sb.Append(canvas.renderMode);
                sb.Append(", worldCamera=");
                sb.Append(canvas.worldCamera != null ? canvas.worldCamera.name : "null");
                sb.Append(", planeDistance=");
                sb.Append(canvas.planeDistance.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(")");
            }

            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                sb.Append(" | RectTransform(scale=");
                sb.Append(FmtV3(rt.localScale));
                sb.Append(", anchoredPos=");
                sb.Append(Fmt(rt.anchoredPosition));
                sb.Append(", sizeDelta=");
                sb.Append(Fmt(rt.sizeDelta));
                sb.Append(", anchorMin=");
                sb.Append(Fmt(rt.anchorMin));
                sb.Append(", anchorMax=");
                sb.Append(Fmt(rt.anchorMax));
                sb.Append(")");
            }

            var scaler = go.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                sb.Append(" | CanvasScaler(uiScaleMode=");
                sb.Append(scaler.uiScaleMode);
                sb.Append(", refRes=");
                sb.Append(Fmt(scaler.referenceResolution));
                sb.Append(", match=");
                sb.Append(scaler.matchWidthOrHeight.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(", scaleFactor=");
                sb.Append(scaler.scaleFactor.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(")");
            }

            return sb.ToString();
        }

        private static string FmtV3(Vector3 v) =>
            $"{v.x.ToString("0.###", CultureInfo.InvariantCulture)},{v.y.ToString("0.###", CultureInfo.InvariantCulture)},{v.z.ToString("0.###", CultureInfo.InvariantCulture)}";

        private static string Fmt(Vector2 v) =>
            $"{v.x.ToString("0.###", CultureInfo.InvariantCulture)},{v.y.ToString("0.###", CultureInfo.InvariantCulture)}";

        public static SceneOperationResult SetRectTransformByHierarchyPath(
            string hierarchyPath,
            Vector2? anchorMin,
            Vector2? anchorMax,
            Vector2? anchoredPosition,
            Vector2? sizeDelta,
            Vector2? pivot,
            Vector2? offsetMin,
            Vector2? offsetMax)
        {
            var go = HierarchyLocator.FindByHierarchyPath(hierarchyPath);
            if (go == null)
                return SceneOperationResult.Fail(BuildHierarchyPathNotFoundMessage(hierarchyPath));

            var rt = go.GetComponent<RectTransform>();
            if (rt == null)
            {
                // 自动为 UI 物体补上 RectTransform（new GameObject() 在 Canvas 下不会自动添加）
                rt = Undo.AddComponent<RectTransform>(go);
                if (rt == null)
                    return SceneOperationResult.Fail($"物体没有 RectTransform 且无法自动添加: \"{hierarchyPath}\"");
            }

            Undo.RecordObject(rt, "Set RectTransform");
            if (anchorMin.HasValue) rt.anchorMin = anchorMin.Value;
            if (anchorMax.HasValue) rt.anchorMax = anchorMax.Value;
            if (anchoredPosition.HasValue) rt.anchoredPosition = anchoredPosition.Value;
            if (sizeDelta.HasValue) rt.sizeDelta = sizeDelta.Value;
            if (pivot.HasValue) rt.pivot = pivot.Value;
            if (offsetMin.HasValue) rt.offsetMin = offsetMin.Value;
            if (offsetMax.HasValue) rt.offsetMax = offsetMax.Value;

            return SceneOperationResult.Ok(go);
        }

        public static SceneOperationResult SetUiTextByHierarchyPath(string hierarchyPath, string text)
        {
            var go = HierarchyLocator.FindByHierarchyPath(hierarchyPath);
            if (go == null)
                return SceneOperationResult.Fail(BuildHierarchyPathNotFoundMessage(hierarchyPath));

            var uiText = go.GetComponent<Text>();
            if (uiText != null)
            {
                Undo.RecordObject(uiText, "Set UI Text");
                uiText.text = text ?? "";
                return SceneOperationResult.Ok(go);
            }

            var tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro")
                          ?? Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            if (tmpType != null)
            {
                var comp = go.GetComponent(tmpType);
                if (comp != null)
                {
                    var p = tmpType.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                    if (p != null && p.CanWrite)
                    {
                        Undo.RecordObject(comp as UnityEngine.Object, "Set TMP Text");
                        p.SetValue(comp, text ?? "");
                        return SceneOperationResult.Ok(go);
                    }
                }
            }

            return SceneOperationResult.Fail("物体上未找到 UnityEngine.UI.Text 或 TMPro.TMP_Text/TextMeshProUGUI。");
        }

        /// <summary>
        /// 将字符串标准化为仅保留字母和数字的形式，用于枚举名称模糊匹配。
        /// 例如："Screen Space - Overlay" → "ScreenSpaceOverlay"
        /// </summary>
        private static string NormalizeEnumName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static (bool Success, string? Error) TryAssignSerializedProperty(SerializedProperty prop, string raw)
        {
            var v = raw.Trim();
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        if (string.IsNullOrEmpty(v))
                            return (false, "propertyValue 为空。");
                        prop.boolValue = v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1";
                        return (true, null);
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                        if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                            return (false, $"无法解析为整数: {v}");
                        prop.intValue = i;
                        return (true, null);
                    case SerializedPropertyType.Float:
                        if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                            return (false, $"无法解析为浮点数: {v}");
                        prop.floatValue = f;
                        return (true, null);
                    case SerializedPropertyType.String:
                        prop.stringValue = v;
                        return (true, null);
                    case SerializedPropertyType.Enum:
                        if (string.IsNullOrEmpty(v))
                            return (false, "propertyValue 为空。");

                        var names = prop.enumNames;

                        // 1. 先尝试精确匹配枚举原始名称（大小写不敏感）
                        for (var n = 0; n < names.Length; n++)
                        {
                            if (string.Equals(names[n], v, StringComparison.OrdinalIgnoreCase))
                            {
                                prop.enumValueIndex = n;
                                return (true, null);
                            }
                        }

                        // 2. 尝试整数索引（带边界检查）
                        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ei))
                        {
                            if (ei >= 0 && ei < names.Length)
                            {
                                prop.enumValueIndex = ei;
                                return (true, null);
                            }
                            return (false, $"枚举索引超出范围: {ei}（有效范围 0~{names.Length - 1}，可选值: {string.Join(", ", names)}）");
                        }

                        // 3. 模糊匹配：去除空格、连字符等特殊字符后比较
                        var normalizedInput = NormalizeEnumName(v);
                        for (var n = 0; n < names.Length; n++)
                        {
                            if (string.Equals(NormalizeEnumName(names[n]), normalizedInput, StringComparison.OrdinalIgnoreCase))
                            {
                                prop.enumValueIndex = n;
                                return (true, null);
                            }
                        }

                        // 4. 提示可用值
                        var displayNames = prop.enumDisplayNames;
                        var hint = new StringBuilder("可选值: ");
                        for (var n = 0; n < names.Length; n++)
                        {
                            if (n > 0) hint.Append(", ");
                            hint.Append($"{names[n]}");
                            if (displayNames != null && displayNames.Length > n && !string.Equals(displayNames[n], names[n]))
                                hint.Append($"（显示名: {displayNames[n]}）");
                        }
                        return (false, $"枚举中无名称: {v}。{hint}");

                    case SerializedPropertyType.Color:
                        var c = ParseColor(v);
                        if (c == null) return (false, $"无法解析颜色: {v}");
                        prop.colorValue = c.Value;
                        return (true, null);
                    case SerializedPropertyType.Vector2:
                        var v2 = SceneOpsVectorParser.TryParseVector2(v);
                        if (v2 == null) return (false, $"无法解析 Vector2: {v}");
                        prop.vector2Value = v2.Value;
                        return (true, null);
                    case SerializedPropertyType.Vector3:
                        var v3 = SceneOpsVectorParser.TryParseVector3(v);
                        if (v3 == null) return (false, $"无法解析 Vector3: {v}");
                        prop.vector3Value = v3.Value;
                        return (true, null);
                    case SerializedPropertyType.Vector4:
                        var parts = v.Split(',');
                        if (parts.Length < 4)
                            return (false, $"Vector4 需要 4 个分量: {v}");
                        if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                            !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                            !float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ||
                            !float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                            return (false, $"无法解析 Vector4: {v}");
                        prop.vector4Value = new Vector4(x, y, z, w);
                        return (true, null);
                    case SerializedPropertyType.ObjectReference:
                        if (string.IsNullOrWhiteSpace(v) || v.Equals("null", StringComparison.OrdinalIgnoreCase))
                        {
                            prop.objectReferenceValue = null;
                            return (true, null);
                        }

                        if (!AssetPathSecurity.TryValidateGenericAssetPath(v, out var ap, out _))
                            return (false, $"ObjectReference 须为 Assets 路径或 null: {v}");

                        prop.objectReferenceValue = LoadObjectReferenceForProperty(prop, ap);
                        if (prop.objectReferenceValue == null)
                            return (false, $"无法加载资源（路径可能不存在，或类型不匹配）：{ap}");
                        return (true, null);
                    default:
                        return (false, $"暂不支持的属性类型: {prop.propertyType}");
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// 根据 SerializedProperty 声明的对象类型（prop.type 形如 "PPtr&lt;$Sprite&gt;"）
        /// 用最合适的方式加载资源。
        /// 若属性期望 Sprite 但贴图尚未被设为 Sprite 导入模式，自动重新导入。
        /// </summary>
        private static UnityEngine.Object? LoadObjectReferenceForProperty(SerializedProperty prop, string assetPath)
        {
            var propType = prop.type; // e.g. "PPtr<$Sprite>", "PPtr<$Texture2D>", "PPtr<$AudioClip>"

            if (propType.Contains("Sprite"))
            {
                // 尝试直接加载 Sprite 子资源
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sprite != null) return sprite;

                // 贴图未以 Sprite 模式导入 → 自动重新导入
                if (AssetImporter.GetAtPath(assetPath) is TextureImporter ti)
                {
                    ti.textureType      = TextureImporterType.Sprite;
                    ti.spriteImportMode = SpriteImportMode.Single;
                    ti.SaveAndReimport();
                    return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                }
                return null;
            }

            if (propType.Contains("Texture2D") || propType.Contains("Texture"))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

            if (propType.Contains("AudioClip"))
                return AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);

            if (propType.Contains("Material"))
                return AssetDatabase.LoadAssetAtPath<Material>(assetPath);

            if (propType.Contains("GameObject"))
                return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            // 兜底：通用加载
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        }

        private static Color? ParseColor(string raw)
        {
            var v = raw.Trim();
            if (v.Length > 0 && v[0] == '#' && ColorUtility.TryParseHtmlString(v, out var html))
                return html;

            var p = raw.Split(',');
            if (p.Length >= 3 &&
                float.TryParse(p[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) &&
                float.TryParse(p[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var g) &&
                float.TryParse(p[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            {
                var a = 1f;
                if (p.Length >= 4)
                    float.TryParse(p[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a);
                return new Color(r, g, b, a);
            }

            return null;
        }

        private static string BuildHierarchyPathNotFoundMessage(string hierarchyPath) =>
            $"未找到物体: \"{hierarchyPath}\"。请优先使用完整层级路径（例如 \"Canvas/LoginPanel/UsernameInput\"）；" +
            "短名仅作为兜底匹配，可能因重名导致命中不确定。";

    }
}
