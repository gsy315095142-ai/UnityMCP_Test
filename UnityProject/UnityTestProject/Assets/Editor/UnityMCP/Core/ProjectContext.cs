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
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>非 Editor 下 MonoScript 的 .cs 路径（已排序），供删除资源与路径映射。</summary>
        public List<string> ScriptAssetPaths { get; set; } = new();

        /// <summary>与 <see cref="ScriptAssetPaths"/> 顺序一致，每项为文件名（不含扩展名），通常与 public class 名一致。</summary>
        public List<string> ExistingScripts { get; set; } = new();
        /// <summary>工程中 Assets 下所有 .prefab 资源路径（已排序），供「项目查询」与 Prompt 使用。</summary>
        public List<string> PrefabAssetPaths { get; set; } = new();
        public List<string> MaterialAssetPaths { get; set; } = new();
        public List<string> SceneAssetPaths { get; set; } = new();
        public List<string> Texture2DAssetPaths { get; set; } = new();
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
                ScriptAssetPaths = CollectScriptAssetPaths(out var scriptNames),
                ExistingScripts = scriptNames,
                PrefabAssetPaths = CollectPrefabAssetPaths(),
                MaterialAssetPaths = CollectAssetPathsByFilter("t:Material"),
                SceneAssetPaths = CollectAssetPathsByFilter("t:Scene"),
                Texture2DAssetPaths = CollectAssetPathsByFilter("t:Texture2D"),
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
- 工程中 .prefab 共 {PrefabAssetPaths.Count} 个；Material {MaterialAssetPaths.Count} 个；场景资产 {SceneAssetPaths.Count} 个；Texture2D {Texture2DAssetPaths.Count} 个；脚本类名约 {ExistingScripts.Count} 个（自定义组件勿与现有类名冲突）";
        }

        /// <summary>
        /// 生成用于 AI Prompt 的上下文文本
        /// </summary>
        /// <param name="omitScriptClassNameListForNamingConflict">
        /// 为 true 时不输出「仅类名」的脚本列表（用于删除资源 Prompt，避免与「脚本文件路径」表冲突）。
        /// </param>
        public string ToPromptContext(bool omitScriptClassNameListForNamingConflict = false)
        {
            string scriptSection;
            if (omitScriptClassNameListForNamingConflict)
            {
                // 删除资源 Prompt 已在更上方提供「脚本文件路径」表；此处不再重复「仅类名」列表，避免模型误判为无路径。
                scriptSection = "";
            }
            else
            {
                var scriptList = ScriptAssetPaths.Count > 0
                    ? string.Join("\n", ScriptAssetPaths.Take(50).Select(p =>
                    {
                        var n = Path.GetFileNameWithoutExtension(p);
                        return $"  - {n} → {p}";
                    }))
                    : "  （暂无自定义脚本）";
                scriptSection = $@"## 已有脚本（避免命名冲突）
每项格式为「类名/文件名 → Assets 下 .cs 路径」，删除或引用脚本时请使用右侧路径。
{scriptList}";
            }

            var packageList = InstalledPackages.Count > 0
                ? string.Join("\n", InstalledPackages.Select(p => $"  - {p}"))
                : "  （仅默认包）";

            const int maxPrefabsInPrompt = 200;
            const int maxOtherAssetsInPrompt = 120;
            var prefabTotal = PrefabAssetPaths.Count;
            var prefabList = prefabTotal > 0
                ? string.Join("\n", PrefabAssetPaths.Take(maxPrefabsInPrompt).Select(p => $"  - {p}"))
                  + (prefabTotal > maxPrefabsInPrompt
                      ? $"\n  … 共 {prefabTotal} 个预制体，此处仅列出前 {maxPrefabsInPrompt} 条路径"
                      : "")
                : "  （工程中暂无 .prefab 资源）";

            string BuildTruncatedList(List<string> paths, int maxLines, string emptyLabel)
            {
                var total = paths.Count;
                if (total == 0) return $"  （{emptyLabel}）";
                var lines = string.Join("\n", paths.Take(maxLines).Select(p => $"  - {p}"));
                if (total > maxLines)
                    lines += $"\n  … 共 {total} 条，此处仅列出前 {maxLines} 条";
                return lines;
            }

            var matList = BuildTruncatedList(MaterialAssetPaths, maxOtherAssetsInPrompt, "暂无 Material");
            var sceneList = BuildTruncatedList(SceneAssetPaths, maxOtherAssetsInPrompt, "暂无场景资产");
            var texList = BuildTruncatedList(Texture2DAssetPaths, maxOtherAssetsInPrompt, "暂无 Texture2D");

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

{scriptSection}

## 工程中的预制体资源（Assets 下 .prefab，路径真实可查）
共 {prefabTotal} 个：
{prefabList}

## 工程中的材质（t:Material，共 {MaterialAssetPaths.Count} 个）
{matList}

## 工程中的场景资产（.unity，共 {SceneAssetPaths.Count} 个）
{sceneList}

## 工程中的 Texture2D 贴图（共 {Texture2DAssetPaths.Count} 个）
{texList}

## 已安装的包
{packageList}";
        }

        /// <summary>
        /// 删除 Project 资源时专用：列出「类名/文件名 → .cs 完整路径」，避免模型只输出类名无法删除。
        /// </summary>
        public string ToPromptContextScriptPathsForDelete()
        {
            const int maxLines = 260;
            var total = ScriptAssetPaths.Count;
            if (total == 0)
                return @"

## 脚本文件路径（删除 .cs）
（当前扫描：**未找到**符合条件的运行时脚本：Assets 下 MonoScript，且排除 `Assets/Editor` 与 `Plugins`。若目标脚本在 Editor 插件目录，请用户在 Project 中手动删除。）";

            var lines = string.Join("\n", ScriptAssetPaths.Take(maxLines).Select(p =>
            {
                var name = Path.GetFileNameWithoutExtension(p);
                return $"  - {name} → {p}";
            }));
            if (total > maxLines)
                lines += $"\n  … 共 {total} 个脚本文件，此处仅列出前 {maxLines} 条";

            return $@"

## 脚本文件路径（删除 .cs 时必填下列路径）
共 {total} 个（文件名与类名通常一致；若用户只说类名，请在本表找到对应行并填入 **→** 右侧的 **Assets/.../xxx.cs**）：
{lines}";
        }

        /// <summary>
        /// 删除资源专用：短摘要 + 少量预制体路径。避免附带完整 ToPromptContext() 导致模型只「看见」Prefab/Material 而忽略脚本表。
        /// </summary>
        public string ToPromptContextAssetDeleteBrief()
        {
            const int maxPrefabsListed = 40;
            var prefabTotal = PrefabAssetPaths.Count;
            var prefabLines = prefabTotal > 0
                ? string.Join("\n", PrefabAssetPaths.Take(maxPrefabsListed).Select(p => $"  - {p}"))
                  + (prefabTotal > maxPrefabsListed ? $"\n  … 共 {prefabTotal} 个预制体，此处仅列前 {maxPrefabsListed} 条" : "")
                : "  （无）";

            return $@"## 工程摘要（删除资源专用）
- **运行时脚本 .cs**（不含 Assets/Editor、Plugins）：共 **{ScriptAssetPaths.Count}** 个，**完整路径仅见上方「脚本文件路径」一节**。
- 预制体 .prefab：共 {prefabTotal} 个；材质 {MaterialAssetPaths.Count}；场景 {SceneAssetPaths.Count}；Texture2D {Texture2DAssetPaths.Count}。
- **说明**：下方预制体列表**不包含** .cs 脚本路径；删除脚本时**必须**使用上方脚本表或用户给出的 `类名.cs` / `Assets/.../x.cs`，**不要**说「列表里只有 Prefab/Material」——脚本在上一节单独列出。

## 预制体路径（删 .prefab 时从这里选；最多 {maxPrefabsListed} 条）
{prefabLines}";
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

        private static List<string> CollectScriptAssetPaths(out List<string> namesOnly)
        {
            var paths = new List<string>();
            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/Editor")) continue;
                if (path.Contains("/Plugins/")) continue;

                paths.Add(path);
            }

            paths.Sort(PathComparer);
            namesOnly = paths.ConvertAll(p => Path.GetFileNameWithoutExtension(p));
            return paths;
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

        private static List<string> CollectAssetPathsByFilter(string typeFilter)
        {
            var paths = new List<string>();
            var guids = AssetDatabase.FindAssets(typeFilter, new[] { "Assets" });
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
