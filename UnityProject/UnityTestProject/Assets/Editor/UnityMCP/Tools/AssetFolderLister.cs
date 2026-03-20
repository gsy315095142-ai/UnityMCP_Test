#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;

namespace UnityMCP.Tools
{
    /// <summary>
    /// 枚举 <c>Assets</c> 下文件夹路径（供工作区预制体前缀等 UI 下拉使用）。
    /// </summary>
    public static class AssetFolderLister
    {
        /// <summary>
        /// 列出 <c>Assets</c> 及其子文件夹的 Unity 资产路径，含 <c>Assets</c> 根。
        /// </summary>
        /// <param name="maxDepth">相对 <c>Assets</c> 的最大深度（0 仅根）。</param>
        /// <param name="maxFolders">最多返回条数，防止超大工程卡顿。</param>
        public static List<string> ListFoldersUnderAssets(int maxDepth = 14, int maxFolders = 800)
        {
            var result = new List<string>();
            var count = 0;

            void Walk(string path, int depth)
            {
                if (count >= maxFolders || depth > maxDepth)
                    return;

                result.Add(path);
                count++;

                if (depth >= maxDepth)
                    return;

                foreach (var sub in AssetDatabase.GetSubFolders(path))
                    Walk(sub, depth + 1);
            }

            Walk("Assets", 0);
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }
    }
}
