#nullable enable

using UnityEditor;
using UnityEngine;

namespace UnityMCP.UI
{
    /// <summary>
    /// 菜单项定义。
    /// 在 Unity 编辑器菜单中注册 AI 助手相关的入口。
    /// </summary>
    public static class MenuItems
    {
        [MenuItem("Window/AI 助手/关于", priority = 200)]
        private static void ShowAbout()
        {
            EditorUtility.DisplayDialog(
                "Unity AI 助手",
                "Unity AI 辅助开发插件 (UnityMCP)\n\n" +
                "版本: 0.1.0 (Phase 1 MVP)\n" +
                "功能: AI 驱动的代码生成\n\n" +
                "快捷键:\n" +
                "  Ctrl+Shift+G  快捷生成\n" +
                "  Ctrl+Shift+,  打开设置\n\n" +
                "当前支持: Ollama 本地模型\n" +
                "计划支持: Claude, OpenAI, Azure",
                "确定");
        }
    }
}
