#nullable enable

using UnityEditor;
using UnityEngine;
using UnityMCP.Generators;

namespace UnityMCP.UI
{
    /// <summary>
    /// 预览窗口，用于显示 AI 生成的代码或预制体 JSON 结构。
    /// </summary>
    public class PreviewWindow : EditorWindow
    {
        private string _textContent = "";
        private string _title = "预览";
        private Vector2 _scrollPos;

        public static void ShowWindow(string title, string content)
        {
            var window = GetWindow<PreviewWindow>(utility: true);
            window.titleContent = new GUIContent(title);
            window._title = title;
            window._textContent = content;
            window.minSize = new Vector2(500, 400);
            window.ShowUtility();
            window.Focus();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(_title, EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, EditorStyles.helpBox);
            
            // 使用 TextArea 使得内容可选中/复制
            var style = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                richText = true
            };
            
            EditorGUILayout.TextArea(_textContent, style, GUILayout.ExpandHeight(true));
            
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("关闭", GUILayout.Width(100), GUILayout.Height(30)))
            {
                Close();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }
    }
}
