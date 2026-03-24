#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace UnityMCP.Tools
{
    /// <summary>
    /// 校验 Assets 下任意资源路径（删除/移动/复制等通用）。
    /// </summary>
    public static class AssetPathSecurity
    {
        /// <summary>
        /// 校验路径位于工程 Assets 目录内、无穿越，不要求特定扩展名。
        /// </summary>
        public static bool TryValidateGenericAssetPath(string? path, out string normalized, out string? error)
        {
            error = null;
            normalized = (path ?? "").Trim().Replace('\\', '/');

            if (string.IsNullOrEmpty(normalized))
            {
                error = "资源路径为空。";
                return false;
            }

            if (normalized.Contains("..", StringComparison.Ordinal))
            {
                error = "禁止在路径中使用 .. 。";
                return false;
            }

            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "路径必须以 Assets/ 开头。";
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
