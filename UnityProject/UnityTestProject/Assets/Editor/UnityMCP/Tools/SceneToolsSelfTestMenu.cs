#nullable enable

using UnityEditor;
using UnityEngine;

namespace UnityMCP.Tools
{
    /// <summary>
    /// A.1 / A.2 冒烟测试菜单（仅开发调试用）。
    /// </summary>
    public static class SceneToolsSelfTestMenu
    {
        [MenuItem("Window/AI 助手/调试/场景工具自检（空物体+BoxCollider）", priority = 500)]
        private static void RunSmokeTestHierarchy()
        {
            var r1 = SceneEditorTools.CreateEmptyGameObjectAt("UnityMCP_A1_Smoke", null);
            if (!r1.Success || r1.GameObject == null)
            {
                EditorUtility.DisplayDialog("场景工具自检", r1.Error ?? "创建失败", "确定");
                return;
            }

            var r2 = SceneEditorTools.AddComponentToGameObject(r1.GameObject, "BoxCollider");
            var msg = r1.ToString() + "\n" + r2.ToString();
            EditorUtility.DisplayDialog("场景工具自检", msg + (r2.Success ? "\n\n可在 Hierarchy 中查看 UnityMCP_A1_Smoke，Ctrl+Z 撤销。" : ""), "确定");
        }

        [MenuItem("Window/AI 助手/调试/场景工具自检（挂到选中物体下）", priority = 501)]
        private static void RunSmokeTestUnderSelection()
        {
            var r1 = SceneEditorTools.CreateEmptyGameObjectAt("UnityMCP_A1_Child", HierarchyLocator.ParentUsesSelection);
            if (!r1.Success)
            {
                EditorUtility.DisplayDialog("场景工具自检", r1.Error ?? "失败", "确定");
                return;
            }

            EditorUtility.DisplayDialog("场景工具自检", r1.ToString() + "\n\nCtrl+Z 可撤销。", "确定");
        }

        private const string A2SampleJson = "{\n" +
            "  \"unityOpsVersion\": 1,\n" +
            "  \"operations\": [\n" +
            "    { \"op\": \"createEmpty\", \"name\": \"UnityMCP_A2_Ops\", \"parentPath\": \"\" },\n" +
            "    { \"op\": \"addComponent\", \"path\": \"UnityMCP_A2_Ops\", \"typeName\": \"BoxCollider\" },\n" +
            "    { \"op\": \"setTransform\", \"path\": \"UnityMCP_A2_Ops\", \"localPosition\": \"0,1,0\" }\n" +
            "  ]\n" +
            "}";
        [MenuItem("Window/AI 助手/调试/场景操控 JSON 自检（Parse + Execute）", priority = 502)]
        private static void RunSceneOpsFromHardcodedJson()
        {
            var parse = SceneOpsParser.Parse(A2SampleJson);
            if (!parse.Success || parse.Envelope == null)
            {
                EditorUtility.DisplayDialog("场景操控 JSON 自检", parse.Error ?? "解析失败", "确定");
                return;
            }

            var exec = SceneOpsExecutor.Execute(parse.Envelope);
            var msg = $"解析 OK（{parse.Envelope.operations.Length} 步）\n执行: {(exec.Success ? "成功" : "失败")}\n" +
                      (exec.Error ?? "") +
                      $"\n\n已完成步数: {exec.StepsCompleted}" +
                      (exec.FailedAtIndex >= 0 ? $"\n失败下标: {exec.FailedAtIndex}" : "");
            EditorUtility.DisplayDialog("场景操控 JSON 自检", msg + "\n\nHierarchy 中应见 UnityMCP_A2_Ops（含 BoxCollider，Y=1）。Ctrl+Z 撤销。", "确定");
        }
    }
}
