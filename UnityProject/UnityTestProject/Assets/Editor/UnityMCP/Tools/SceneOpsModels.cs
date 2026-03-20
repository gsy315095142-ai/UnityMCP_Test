#nullable enable

using System;

namespace UnityMCP.Tools
{
    /// <summary>
    /// scene-ops JSON 根对象（A.2），与模型约定字段名一致以便 JsonUtility 反序列化。
    /// </summary>
    [Serializable]
    public class SceneOpsEnvelopeDto
    {
        /// <summary>当前仅支持 1</summary>
        public int unityOpsVersion;

        /// <summary>按顺序执行的操作列表</summary>
        public SceneOperationDto[] operations = Array.Empty<SceneOperationDto>();
    }

    /// <summary>
    /// 单步操作；未使用的字段保持默认即可。
    /// </summary>
    [Serializable]
    public class SceneOperationDto
    {
        /// <summary>
        /// 操作类型：createEmpty | setParent | addComponent | setTransform | instantiatePrefab（大小写不敏感，允许下划线写法）。
        /// </summary>
        public string op = "";

        /// <summary>createEmpty：物体名称</summary>
        public string name = "";

        /// <summary>createEmpty / instantiatePrefab：父节点；空=场景根；<c>__selection__</c> 为当前选中</summary>
        public string parentPath = "";

        /// <summary>setParent / addComponent / setTransform：目标物体层级路径</summary>
        public string path = "";

        /// <summary>setParent：新父节点层级路径或 <c>__selection__</c></summary>
        public string newParentPath = "";

        /// <summary>instantiatePrefab：Assets 下 .prefab 路径</summary>
        public string prefabAssetPath = "";

        /// <summary>addComponent：组件类型短名或全名</summary>
        public string typeName = "";

        /// <summary>setParent：是否保持世界坐标（默认 false）</summary>
        public bool worldPositionStays;

        /// <summary>可选，格式 "x,y,z"（逗号分隔，InvariantCulture）</summary>
        public string localPosition = "";

        public string localEulerAngles = "";

        public string localScale = "";
    }

    /// <summary>解析 <see cref="SceneOpsEnvelopeDto"/> 的结果</summary>
    public sealed class SceneOpsParseResult
    {
        public bool Success { get; private set; }
        public SceneOpsEnvelopeDto? Envelope { get; private set; }
        public string RawJson { get; private set; } = "";
        public string? Error { get; private set; }

        public static SceneOpsParseResult Ok(SceneOpsEnvelopeDto envelope, string rawJson) => new()
        {
            Success = true,
            Envelope = envelope,
            RawJson = rawJson ?? ""
        };

        public static SceneOpsParseResult Fail(string error, string rawJson = "") => new()
        {
            Success = false,
            Error = error,
            RawJson = rawJson ?? ""
        };
    }

    /// <summary>顺序执行 scene-ops 的结果</summary>
    public sealed class SceneOpsBatchResult
    {
        public bool Success { get; set; }
        /// <summary>已成功完成的步数（失败时即为失败步序号之前的步数）</summary>
        public int StepsCompleted { get; set; }
        /// <summary>失败步骤下标，0-based；-1 表示无失败</summary>
        public int FailedAtIndex { get; set; } = -1;
        public string? Error { get; set; }
    }
}
