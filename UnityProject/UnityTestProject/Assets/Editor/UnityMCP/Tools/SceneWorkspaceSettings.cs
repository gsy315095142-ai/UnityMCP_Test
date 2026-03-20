#nullable enable

using UnityEditor;

namespace UnityMCP.Tools
{
    /// <summary>
    /// LumiAI 场景操控「工作区」配置（Editor 内持久化）。
    /// </summary>
    public sealed class SceneWorkspaceSettings
    {
        private const string PrefEnforce = "LumiAI.Workspace.Enforce";
        private const string PrefHierarchyRoot = "LumiAI.Workspace.HierarchyRoot";
        private const string PrefHierarchyEntireActiveScene = "LumiAI.Workspace.HierarchyEntireActiveScene";
        private const string PrefPrefabPrefix = "LumiAI.Workspace.PrefabPrefix";

        /// <summary>启用后，超出工作区的每步 scene-ops 需用户确认。</summary>
        public bool Enforce;

        /// <summary>活动场景内层级根路径（从根物体起，如 Game/LumiRoot）。与 <see cref="HierarchyUseEntireActiveScene"/> 互斥生效（见评估器）。</summary>
        public string HierarchyRoot = "";

        /// <summary>
        /// 为 true 时，层级工作区为<strong>当前活动场景的全部 Hierarchy</strong>，不再要求填写 <see cref="HierarchyRoot"/>。
        /// </summary>
        public bool HierarchyUseEntireActiveScene;

        /// <summary>允许的预制体资产路径前缀（如 Assets/Lumi/Prefabs）。空表示不限制预制体路径（仍受层级约束）。</summary>
        public string PrefabAssetPrefix = "";

        public static SceneWorkspaceSettings LoadFromEditorPrefs()
        {
            return new SceneWorkspaceSettings
            {
                Enforce = EditorPrefs.GetBool(PrefEnforce, false),
                HierarchyRoot = EditorPrefs.GetString(PrefHierarchyRoot, ""),
                HierarchyUseEntireActiveScene = EditorPrefs.GetBool(PrefHierarchyEntireActiveScene, false),
                PrefabAssetPrefix = EditorPrefs.GetString(PrefPrefabPrefix, "")
            };
        }

        public void SaveToEditorPrefs()
        {
            EditorPrefs.SetBool(PrefEnforce, Enforce);
            EditorPrefs.SetString(PrefHierarchyRoot, HierarchyRoot ?? "");
            EditorPrefs.SetBool(PrefHierarchyEntireActiveScene, HierarchyUseEntireActiveScene);
            EditorPrefs.SetString(PrefPrefabPrefix, PrefabAssetPrefix ?? "");
        }
    }
}
