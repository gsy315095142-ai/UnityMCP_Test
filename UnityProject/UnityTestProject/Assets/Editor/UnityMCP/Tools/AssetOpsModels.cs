#nullable enable

using System;

namespace UnityMCP.Tools
{
    /// <summary>
    /// asset-ops JSON 根对象，与模型约定字段名一致以便 JsonUtility 反序列化。
    /// </summary>
    [Serializable]
    public class AssetOpsEnvelopeDto
    {
        public int assetOpsVersion;

        public AssetOperationDto[] operations = Array.Empty<AssetOperationDto>();
    }

    /// <summary>单步 Project 资源操作。</summary>
    [Serializable]
    public class AssetOperationDto
    {
        /// <summary>
        /// moveAsset | renameAsset | createFolder | copyAsset（大小写不敏感，允许下划线）。
        /// </summary>
        public string op = "";

        /// <summary>源路径或重命名/复制的主体路径。</summary>
        public string path = "";

        /// <summary>moveAsset / copyAsset 的目标完整路径（含文件名）。</summary>
        public string destPath = "";

        /// <summary>renameAsset：新文件名（须含扩展名）。</summary>
        public string newName = "";
    }

    /// <summary>顺序执行 asset-ops 的结果。</summary>
    public sealed class AssetOpsBatchResult
    {
        public bool Success { get; set; }
        public int StepsCompleted { get; set; }
        public int FailedAtIndex { get; set; } = -1;
        public string? Error { get; set; }
    }

    public sealed class AssetOpsParseResult
    {
        public bool Success { get; private set; }
        public AssetOpsEnvelopeDto? Envelope { get; private set; }
        public string RawJson { get; private set; } = "";
        public string? Error { get; private set; }

        public static AssetOpsParseResult Ok(AssetOpsEnvelopeDto envelope, string rawJson) => new()
        {
            Success = true,
            Envelope = envelope,
            RawJson = rawJson ?? ""
        };

        public static AssetOpsParseResult Fail(string error, string rawJson = "") => new()
        {
            Success = false,
            Error = error,
            RawJson = rawJson ?? ""
        };
    }
}
