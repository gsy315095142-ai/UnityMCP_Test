#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityMCP.AI;
using UnityMCP.Core;

namespace UnityMCP.Tools
{
    /// <summary>
    /// AI 输出的「待删除资源路径」JSON（Assets 下任意资源，须用户确认后执行）。
    /// </summary>
    [Serializable]
    public sealed class AssetDeleteEnvelopeDto
    {
        public string[]? assetPaths;
        public string? note;
    }

    /// <summary>
    /// 解析 <see cref="AssetDeleteEnvelopeDto"/> 的结果。
    /// </summary>
    public sealed class AssetDeleteParseResult
    {
        public bool Success { get; }
        public string? Error { get; }
        public AssetDeleteEnvelopeDto? Envelope { get; }
        public IReadOnlyList<string> NormalizedPaths { get; }
        public string RawJson { get; }

        private AssetDeleteParseResult(
            bool success,
            string? error,
            AssetDeleteEnvelopeDto? envelope,
            IReadOnlyList<string> paths,
            string rawJson)
        {
            Success = success;
            Error = error;
            Envelope = envelope;
            NormalizedPaths = paths;
            RawJson = rawJson;
        }

        public static AssetDeleteParseResult Ok(AssetDeleteEnvelopeDto env, IReadOnlyList<string> paths, string rawJson) =>
            new(true, null, env, paths, rawJson);

        public static AssetDeleteParseResult Fail(string error, string rawJson) =>
            new(false, error, null, Array.Empty<string>(), rawJson);
    }

    /// <summary>
    /// AI 输出的删除意图根对象（JSON 根字段）。
    /// </summary>
    [Serializable]
    public sealed class AssetDeleteIntentRootDto
    {
        public AssetDeleteIntentDto assetDeleteIntent;
    }

    /// <summary>
    /// 结构化删除意图（由插件解析为真实 Assets 路径）。
    /// </summary>
    [Serializable]
    public sealed class AssetDeleteIntentDto
    {
        public int version;
        public AssetDeleteIntentTargetDto[] targets;
        public string note;
    }

    [Serializable]
    public sealed class AssetDeleteIntentTargetDto
    {
        public string kind;
        public string nameHint;
        public string pathHint;
    }

    /// <summary>
    /// 解析 <see cref="AssetDeleteIntentRootDto"/> 并由插件解析工程内路径的结果。
    /// </summary>
    public sealed class AssetDeleteIntentResolveResult
    {
        public bool Success { get; }
        public string? Error { get; }
        public IReadOnlyList<string> ResolvedPaths { get; }
        public string Note { get; }
        public string RawJson { get; }
        public bool UsedLegacyAssetPathsFormat { get; }

        private AssetDeleteIntentResolveResult(
            bool success,
            string? error,
            IReadOnlyList<string> paths,
            string note,
            string rawJson,
            bool legacy)
        {
            Success = success;
            Error = error;
            ResolvedPaths = paths;
            Note = note;
            RawJson = rawJson;
            UsedLegacyAssetPathsFormat = legacy;
        }

        public static AssetDeleteIntentResolveResult Ok(
            IReadOnlyList<string> paths,
            string? note,
            string rawJson,
            bool legacy) =>
            new(true, null, paths, note ?? "", rawJson, legacy);

        public static AssetDeleteIntentResolveResult Fail(string error, string rawJson) =>
            new(false, error, Array.Empty<string>(), "", rawJson, false);
    }

    /// <summary>
    /// 从模型输出解析待删除的资源路径列表（不限于 .prefab）。
    /// </summary>
    public static class AssetDeleteParser
    {
        private static readonly Regex TrailingCommaRegex = new(@",(\s*[\]}])", RegexOptions.Compiled);

        /// <summary>
        /// 宽松匹配 <c>标识符.cs</c>：左侧用 ASCII 负向环视（避免插在英文标识符中间）；右侧避免 .csharp 等。
        /// </summary>
        private static readonly Regex LooseCsFileRegex = new(
            @"(?<![A-Za-z0-9_])([A-Za-z_][A-Za-z0-9_]*)\.cs(?![A-Za-z0-9_])",
            RegexOptions.Compiled);

