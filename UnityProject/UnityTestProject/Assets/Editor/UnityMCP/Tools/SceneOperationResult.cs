#nullable enable

using UnityEngine;

namespace UnityMCP.Tools
{
    /// <summary>
    /// 场景原子操作的统一返回结果（A.1 操作层）。
    /// </summary>
    public readonly struct SceneOperationResult
    {
        public bool Success { get; }
        public string? Error { get; }
        public GameObject? GameObject { get; }

        private SceneOperationResult(bool success, string? error, GameObject? go)
        {
            Success = success;
            Error = error;
            GameObject = go;
        }

        public static SceneOperationResult Ok(GameObject? go = null) => new(true, null, go);

        public static SceneOperationResult Fail(string error) => new(false, error, null);

        public override string ToString() => Success
            ? $"成功{(GameObject != null ? $": {GameObject.name}" : "")}"
            : $"失败: {Error}";
    }
}
