#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace UnityMCP.Tools
{
    /// <summary>
    /// 预制体等资源路径安全校验（A.0）。
    /// </summary>
    public static class ScenePathSecurity
    {
        /// <summary>
        /// 校验用于实例化的预制体资源路径：必须在 Assets 下、为 .prefab、无路径穿越。
        /// </summary>
        public static bool TryValidatePrefabAssetPath(string? path, out string normalized, out string? error)
        {
            error = null;
            normalized = (path ?? "").Trim().Replace('\\', '/');

            if (string.IsNullOrEmpty(normalized))
            {
                error = "预制体路径为空。";
                return false;
            }

            if (normalized.Contains("..", StringComparison.Ordinal))
            {
                error = "禁止在路径中使用 .. 。";
                return false;
            }

            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "预制体路径必须以 Assets/ 开头。";
                return false;
            }

            if (!normalized.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                error = "必须是 .prefab 资源路径。";
                return false;
            }

            try
            {
                var dataPath = Application.dataPath.Replace('\\', '/');
                var projectRoot = Path.GetDirectoryName(dataPath);
                if (string.IsNullOrEmpty(projectRoot))
                {
                    error = "无法解析工程目录。";
                    return false;
                }

                var fullAsset = Path.GetFullPath(Path.Combine(projectRoot, normalized));
                var fullData = Path.GetFullPath(dataPath);
                if (!fullAsset.StartsWith(fullData, StringComparison.OrdinalIgnoreCase))
                {
                    error = "路径必须位于 Assets 目录内。";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = $"路径校验异常: {ex.Message}";
                return false;
            }

            return true;
        }
    }
}
