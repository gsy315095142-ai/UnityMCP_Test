#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Core
{
    /// <summary>
    /// 项目上下文信息。
    /// 收集当前 Unity 项目的环境信息，用于构建 AI Prompt 的上下文。
    /// </summary>
    public class ProjectContext
    {
        public string UnityVersion { get; set; } = "";
        public string RenderPipeline { get; set; } = "";
        public string DefaultNamespace { get; set; } = "";
        public string ScriptOutputPath { get; set; } = "";
        public string PrefabOutputPath { get; set; } = "";
        public List<string> ExistingScripts { get; set; } = new();
        /// <summary>工程中 Assets 下所有 .prefab 资源路径（已排序），供「项目查询」与 Prompt 使用。</summary>
        public List<string> PrefabAssetPaths { get; set; } = new();
        public List<string> InstalledPackages { get; set; } = new();

        private const string DEFAULT_NAMESPACE = "UnityMCP.Generated";
        private const string DEFAULT_SCRIPT_PATH = "Assets/Scripts/Generated";
        private const string DEFAULT_PREFAB_PATH = "Assets/Prefabs/Generated";

        /// <summary>
        /// 收集当前项目的上下文信息
        /// </summary>
        public static ProjectContext Collect()
        {
            var context = new ProjectContext
            {
                UnityVersion = Application.unityVersion,
                RenderPipeline = DetectRenderPipeline(),
                DefaultNamespace = DEFAULT_NAMESPACE,
                ScriptOutputPath = DEFAULT_SCRIPT_PATH,
                PrefabOutputPath = DEFAULT_PREFAB_PATH,
                ExistingScripts = CollectExistingScripts(),
                PrefabAssetPaths = CollectPrefabAssetPaths(),
                InstalledPackages = CollectInstalledPackages()
            };

            return context;
        }

        /// <summary>
        /// 场景操控（unity-ops）等场景的轻量摘要：避免把完整脚本列表塞进 Prompt。
        /// </summary>
        public string ToPromptContextSceneOpsBrief()
        {
            return $@"## 项目摘要（场景操控）
- Unity {UnityVersion}，渲染管线: {RenderPipeline}
- 默认命名空间: {DefaultNamespace}；脚本目录: {ScriptOutputPath}；预制体目录: {PrefabOutputPath}
- 工程中 .prefab 资源共 {PrefabAssetPaths.Count} 个；脚本类名约 {ExistingScripts.Count} 个（操控层级时若需自定义组件，勿与现有类名冲突）";
        }

        /// <summary>
        /// 生成用于 AI Prompt 的上下文文本
        /// </summary>
        public string ToPromptContext()
        {
            var scriptList = ExistingScripts.Count > 0
                ? string.Join("\n", ExistingScripts.Take(50).Select(s => $"  - {s}"))
                : "  （暂无自定义脚本）";

            var packageList = InstalledPackages.Count > 0
                ? string.Join("\n", InstalledPackages.Select(p => $"  - {p}"))
                : "  （仅默认包）";

            const int maxPrefabsInPrompt = 200;
            var prefabTotal = PrefabAssetPaths.Count;
            var prefabList = prefabTotal > 0
                ? string.Join("\n", PrefabAssetPaths.Take(maxPrefabsInPrompt).Select(p => $"  - {p}"))
                  + (prefabTotal > maxPrefabsInPrompt
                      ? $"\n  … 共 {prefabTotal} 个预制体，此处仅列出前 {maxPrefabsInPrompt} 条路径"
                      : "")
                : "  （工程中暂无 .prefab 资源）";

            return $@"## 项目环境
- Unity 版本: {UnityVersion}
- 渲染管线: {RenderPipeline}
- 项目类型: VR 3D
- 目标平台: PC (Windows)

## 代码规范
- 默认命名空间: {DefaultNamespace}
- 命名约定: PascalCase（类、方法、属性）、camelCase（字段、参数）
- 注释语言: 中文
- 文档注释: 使用 XML 文档注释（/// <summary>）

## 已有脚本（避免命名冲突）
{scriptList}

## 工程中的预制体资源（Assets 下 .prefab，路径真实可查）
共 {prefabTotal} 个：
{prefabList}

## 已安装的包
{packageList}";
        }

        private static string DetectRenderPipeline()
        {
            var currentRP = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (currentRP == null)
                return "Built-in";

            var typeName = currentRP.GetType().Name;
            if (typeName.Contains("Universal") || typeName.Contains("URP"))
                return "URP";
            if (typeName.Contains("HD") || typeName.Contains("HDRP"))
                return "HDRP";

            return typeName;
        }

        private static List<string> CollectExistingScripts()
        {
            var scripts = new List<string>();
            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/Editor")) continue;
                if (path.Contains("/Plugins/")) continue;

                var fileName = Path.GetFileNameWithoutExtension(path);
                scripts.Add(fileName);
            }

            return scripts;
        }

        private static List<string> CollectPrefabAssetPaths()
        {
            var paths = new List<string>();
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        private static List<string> CollectInstalledPackages()
        {
            var packages = new List<string>();
            var manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");

            if (!File.Exists(manifestPath))
                return packages;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var startIdx = json.IndexOf("\"dependencies\"");
                if (startIdx < 0) return packages;

                var braceStart = json.IndexOf('{', startIdx);
                var braceEnd = json.IndexOf('}', braceStart);
                if (braceStart < 0 || braceEnd < 0) return packages;

                var depsBlock = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
                var lines = depsBlock.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim().Trim(',');
                    if (trimmed.StartsWith("\"com.unity.") && !trimmed.Contains("modules"))
                    {
                        var colonIdx = trimmed.IndexOf(':');
                        if (colonIdx > 0)
                        {
                            var pkgName = trimmed.Substring(1, colonIdx - 2);
                            packages.Add(pkgName);
                        }
                    }
                }
            }
            catch
            {
                // 解析失败不影响主流程
            }

            return packages;
        }
    }
}
