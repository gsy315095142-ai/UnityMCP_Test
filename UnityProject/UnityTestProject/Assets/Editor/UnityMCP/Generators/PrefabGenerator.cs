#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Generators
{
    /// <summary>
    /// 预制体生成结果
    /// </summary>
    public class PrefabGenerateResult
    {
        /// <summary>是否成功</summary>
        public bool Success { get; set; }

        /// <summary>生成的预制体资源路径</summary>
        public string AssetPath { get; set; } = "";

        /// <summary>生成的预制体资源</summary>
        public GameObject? PrefabAsset { get; set; }

        /// <summary>错误信息（失败时）</summary>
        public string? Error { get; set; }

        /// <summary>生成过程中的警告列表</summary>
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// 预制体生成器。
    /// 根据 AI 返回的 PrefabDescription，创建 GameObject 层级结构并保存为预制体。
    /// </summary>
    public static class PrefabGenerator
    {
        private const string DEFAULT_PREFAB_PATH = "Assets/Prefabs/Generated";

        /// <summary>
        /// 根据描述生成预制体
        /// </summary>
        /// <param name="description">AI 生成的预制体描述</param>
        /// <param name="outputFolder">输出文件夹（Assets/... 格式），为 null 时使用默认路径</param>
        /// <returns>生成结果</returns>
        /// <param name="ensureScriptComponentClassName">
        /// 联合生成时：预制体保存后强制在根物体上挂载该 MonoBehaviour（类名），避免仅依赖 AI 的 JSON 漏挂或类型尚未解析失败。
        /// </param>
        public static PrefabGenerateResult Generate(
            PrefabDescription description,
            string? outputFolder = null,
            string? ensureScriptComponentClassName = null)
        {
            var result = new PrefabGenerateResult();
            var folder = outputFolder ?? DEFAULT_PREFAB_PATH;

            if (string.IsNullOrWhiteSpace(description.prefabName))
            {
                result.Error = "预制体名称不能为空";
                return result;
            }

            if (!EnsureDirectoryExists(folder))
            {
                result.Error = $"无法创建目录: {folder}";
                return result;
            }

            GameObject? rootGo = null;
            var scriptAttachDeferred = false;
            try
            {
                rootGo = BuildGameObject(description.rootObject, null, result.Warnings);
                if (rootGo == null)
                {
                    result.Error = "构建 GameObject 层级失败";
                    return result;
                }

                var assetPath = $"{folder}/{description.prefabName}.prefab";

                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (existing != null)
                {
                    result.Warnings.Add($"预制体 {description.prefabName}.prefab 已存在，将被覆盖");
                }

                if (!string.IsNullOrWhiteSpace(ensureScriptComponentClassName))
                {
                    if (!TryAttachScriptToRoot(rootGo, ensureScriptComponentClassName))
                    {
                        scriptAttachDeferred = true;
                        result.Warnings.Add(
                            $"未能即时挂载脚本「{ensureScriptComponentClassName}」（类尚未编译或不是 MonoBehaviour）。将在保存后于编译完成时重试挂载。");
                    }
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(rootGo, assetPath, out var success);

                if (!success || prefab == null)
                {
                    result.Error = $"保存预制体失败: {assetPath}";
                    return result;
                }

                if (scriptAttachDeferred && !string.IsNullOrWhiteSpace(ensureScriptComponentClassName))
                    ScheduleAttachScriptToPrefabAfterCompile(assetPath, ensureScriptComponentClassName);

                result.Success = true;
                result.AssetPath = assetPath;
                result.PrefabAsset = prefab;

                Debug.Log($"[UnityMCP] 预制体已生成: {assetPath}");
            }
            catch (System.Exception ex)
            {
                result.Error = $"生成预制体时出错: {ex.Message}";
            }
            finally
            {
                if (rootGo != null)
                    UnityEngine.Object.DestroyImmediate(rootGo);
            }

            return result;
        }

        /// <summary>
        /// 在脚本刚落盘后尝试把组件挂到已有预制体（先预制体后脚本流程）。
        /// </summary>
        public static void ScheduleAttachScriptToPrefabAfterCompile(string prefabAssetPath, string className)
        {
            if (string.IsNullOrEmpty(prefabAssetPath) || string.IsNullOrEmpty(className))
                return;

            EditorApplication.delayCall += () =>
            {
                AssetDatabase.Refresh();
                var w = new List<string>();
                if (TryAttachScriptToPrefabAsset(prefabAssetPath, className, w))
                    return;

                UnityEditor.Compilation.CompilationPipeline.compilationFinished += OnCompiled;
                void OnCompiled(object _)
                {
                    UnityEditor.Compilation.CompilationPipeline.compilationFinished -= OnCompiled;
                    AssetDatabase.Refresh();
                    var w2 = new List<string>();
                    if (!TryAttachScriptToPrefabAsset(prefabAssetPath, className, w2))
                    {
                        foreach (var x in w2)
                            Debug.LogWarning($"[UnityMCP] 仍未能挂载脚本到预制体: {x}");
                    }
                }
            };
        }

        /// <summary>
        /// 对已保存的预制体资源打开并挂载脚本（用于编译完成后补挂）。
        /// </summary>
        public static bool TryAttachScriptToPrefabAsset(string prefabAssetPath, string className, List<string>? warnings)
        {
            warnings ??= new List<string>();
            var type = ComponentConfigurator.ResolveComponentTypeForTools(className);
            if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                warnings.Add($"未找到可挂载类型「{className}」（请确认类名与命名空间、且脚本已编译）。");
                return false;
            }

            var root = PrefabUtility.LoadPrefabContents(prefabAssetPath);
            try
            {
                if (root.GetComponent(type) != null)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabAssetPath);
                    return true;
                }

                Undo.AddComponent(root, type);
                PrefabUtility.SaveAsPrefabAsset(root, prefabAssetPath);
                Debug.Log($"[UnityMCP] 已自动将 {type.Name} 挂到预制体: {prefabAssetPath}");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool TryAttachScriptToRoot(GameObject root, string className)
        {
            var type = ComponentConfigurator.ResolveComponentTypeForTools(className);
            if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
                return false;
            if (root.GetComponent(type) != null)
                return true;
            Undo.AddComponent(root, type);
            return true;
        }

        /// <summary>
        /// 获取预制体的完整输出路径
        /// </summary>
        public static string GetPrefabPath(string prefabName, string? outputFolder = null)
        {
            var folder = outputFolder ?? DEFAULT_PREFAB_PATH;
            return $"{folder}/{prefabName}.prefab";
        }

        /// <summary>
        /// 检查预制体是否已存在
        /// </summary>
        public static bool PrefabExists(string prefabName, string? outputFolder = null)
        {
            var path = GetPrefabPath(prefabName, outputFolder);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        }

        /// <summary>
        /// 递归构建 GameObject 层级
        /// </summary>
        private static GameObject? BuildGameObject(
            GameObjectDescription desc, Transform? parent, List<string> warnings)
        {
            GameObject go;
            if (!string.IsNullOrWhiteSpace(desc.primitive) &&
                TryParsePrimitive(desc.primitive, out var primitiveType))
            {
                go = GameObject.CreatePrimitive(primitiveType);
                if (!string.IsNullOrWhiteSpace(desc.name))
                    go.name = desc.name;
            }
            else
            {
                go = new GameObject(string.IsNullOrWhiteSpace(desc.name) ? "GameObject" : desc.name);
            }

            if (parent != null)
                go.transform.SetParent(parent, false);

            go.SetActive(desc.active);

            if (desc.tag != "Untagged" && !string.IsNullOrEmpty(desc.tag))
            {
                try
                {
                    go.tag = desc.tag;
                }
                catch
                {
                    warnings.Add($"标签 \"{desc.tag}\" 不存在，已跳过设置");
                }
            }

            if (desc.layer != 0)
                go.layer = desc.layer;

            ApplyTransform(go.transform, desc);

            foreach (var compDesc in desc.components)
            {
                var compResult = ComponentConfigurator.AddAndConfigure(go, compDesc);
                if (!compResult.Success)
                {
                    warnings.Add(compResult.Error ?? $"组件 {compDesc.type} 添加失败");
                }
                else if (compResult.Warnings.Count > 0)
                {
                    warnings.AddRange(compResult.Warnings);
                }
            }

            foreach (var childDesc in desc.children)
            {
                var child = BuildGameObject(childDesc, go.transform, warnings);
                if (child == null)
                {
                    warnings.Add($"子对象 {childDesc.name} 创建失败");
                }
            }

            return go;
        }

        private static bool TryParsePrimitive(string s, out PrimitiveType primitiveType)
        {
            primitiveType = default;
            if (string.IsNullOrWhiteSpace(s))
                return false;
            return Enum.TryParse(s.Trim(), true, out primitiveType);
        }

        /// <summary>
        /// 应用 Transform 数据
        /// </summary>
        private static void ApplyTransform(Transform transform, GameObjectDescription desc)
        {
            if (desc.position is { Length: >= 3 })
                transform.localPosition = new Vector3(desc.position[0], desc.position[1], desc.position[2]);

            if (desc.rotation is { Length: >= 3 })
                transform.localRotation = Quaternion.Euler(desc.rotation[0], desc.rotation[1], desc.rotation[2]);

            if (desc.scale is { Length: >= 3 })
                transform.localScale = new Vector3(desc.scale[0], desc.scale[1], desc.scale[2]);
        }

        private static bool EnsureDirectoryExists(string assetPath)
        {
            var fullPath = Path.Combine(Application.dataPath, "..", assetPath);
            fullPath = Path.GetFullPath(fullPath);

            try
            {
                if (!Directory.Exists(fullPath))
                    Directory.CreateDirectory(fullPath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
