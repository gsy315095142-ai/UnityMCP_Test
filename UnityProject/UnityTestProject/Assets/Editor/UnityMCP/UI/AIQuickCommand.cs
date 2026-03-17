#nullable enable

using System;
using UnityEditor;
using UnityEngine;
using UnityMCP.AI;
using UnityMCP.Core;
using UnityMCP.Generators;

namespace UnityMCP.UI
{
    /// <summary>
    /// AI 快捷命令窗口。
    /// 按快捷键呼出，输入需求后自动调用 AI 生成代码。
    /// </summary>
    public class AIQuickCommand : EditorWindow
    {
        private enum State
        {
            Input,
            Loading,
            Preview,
            Success,
            Error
        }

        private State _state = State.Input;
        private string _userInput = "";
        private string _generatedCode = "";
        private string _scriptName = "";
        private string _errorMessage = "";
        private string _savedFilePath = "";
        private float _generationTime;
        private int _tokensUsed;
        private Vector2 _codeScrollPos;
        private AIServiceConfig? _config;

        [MenuItem("Window/AI 助手/快捷生成 %#g", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<AIQuickCommand>(utility: true);
            window.titleContent = new GUIContent("AI 代码生成");
            window.minSize = new Vector2(600, 450);
            window.maxSize = new Vector2(900, 700);
            window.Reset();
            window.ShowUtility();
            window.Focus();
        }

        private void OnEnable()
        {
            _config = AIServiceConfig.Load();
        }

        private void Reset()
        {
            _state = State.Input;
            _userInput = "";
            _generatedCode = "";
            _scriptName = "";
            _errorMessage = "";
            _savedFilePath = "";
            _codeScrollPos = Vector2.zero;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(5);

            switch (_state)
            {
                case State.Input:
                    DrawInputUI();
                    break;
                case State.Loading:
                    DrawLoadingUI();
                    break;
                case State.Preview:
                    DrawPreviewUI();
                    break;
                case State.Success:
                    DrawSuccessUI();
                    break;
                case State.Error:
                    DrawErrorUI();
                    break;
            }
        }

        #region UI 绘制

        private void DrawInputUI()
        {
            EditorGUILayout.LabelField("描述你需要的脚本", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "示例：\n" +
                "• 创建一个 Player 脚本，包含 WASD 移动和空格跳跃\n" +
                "• 创建一个生命值管理器，支持受伤、治疗和死亡事件\n" +
                "• 创建一个物体旋转脚本，可以设置旋转速度和轴向",
                MessageType.None);

            EditorGUILayout.Space(5);

            _userInput = EditorGUILayout.TextArea(_userInput, GUILayout.MinHeight(80));

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUI.enabled = !string.IsNullOrWhiteSpace(_userInput);
            if (GUILayout.Button("生成代码", GUILayout.Width(120), GUILayout.Height(30)))
            {
                GenerateCode();
            }

            if (Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Return
                && Event.current.control
                && !string.IsNullOrWhiteSpace(_userInput))
            {
                GenerateCode();
                Event.current.Use();
            }

            GUI.enabled = true;

            if (GUILayout.Button("取消", GUILayout.Width(80), GUILayout.Height(30)))
            {
                Close();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("提示: Ctrl+Enter 快速生成", EditorStyles.miniLabel);
        }

        private void DrawLoadingUI()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("正在生成代码，请稍候...", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"模型: {_config?.GetEffectiveModel() ?? "未配置"}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();

            Repaint();
        }

        private void DrawPreviewUI()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("生成结果预览", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                $"耗时: {_generationTime:F1}秒 | Token: {_tokensUsed}",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("脚本名称:", GUILayout.Width(70));
            _scriptName = EditorGUILayout.TextField(_scriptName);
            EditorGUILayout.LabelField(".cs", GUILayout.Width(25));
            EditorGUILayout.EndHorizontal();

            var targetPath = ScriptGenerator.GetScriptPath(_scriptName);
            EditorGUILayout.LabelField($"保存位置: {targetPath}", EditorStyles.miniLabel);

            if (ScriptGenerator.ScriptExists(_scriptName))
            {
                EditorGUILayout.HelpBox(
                    $"⚠️ 文件 {_scriptName}.cs 已存在，保存将覆盖（会自动备份）",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("代码预览:");
            _codeScrollPos = EditorGUILayout.BeginScrollView(
                _codeScrollPos, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_generatedCode, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("创建文件", GUILayout.Height(30)))
            {
                SaveScript();
            }
            if (GUILayout.Button("重新生成", GUILayout.Height(30)))
            {
                GenerateCode();
            }
            if (GUILayout.Button("取消", GUILayout.Height(30)))
            {
                Reset();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSuccessUI()
        {
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("✅ 脚本创建成功！", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(_savedFilePath, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开文件", GUILayout.Height(30)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(_savedFilePath);
                if (asset != null)
                    AssetDatabase.OpenAsset(asset);
            }
            if (GUILayout.Button("继续生成", GUILayout.Height(30)))
            {
                Reset();
            }
            if (GUILayout.Button("关闭", GUILayout.Height(30)))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawErrorUI()
        {
            EditorGUILayout.LabelField("生成失败", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重试", GUILayout.Height(30)))
            {
                _state = State.Input;
            }
            if (GUILayout.Button("打开设置", GUILayout.Height(30)))
            {
                SettingsWindow.ShowWindow();
            }
            if (GUILayout.Button("关闭", GUILayout.Height(30)))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 核心逻辑

        private async void GenerateCode()
        {
            if (_config == null)
            {
                _errorMessage = "AI 服务未配置，请先打开设置窗口进行配置。";
                _state = State.Error;
                return;
            }

            _state = State.Loading;
            Repaint();

            try
            {
                var service = AIServiceFactory.Create(_config);
                var context = ProjectContext.Collect();

                var systemPrompt = PromptBuilder.BuildCodeSystemPrompt(context);
                var userPrompt = PromptBuilder.BuildCodeUserPrompt(_userInput);

                var response = await service.SendMessageAsync(systemPrompt, userPrompt);

                if (!response.Success)
                {
                    _errorMessage = response.Error ?? "AI 返回了未知错误";
                    _state = State.Error;
                    Repaint();
                    return;
                }

                var parseResult = ResponseParser.ParseCodeResponse(response.Content);

                if (!parseResult.Success)
                {
                    _errorMessage = parseResult.Error ?? "无法解析 AI 响应";
                    if (!string.IsNullOrEmpty(parseResult.Code))
                    {
                        _errorMessage += $"\n\nAI 原始输出:\n{parseResult.Code}";
                    }
                    _state = State.Error;
                    Repaint();
                    return;
                }

                _generatedCode = parseResult.Code;
                _scriptName = parseResult.ScriptName;
                _generationTime = response.Duration;
                _tokensUsed = response.TokensUsed;
                _state = State.Preview;
            }
            catch (Exception ex)
            {
                _errorMessage = $"生成过程中出错: {ex.Message}";
                _state = State.Error;
            }

            Repaint();
        }

        private void SaveScript()
        {
            if (string.IsNullOrEmpty(_scriptName))
            {
                EditorUtility.DisplayDialog("错误", "脚本名称不能为空", "确定");
                return;
            }

            if (ScriptGenerator.ScriptExists(_scriptName))
            {
                if (!EditorUtility.DisplayDialog(
                    "文件已存在",
                    $"{_scriptName}.cs 已存在，是否覆盖？\n（原文件将自动备份为 .cs.bak）",
                    "覆盖", "取消"))
                {
                    return;
                }
            }

            var result = ScriptGenerator.SaveScript(_scriptName, _generatedCode);

            if (result.Success)
            {
                _savedFilePath = result.FilePath;
                _state = State.Success;
            }
            else
            {
                _errorMessage = result.Error ?? "保存失败";
                _state = State.Error;
            }

            Repaint();
        }

        #endregion
    }
}
