#nullable enable

using System.Globalization;
using UnityEngine;

namespace UnityMCP.Tools
{
    /// <summary>
    /// 解析 scene-ops 中 "x,y,z" 形式的可选向量（A.2）。
    /// </summary>
    public static class SceneOpsVectorParser
    {
        public static Vector3? TryParseVector3(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return null;

            var parts = csv.Trim().Split(new[] { ',' }, System.StringSplitOptions.None);
            if (parts.Length < 3)
                return null;

            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                return null;
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                return null;
            if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                return null;

            return new Vector3(x, y, z);
        }
    }
}
