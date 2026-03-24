#nullable enable

using System;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Tools
{
    /// <summary>
    /// 执行 <see cref="AssetOpsEnvelopeDto.operations"/>。
    /// </summary>
    public static class AssetOpsExecutor
    {
        public static AssetOpsBatchResult Execute(AssetOpsEnvelopeDto envelope)
        {
            var result = new AssetOpsBatchResult { Success = true, StepsCompleted = 0, FailedAtIndex = -1 };

            if (envelope.operations == null || envelope.operations.Length == 0)
            {
                result.Success = false;
                result.Error = "operations 为空";
                result.FailedAtIndex = 0;
                return result;
            }

            for (var i = 0; i < envelope.operations.Length; i++)
            {
                var step = envelope.operations[i];
                var one = ExecuteOne(step);
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

            AssetDatabase.Refresh();
            return result;
        }

        private static (bool Success, string? Error) ExecuteOne(AssetOperationDto op)
        {
            var kind = NormalizeOp(op.op);
            if (string.IsNullOrEmpty(kind))
                return (false, "op 字段为空");

            return kind switch
            {
                "moveasset" => OpMove(op),
                "renameasset" => OpRename(op),
                "createfolder" => OpCreateFolder(op),
                "copyasset" => OpCopy(op),
                _ => (false, $"未知操作: {op.op}")
            };
        }

        private static string NormalizeOp(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            return raw.Trim().ToLowerInvariant().Replace("_", "");
        }

        private static (bool Success, string? Error) OpMove(AssetOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path) || string.IsNullOrWhiteSpace(op.destPath))
                return (false, "moveAsset 需要 path 与 destPath");

            if (!AssetPathSecurity.TryValidateGenericAssetPath(op.path, out var from, out var e1))
                return (false, e1);
            if (!AssetPathSecurity.TryValidateGenericAssetPath(op.destPath, out var to, out var e2))
                return (false, e2);

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(from) == null)
                return (false, $"源资源不存在: {from}");

            var err = AssetDatabase.MoveAsset(from, to);
            return string.IsNullOrEmpty(err) ? (true, null) : (false, err);
        }

        private static (bool Success, string? Error) OpRename(AssetOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path) || string.IsNullOrWhiteSpace(op.newName))
                return (false, "renameAsset 需要 path 与 newName");

            if (!AssetPathSecurity.TryValidateGenericAssetPath(op.path, out var path, out var err))
                return (false, err);

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                return (false, $"资源不存在: {path}");

            var e = AssetDatabase.RenameAsset(path, op.newName.Trim());
            return string.IsNullOrEmpty(e) ? (true, null) : (false, e);
        }

        private static (bool Success, string? Error) OpCreateFolder(AssetOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path))
                return (false, "createFolder 需要 path（如 Assets/Art/UI）");

            if (!AssetPathSecurity.TryValidateGenericAssetPath(op.path, out var folderPath, out var err))
                return (false, err);

            folderPath = folderPath.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folderPath))
                return (true, null);

            try
            {
                if (!EnsureFolderPathExists(folderPath))
                    return (false, $"无法创建文件夹: {folderPath}");
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static (bool Success, string? Error) OpCopy(AssetOperationDto op)
        {
            if (string.IsNullOrWhiteSpace(op.path) || string.IsNullOrWhiteSpace(op.destPath))
                return (false, "copyAsset 需要 path 与 destPath");

            if (!AssetPathSecurity.TryValidateGenericAssetPath(op.path, out var from, out var e1))
                return (false, e1);
            if (!AssetPathSecurity.TryValidateGenericAssetPath(op.destPath, out var to, out var e2))
                return (false, e2);

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(from) == null)
                return (false, $"源资源不存在: {from}");

            if (!AssetDatabase.CopyAsset(from, to))
                return (false, "CopyAsset 返回 false");

            return (true, null);
        }

        /// <summary>递归创建 Assets 下文件夹（分段 CreateFolder）。</summary>
        private static bool EnsureFolderPathExists(string assetFolderPath)
        {
            assetFolderPath = assetFolderPath.Replace('\\', '/').TrimEnd('/');
            if (!assetFolderPath.StartsWith("Assets/", StringComparison.Ordinal))
                return false;
            if (AssetDatabase.IsValidFolder(assetFolderPath))
                return true;

            var slash = assetFolderPath.LastIndexOf('/');
            if (slash <= 0)
                return false;

            var parent = assetFolderPath.Substring(0, slash);
            var leaf = assetFolderPath.Substring(slash + 1);
            if (string.IsNullOrEmpty(leaf))
                return false;

            if (!AssetDatabase.IsValidFolder(parent))
            {
                if (!EnsureFolderPathExists(parent))
                    return false;
            }

            AssetDatabase.CreateFolder(parent, leaf);
            return AssetDatabase.IsValidFolder(assetFolderPath);
        }
    }
}