        /// <summary>将用户/模型文本中的反斜杠路径、全角符号等规范为便于匹配的字符串。</summary>
        public static string NormalizeHintForAssetDelete(string? hint)
        {
            if (string.IsNullOrEmpty(hint)) return "";
            return hint.Replace('\\', '/').Trim();
        }

        private static bool IsRuntimeScriptPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith("Assets/Editor", StringComparison.Ordinal)) return false;
            if (path.IndexOf("/Plugins/", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        /// <summary>
        /// 与 <see cref="ProjectContext"/> 扫描规则一致；在预扫描列表为空时仍可用于按文件名查找。
        /// </summary>
        private static List<string> ListRuntimeMonoScriptPathsFromDatabase()
        {
            var paths = new List<string>();
            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsRuntimeScriptPath(path))
                    paths.Add(path);
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        /// <summary>
        /// 在 Assets 目录下按文件名搜索 .cs（不依赖 AssetDatabase 导入状态）。
        /// </summary>
        private static IEnumerable<string> FindRuntimeScriptPathsByFileNameOnDisk(string baseNameWithoutExt)
        {
            var dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
                yield break;

            string[] files;
            try
            {
                files = Directory.GetFiles(dataPath, baseNameWithoutExt + ".cs", SearchOption.AllDirectories);
            }
            catch
            {
                yield break;
            }

            var dataNorm = dataPath.Replace('\\', '/');
            foreach (var full in files)
            {
                var norm = full.Replace('\\', '/');
                if (!norm.StartsWith(dataNorm, StringComparison.OrdinalIgnoreCase))
                    continue;
                var rel = norm.Substring(dataNorm.Length).TrimStart('/');
                var assetPath = "Assets/" + rel;
                if (!IsRuntimeScriptPath(assetPath))
                    continue;
                yield return assetPath;
            }
        }

        private static IEnumerable<string> PathsToSearchForFileName(ProjectContext ctx, string baseName)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ctx.ScriptAssetPaths)
            {
                if (!string.Equals(Path.GetFileNameWithoutExtension(p), baseName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(p))
                    yield return p;
            }

            foreach (var path in ListRuntimeMonoScriptPathsFromDatabase())
            {
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), baseName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(path))
                    yield return path;
            }

            foreach (var path in FindRuntimeScriptPathsByFileNameOnDisk(baseName))
            {
                if (seen.Add(path))
                    yield return path;
            }

