#nullable enable

using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMCP.Tools
{
    /// <summary>
    /// 按序执行 <see cref="SceneOpsEnvelopeDto.operations"/>（A.2）。
    /// </summary>
    public static class SceneOpsExecutor
    {
        /// <summary>
        /// 在活动场景中顺序执行；任一步失败则中止并返回失败下标。
        /// </summary>
        public static SceneOpsBatchResult Execute(SceneOpsEnvelopeDto envelope)
        {
            var result = new SceneOpsBatchResult { Success = true, StepsCompleted = 0, FailedAtIndex = -1 };

            if (envelope.operations == null || envelope.operations.Length == 0)
            {
                result.Success = false;
                result.Error = "operations 为空";
                result.FailedAtIndex = 0;
                return result;
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                result.Success = false;
                result.Error = "没有有效的活动场景";
                result.FailedAtIndex = 0;
                return result;
            }

            for (var i = 0; i < envelope.operations.Length; i++)
            {
                var step = envelope.operations[i];
                var one = ExecuteOne(step, scene);
                if (!one.Success)
                {
                    result.Success = false;
                    result.FailedAtIndex = i;
                    result.StepsCompleted = i;
                    result.Error = $"步骤 {i} ({step.op}): {one.Error}";
                    return result;
                }

                result.StepsCompleted = i + 1;
            }

            return result;
        }

        /// <summary>
        /// 仅执行单步（用于带工作区确认的交互式批处理）。
        /// </summary>
        public static SceneOperationResult ExecuteStep(SceneOperationDto op)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return SceneOperationResult.Fail("没有有效的活动场景");
            return ExecuteOne(op, scene);
        }

        private static SceneOperationResult ExecuteOne(SceneOperationDto op, Scene scene)
        {
            var kind = NormalizeOp(op.op);
            if (string.IsNullOrEmpty(kind))
                return SceneOperationResult.Fail("op 字段为空");

            return kind switch
            {
                "createempty" => ExecCreateEmpty(op, scene),
                "setparent" => ExecSetParent(op, scene),
                "addcomponent" => ExecAddComponent(op),
                "settransform" => ExecSetTransform(op),
                "instantiateprefab" => ExecInstantiatePrefab(op, scene),
                "destroy" => ExecDestroy(op),
                "duplicate" => ExecDuplicate(op),
                "setactive" => ExecSetActive(op),
                "setlayer" => ExecSetLayer(op),
                "settag" => ExecSetTag(op),
                "openscene" => ExecOpenScene(op),
                "setcomponentproperty" => ExecSetComponentProperty(op),
                "setrecttransform" => ExecSetRectTransform(op),
                "setuitext" => ExecSetUiText(op),
                _ => SceneOperationResult.Fail($"未知操作: {op.op}")
            };
        }

        private static string NormalizeOp(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            return raw.Trim().ToLowerInvariant().Replace("_", "");
        }

        private static SceneOperationResult ExecCreateEmpty(SceneOperationDto op, Scene scene)
        {
            if (string.IsNullOrWhiteSpace(op.name))
                return SceneOperationResult.Fail("createEmpty 需要 name");

            var parentPath = string.IsNullOrWhiteSpace(op.parentPath) ? null : op.parentPath.Trim();
            return SceneEditorTools.CreateEmptyGameObjectAt(op.name.Trim(), parentPath);
        }

        private static SceneOperationResult ExecSetParent(SceneOperationDto op, Scene scene)
        {
            if (string.IsNullOrWhiteSpace(op.path))
                return SceneOperationResult.Fail("setParent 需要 path");
            if (string.IsNullOrWhiteSpace(op.newParentPath))
                return SceneOperationResult.Fail("setParent 需要 newParentPath");

            var child = HierarchyLocator.FindByHierarchyPath(scene, op.path.Trim());
            if (child == null)
                return SceneOperationResult.Fail($"未找到物体: {op.path}");

            var pr = HierarchyLocator.TryResolveParent(op.newParentPath.Trim(), scene, out var parent);
            if (!pr.Success)
                return pr;
            if (parent == null)
                return SceneOperationResult.Fail("setParent：未能解析 newParentPath 为有效父节点");

            return SceneEditorTools.SetParent(child, parent, op.worldPositionStays);
        }

        private static SceneOperationResult ExecAddComponent(SceneOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path))
                return SceneOperationResult.Fail("addComponent 需要 path");
            if (string.IsNullOrWhiteSpace(op.typeName))
                return SceneOperationResult.Fail("addComponent 需要 typeName");

            return SceneEditorTools.AddComponentByHierarchyPath(op.path.Trim(), op.typeName.Trim());
        }

        private static SceneOperationResult ExecSetTransform(SceneOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path))
                return SceneOperationResult.Fail("setTransform 需要 path");

            var pos = SceneOpsVectorParser.TryParseVector3(op.localPosition);
            var euler = SceneOpsVectorParser.TryParseVector3(op.localEulerAngles);
            var scale = SceneOpsVectorParser.TryParseVector3(op.localScale);

            if (pos == null && euler == null && scale == null)
                return SceneOperationResult.Fail("setTransform 至少需要 localPosition / localEulerAngles / localScale 之一");

            return SceneEditorTools.SetTransformLocalByPath(
                op.path.Trim(),
                pos,
                euler,
                scale);
        }

        private static SceneOperationResult ExecInstantiatePrefab(SceneOperationDto op, Scene scene)
        {
            if (string.IsNullOrWhiteSpace(op.prefabAssetPath))
                return SceneOperationResult.Fail("instantiatePrefab 需要 prefabAssetPath");

            var parentPath = string.IsNullOrWhiteSpace(op.parentPath) ? null : op.parentPath.Trim();
            var pos = SceneOpsVectorParser.TryParseVector3(op.localPosition);
            var euler = SceneOpsVectorParser.TryParseVector3(op.localEulerAngles);
            var scale = SceneOpsVectorParser.TryParseVector3(op.localScale);

            return SceneEditorTools.InstantiatePrefabAtPath(
                op.prefabAssetPath.Trim(),
                parentPath,
                pos,
                euler,
                scale);
        }

        private static SceneOperationResult ExecDestroy(SceneOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path))
                return SceneOperationResult.Fail("destroy 需要 path");
            return SceneEditorTools.DestroyGameObjectByHierarchyPath(op.path.Trim());
        }

        private static SceneOperationResult ExecDuplicate(SceneOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path))
                return SceneOperationResult.Fail("duplicate 需要 path");
            var name = string.IsNullOrWhiteSpace(op.duplicateNewName) ? null : op.duplicateNewName.Trim();
            return SceneEditorTools.DuplicateGameObjectByHierarchyPath(op.path.Trim(), name);
        }

        private static SceneOperationResult ExecSetActive(SceneOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path))
                return SceneOperationResult.Fail("setActive 需要 path");
            return SceneEditorTools.SetActiveByHierarchyPath(op.path.Trim(), op.active);
        }

        private static SceneOperationResult ExecSetLayer(SceneOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path))
                return SceneOperationResult.Fail("setLayer 需要 path");
            return SceneEditorTools.SetLayerByHierarchyPath(
                op.path.Trim(),
                op.layerIndex,
                string.IsNullOrWhiteSpace(op.layerName) ? null : op.layerName.Trim());
        }

        private static SceneOperationResult ExecSetTag(SceneOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path))
                return SceneOperationResult.Fail("setTag 需要 path");
            return SceneEditorTools.SetTagByHierarchyPath(op.path.Trim(), op.gameObjectTag ?? "");
        }

        private static SceneOperationResult ExecOpenScene(SceneOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.sceneAssetPath))
                return SceneOperationResult.Fail("openScene 需要 sceneAssetPath");
            return SceneEditorTools.OpenSceneByAssetPath(op.sceneAssetPath.Trim(), op.openSceneAdditive);
        }

        private static SceneOperationResult ExecSetComponentProperty(SceneOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path))
                return SceneOperationResult.Fail("setComponentProperty 需要 path");
            return SceneEditorTools.SetComponentPropertyByHierarchyPath(
                op.path.Trim(),
                op.typeName ?? "",
                op.serializedPropertyPath ?? "",
                op.propertyValue ?? "");
        }

        private static SceneOperationResult ExecSetRectTransform(SceneOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path))
                return SceneOperationResult.Fail("setRectTransform 需要 path");

            var amin = SceneOpsVectorParser.TryParseVector2(op.anchorMin);
            var amax = SceneOpsVectorParser.TryParseVector2(op.anchorMax);
            var apos = SceneOpsVectorParser.TryParseVector2(op.anchoredPosition);
            var sd = SceneOpsVectorParser.TryParseVector2(op.sizeDelta);
            var pv = SceneOpsVectorParser.TryParseVector2(op.pivot);
            var omin = SceneOpsVectorParser.TryParseVector2(op.offsetMin);
            var omax = SceneOpsVectorParser.TryParseVector2(op.offsetMax);

            if (amin == null && amax == null && apos == null && sd == null && pv == null && omin == null && omax == null)
                return SceneOperationResult.Fail(
                    "setRectTransform 至少需要 anchorMin / anchorMax / anchoredPosition / sizeDelta / pivot / offsetMin / offsetMax 之一");

            return SceneEditorTools.SetRectTransformByHierarchyPath(
                op.path.Trim(),
                amin,
                amax,
                apos,
                sd,
                pv,
                omin,
                omax);
        }

        private static SceneOperationResult ExecSetUiText(SceneOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path))
                return SceneOperationResult.Fail("setUiText 需要 path");
            return SceneEditorTools.SetUiTextByHierarchyPath(op.path.Trim(), op.uiText ?? "");
        }
    }
}