            foreach (var path in FindRuntimeScriptPathsByGetAllAssetPaths(baseName))
            {
                if (seen.Add(path))
                    yield return path;
            }
        }

        /// <summary>
        /// 最终兜底：与 FindAssets 缓存状态无关，直接扫工程内全部资源路径。
        /// </summary>
        private static IEnumerable<string> FindRuntimeScriptPathsByGetAllAssetPaths(string baseNameWithoutExt)
        {
            foreach (var ap in AssetDatabase.GetAllAssetPaths())
            {
                if (!ap.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!IsRuntimeScriptPath(ap))
                    continue;
                if (!string.Equals(Path.GetFileNameWithoutExtension(ap), baseNameWithoutExt, StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return ap;
            }
        }

        /// <summary>
        /// 合并预扫描与数据库扫描，避免「只填其中一种列表」时漏掉脚本名子串匹配。
        /// </summary>
        private static List<string> MergedRuntimeScriptPathsForSubstring(ProjectContext ctx)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var p in ctx.ScriptAssetPaths)
            {
                if (seen.Add(p))
                    list.Add(p);
            }

            foreach (var p in ListRuntimeMonoScriptPathsFromDatabase())
            {
                if (seen.Add(p))
                    list.Add(p);
            }

            return list;
        }

        /// <summary>
        /// 当子串列表里尚未出现某路径时，仍按「已有类名」在 hint 中出现，走 <see cref="PathsToSearchForFileName"/>（含磁盘兜底）。
        /// </summary>
        private static void TryAddMatchesFromExistingScriptNames(ProjectContext ctx, string userPrompt, HashSet<string> result)
        {
            if (ctx.ExistingScripts == null || ctx.ExistingScripts.Count == 0)
                return;
            foreach (var className in ctx.ExistingScripts)
            {
                if (string.IsNullOrEmpty(className) || className.Length < 2)
                    continue;
                if (userPrompt.IndexOf(className, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                foreach (var p in PathsToSearchForFileName(ctx, className))
                {
                    if (AssetExistsForDelete(p))
                        result.Add(p);
                }
            }
        }

        /// <summary>
        /// 判断 Assets 路径是否可视为可删除资源（.cs 会尝试 MonoScript 与磁盘文件，避免仅 Load&lt;Object&gt; 失败导致误拒）。
        /// </summary>
        public static bool AssetExistsForDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                return false;
            // 资源库已登记即可删（编译失败、Load 失败时 Object 仍可能为 null）
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                return true;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                return true;
            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                if (AssetDatabase.LoadAssetAtPath<MonoScript>(path) != null)
                    return true;
                var rel = path.Substring("Assets/".Length);
                var full = Path.Combine(Application.dataPath, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full))
                    return true;
            }

            return false;
        }

        public static AssetDeleteParseResult Parse(string textOrAiOutput)
        {
            if (string.IsNullOrWhiteSpace(textOrAiOutput))
                return AssetDeleteParseResult.Fail("输入为空", "");

            var json = ResponseParser.ExtractJsonFromModelOutput(textOrAiOutput);
            if (string.IsNullOrWhiteSpace(json))
                return AssetDeleteParseResult.Fail(
                    "无法提取 JSON。请使用 ```json 代码块，或输出含 assetDeleteIntent 或 assetPaths 的对象。",
                    textOrAiOutput);

            json = RemoveTrailingCommas(json.Trim());

            AssetDeleteEnvelopeDto? dto;
            try
            {
                dto = JsonUtility.FromJson<AssetDeleteEnvelopeDto>(json);
            }
            catch (Exception ex)
            {
                return AssetDeleteParseResult.Fail($"JSON 反序列化失败: {ex.Message}", json);
            }

            if (dto == null)
                return AssetDeleteParseResult.Fail("JSON 反序列化结果为 null", json);

            if (dto.assetPaths == null || dto.assetPaths.Length == 0)
                return AssetDeleteParseResult.Fail("assetPaths 不能为空（若使用新版协议请输出 assetDeleteIntent.targets）", json);

            var normalized = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in dto.assetPaths)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                if (!AssetPathSecurity.TryValidateGenericAssetPath(raw, out var path, out _))
                    continue;
                if (!AssetExistsForDelete(path))
                    continue;
                if (seen.Add(path))
                    normalized.Add(path);
            }

            if (normalized.Count == 0)
                return AssetDeleteParseResult.Fail(
                    "assetPaths 中无有效的 Assets 资源路径（已过滤非法或不存在项）。",
                    json);

            return AssetDeleteParseResult.Ok(dto, normalized, json);
        }

        /// <summary>
        /// 不依赖 AI：从用户原文中解析 <c>Assets/.../x.cs</c> 或 <c>类名.cs</c>（C# 标识符），仅在工程扫描列表中存在且资源可加载时返回。
        /// </summary>
        public static List<string> ResolveScriptPathsDeterministic(ProjectContext ctx, string hint)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            hint = NormalizeHintForAssetDelete(hint);
            if (string.IsNullOrWhiteSpace(hint))
                return new List<string>();

            foreach (Match m in Regex.Matches(hint, @"Assets[/\\][^\s""'`]+\.cs", RegexOptions.IgnoreCase))
            {
                var raw = m.Value.TrimEnd('.', ',', '，', '；', ';', '）', ')');
                if (AssetPathSecurity.TryValidateGenericAssetPath(raw, out var path, out _) && AssetExistsForDelete(path))
                    result.Add(path);
            }

            foreach (var baseName in CollectCsBaseNamesFromHint(hint))
            {
                foreach (var p in PathsToSearchForFileName(ctx, baseName))
                {
                    if (AssetExistsForDelete(p))
                        result.Add(p);
                }
            }

            return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 当模型未输出有效路径时：先走 <see cref="ResolveScriptPathsDeterministic"/>，再按「文件名≈类名」子串在工程扫描路径中宽松匹配。
        /// </summary>
        public static List<string> ResolveScriptPathsFromUserPrompt(ProjectContext ctx, string userPrompt)
        {
            userPrompt = NormalizeHintForAssetDelete(userPrompt);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ResolveScriptPathsDeterministic(ctx, userPrompt))
                result.Add(p);

            if (string.IsNullOrWhiteSpace(userPrompt))
                return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

            var pathsForSubstring = MergedRuntimeScriptPathsForSubstring(ctx);

            foreach (var p in pathsForSubstring)
            {
                var name = Path.GetFileNameWithoutExtension(p);
                if (string.IsNullOrEmpty(name))
                    continue;
                if (userPrompt.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 && AssetExistsForDelete(p))
                    result.Add(p);
            }

            if (result.Count == 0)
                TryAddMatchesFromExistingScriptNames(ctx, userPrompt, result);

            if (result.Count == 0)
            {
                foreach (var p in ResolveScriptPathsFromAllAssetsOnly(userPrompt))
                    result.Add(p);
            }

            return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 仅从 hint 中提取 <c>类名.cs</c>，用 <see cref="AssetDatabase.GetAllAssetPaths"/> 按文件名解析（不依赖 ProjectContext 预扫描）。
        /// </summary>
        public static List<string> ResolveScriptPathsFromAllAssetsOnly(string hint)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            hint = NormalizeHintForAssetDelete(hint);
            if (string.IsNullOrWhiteSpace(hint))
                return new List<string>();

            foreach (var baseName in CollectCsBaseNamesFromHint(hint))
            {
                foreach (var ap in FindRuntimeScriptPathsByGetAllAssetPaths(baseName))
                {
                    if (AssetExistsForDelete(ap))
                        result.Add(ap);
                }
            }

            return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 从文本中提取「要删的脚本」文件名（不含 .cs）：正则 + 按空白分词识别 xxx.cs。
        /// </summary>
        private static IEnumerable<string> CollectCsBaseNamesFromHint(string hint)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in LooseCsFileRegex.Matches(hint))
            {
                var n = m.Groups[1].Value;
                if (!string.IsNullOrEmpty(n) && seen.Add(n))
                    yield return n;
            }

            foreach (var token in Regex.Split(hint, @"\s+"))
            {
                var t = token.Trim('\"', '\'', '，', ',', '。', '；', ';', '）', ')', ']', '}', '（', '(');
                if (t.Length < 5 || !t.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;
                var n = Path.GetFileNameWithoutExtension(t);
                if (string.IsNullOrEmpty(n) || n.Length < 1)
                    continue;
                if (!char.IsLetter(n[0]) && n[0] != '_')
                    continue;
                if (seen.Add(n))
                    yield return n;
            }

            // JSON / 无空格粘连：..."ObjectColorChanger.cs"...
            foreach (Match m in Regex.Matches(hint, @"[A-Za-z_][A-Za-z0-9_]*\.cs", RegexOptions.IgnoreCase))
            {
                var n = Path.GetFileNameWithoutExtension(m.Value);
                if (string.IsNullOrEmpty(n) || n.Length < 2)
                    continue;
                if (seen.Add(n))
                    yield return n;
            }
        }

        /// <summary>
        /// 解析 AI 输出的 <see cref="AssetDeleteIntentRootDto"/>，并由插件将意图解析为工程内可删除路径；失败时回退旧版 <c>assetPaths</c>。
        /// </summary>
        public static AssetDeleteIntentResolveResult ParseAndResolveDeleteIntent(string textOrAiOutput, ProjectContext ctx)
        {
            if (string.IsNullOrWhiteSpace(textOrAiOutput))
                return AssetDeleteIntentResolveResult.Fail("输入为空", "");

            var json = ResponseParser.ExtractJsonFromModelOutput(textOrAiOutput);
            if (string.IsNullOrWhiteSpace(json))
                return AssetDeleteIntentResolveResult.Fail(
                    "无法提取 JSON。请使用 ```json 代码块，或输出含 assetDeleteIntent 的对象。",
                    textOrAiOutput);

            json = RemoveTrailingCommas(json.Trim());

            AssetDeleteIntentRootDto? root = null;
            try
            {
                root = JsonUtility.FromJson<AssetDeleteIntentRootDto>(json);
            }
            catch
            {
                root = null;
            }

            if (root?.assetDeleteIntent != null &&
                root.assetDeleteIntent.targets != null &&
                root.assetDeleteIntent.targets.Length > 0)
            {
                var paths = ResolvePathsFromIntent(ctx, root.assetDeleteIntent);
                if (paths.Count > 0)
                    return AssetDeleteIntentResolveResult.Ok(paths, root.assetDeleteIntent.note ?? "", json, false);

                return AssetDeleteIntentResolveResult.Fail(
                    "插件未能根据 AI 给出的 assetDeleteIntent.targets 在工程内解析到可删除的资源路径。请检查 nameHint/pathHint，或使用 kind=asset_path 提供完整 Assets/ 路径。",
                    json);
            }

            var legacy = Parse(textOrAiOutput);
            if (legacy.Success)
                return AssetDeleteIntentResolveResult.Ok(legacy.NormalizedPaths, legacy.Envelope?.note ?? "", legacy.RawJson, true);

            return AssetDeleteIntentResolveResult.Fail(legacy.Error ?? "无法解析删除意图", legacy.RawJson);
        }

        /// <summary>
        /// 无 AI 时：仅从用户句与记忆中按关键字解析可删路径（脚本优先，其次预制体等资源文件名匹配）。
        /// </summary>
        public static List<string> ResolvePluginOnlyDeletePaths(ProjectContext ctx, string hintFull)
        {
            hintFull = NormalizeHintForAssetDelete(hintFull);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ResolveScriptPathsFromUserPrompt(ctx, hintFull))
                result.Add(p);
            if (result.Count == 0)
            {
                foreach (var p in ResolveAssetPathsFromKeywordHint(ctx, hintFull))
                    result.Add(p);
            }

            return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> ResolvePathsFromIntent(ProjectContext ctx, AssetDeleteIntentDto intent)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (intent.targets == null)
                return new List<string>();

            foreach (var t in intent.targets)
            {
                if (t == null)
                    continue;
                var kind = string.IsNullOrWhiteSpace(t.kind) ? "unknown" : t.kind.Trim().ToLowerInvariant();
                switch (kind)
                {
                    case "script":
                        foreach (var p in ResolveScriptPathsFromUserPrompt(ctx, CombineTargetHints(t)))
                            result.Add(p);
                        break;
                    case "prefab":
                        foreach (var p in ResolveAssetPathsFromList(ctx.PrefabAssetPaths, t))
                            result.Add(p);
                        break;
                    case "material":
                        foreach (var p in ResolveAssetPathsFromList(ctx.MaterialAssetPaths, t))
                            result.Add(p);
                        break;
                    case "scene":
                        foreach (var p in ResolveAssetPathsFromList(ctx.SceneAssetPaths, t))
                            result.Add(p);
                        break;
                    case "texture2d":
                    case "texture":
                        foreach (var p in ResolveAssetPathsFromList(ctx.Texture2DAssetPaths, t))
                            result.Add(p);
                        break;
                    case "asset_path":
                        if (!string.IsNullOrWhiteSpace(t.pathHint) &&
                            AssetPathSecurity.TryValidateGenericAssetPath(t.pathHint.Trim(), out var ap, out _) &&
                            AssetExistsForDelete(ap))
                            result.Add(ap);
                        break;
                    default:
                        foreach (var p in ResolveUnknownTarget(ctx, t))
                            result.Add(p);
                        break;
                }
            }

            return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string CombineTargetHints(AssetDeleteIntentTargetDto t)
        {
            var n = t.nameHint?.Trim();
            var p = t.pathHint?.Trim();
            if (string.IsNullOrEmpty(n) && string.IsNullOrEmpty(p))
                return "";
            if (string.IsNullOrEmpty(n))
                return p ?? "";
            if (string.IsNullOrEmpty(p))
                return n;
            return n + " " + p;
        }

        private static IEnumerable<string> ResolveAssetPathsFromList(List<string> list, AssetDeleteIntentTargetDto t)
        {
            var pathHint = t.pathHint?.Trim();
            var nameHint = t.nameHint?.Trim();
            if (!string.IsNullOrEmpty(pathHint) && pathHint.StartsWith("Assets/", StringComparison.Ordinal))
            {
                if (AssetPathSecurity.TryValidateGenericAssetPath(pathHint, out var norm, out _) && AssetExistsForDelete(norm))
                {
                    yield return norm;
                    yield break;
                }
            }

            foreach (var p in list)
            {
                if (!AssetExistsForDelete(p))
                    continue;
                var file = Path.GetFileName(p);
                var noExt = Path.GetFileNameWithoutExtension(p);
                if (!string.IsNullOrEmpty(nameHint) &&
                    (string.Equals(file, nameHint, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(noExt, nameHint, StringComparison.OrdinalIgnoreCase)))
                    yield return p;
            }

            if (!string.IsNullOrEmpty(nameHint))
            {
                foreach (var p in list)
                {
                    if (!AssetExistsForDelete(p))
                        continue;
                    if (Path.GetFileName(p).IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) >= 0)
                        yield return p;
                }
            }

            if (!string.IsNullOrEmpty(pathHint) && !pathHint.StartsWith("Assets/", StringComparison.Ordinal))
            {
                foreach (var p in list)
                {
                    if (!AssetExistsForDelete(p))
                        continue;
                    if (p.IndexOf(pathHint, StringComparison.OrdinalIgnoreCase) >= 0)
                        yield return p;
                }
            }
        }

        private static IEnumerable<string> ResolveUnknownTarget(ProjectContext ctx, AssetDeleteIntentTargetDto t)
        {
            var combined = CombineTargetHints(t);
            foreach (var p in ResolveScriptPathsFromUserPrompt(ctx, combined))
                yield return p;
            foreach (var p in ResolveAssetPathsFromList(ctx.PrefabAssetPaths, t))
                yield return p;
            foreach (var p in ResolveAssetPathsFromList(ctx.MaterialAssetPaths, t))
                yield return p;
            foreach (var p in ResolveAssetPathsFromList(ctx.SceneAssetPaths, t))
                yield return p;
            foreach (var p in ResolveAssetPathsFromList(ctx.Texture2DAssetPaths, t))
                yield return p;
            if (!string.IsNullOrEmpty(t.pathHint) && t.pathHint.Trim().StartsWith("Assets/", StringComparison.Ordinal) &&
                AssetPathSecurity.TryValidateGenericAssetPath(t.pathHint.Trim(), out var ap, out _) &&
                AssetExistsForDelete(ap))
                yield return ap;
        }

        private static IEnumerable<string> ResolveAssetPathsFromKeywordHint(ProjectContext ctx, string hint)
        {
            hint = NormalizeHintForAssetDelete(hint);
            if (string.IsNullOrWhiteSpace(hint))
                yield break;
            foreach (var list in new[]
                     {
                         ctx.PrefabAssetPaths, ctx.MaterialAssetPaths, ctx.SceneAssetPaths, ctx.Texture2DAssetPaths
                     })
            {
                foreach (var p in list)
                {
                    if (!AssetExistsForDelete(p))
                        continue;
                    var fn = Path.GetFileName(p);
                    if (hint.IndexOf(fn, StringComparison.OrdinalIgnoreCase) >= 0)
                        yield return p;
                }
            }
        }

        private static string RemoveTrailingCommas(string json)
        {
            var s = json;
            for (var i = 0; i < 8; i++)
            {
                var n = TrailingCommaRegex.Replace(s, "$1");
                if (n == s) break;
                s = n;
            }

            return s;
        }
    }
}
