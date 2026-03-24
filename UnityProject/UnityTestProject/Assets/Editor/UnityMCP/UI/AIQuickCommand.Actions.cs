#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMCP.AI;
using UnityMCP.Core;
using UnityMCP.Generators;
using UnityMCP.Tools;

namespace UnityMCP.UI
{
    public partial class AIQuickCommand : EditorWindow
    {

        #region 操作按钮处理

        /// <summary>用户是否明确希望把生成的预制体放进当前打开的场景（自然语言启发式）。</summary>
        private static bool ContentRequestsScenePlacement(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;
            var c = content.ToLowerInvariant();
            if (c.Contains("当前场景") || c.Contains("放到场景") || c.Contains("放进场景") || c.Contains("场景里") ||
                c.Contains("场景根") || c.Contains("实例化到场景") || c.Contains("拖入场景") || c.Contains("在场景里") ||
                c.Contains("hierarchy") || c.Contains("层级里"))
                return true;
            return c.Contains("into the scene") || c.Contains("in the scene");
        }

        private static void TryInstantiatePrefabInActiveScene(string assetPath)
        {
            var r = SceneEditorTools.InstantiatePrefab(assetPath, null);
            if (!r.Success)
            {
                Debug.LogWarning($"[UnityMCP] 未能将预制体放入当前场景: {r.Error}");
                return;
            }

            if (r.GameObject != null)
            {
                Selection.activeObject = r.GameObject;
                EditorGUIUtility.PingObject(r.GameObject);
            }

            Debug.Log($"[UnityMCP] 已将预制体实例化到当前场景: {assetPath}");
        }

        private void SaveScript(ChatMessage msg)
        {
            if (string.IsNullOrEmpty(msg.ScriptName))
            {
                EditorUtility.DisplayDialog("错误", "脚本名称不能为空", "确定");
                return;
            }

            if (ScriptGenerator.ScriptExists(msg.ScriptName))
            {
                if (!EditorUtility.DisplayDialog("文件已存在", $"{msg.ScriptName}.cs 已存在，是否覆盖？", "覆盖", "取消"))
                    return;
            }

            var result = ScriptGenerator.SaveScript(msg.ScriptName, msg.GeneratedCode);
            if (result.Success)
            {
                if (msg.Mode == GenerateMode.CombinedPrefabFirst && !string.IsNullOrEmpty(msg.SavedPrefabPath))
                {
                    msg.SavedScriptPath = result.FilePath;
                    PrefabGenerator.ScheduleAttachScriptToPrefabAfterCompile(msg.SavedPrefabPath, msg.ScriptName);
                    // 保留原「第 2 步完成」气泡（仍为 CodeGenerated），另起一条最终成功气泡
                    var finalMsg = new ChatMessage
                    {
                        Role = ChatRole.Assistant,
                        Type = MessageTypeEnum.SuccessResult,
                        Mode = GenerateMode.CombinedPrefabFirst,
                        Content = msg.Content,
                        SavedScriptPath = result.FilePath,
                        SavedPrefabPath = msg.SavedPrefabPath,
                        PrefabWarnings = new List<string>(msg.PrefabWarnings),
                        GenerationTime = msg.GenerationTime,
                        TokensUsed = msg.TokensUsed,
                        CodeGenerationTime = msg.CodeGenerationTime,
                        CodeTokensUsed = msg.CodeTokensUsed,
                        CombinedPrefabFirst = true
                    };
                    AddResultBubble(finalMsg, removeWaitingTextBubbles: false);
                    _isGenerating = false;

                    var placePath = _pendingPrefabPathForScenePlace;
                    if (!string.IsNullOrEmpty(placePath) &&
                        string.Equals(placePath, msg.SavedPrefabPath, StringComparison.OrdinalIgnoreCase))
                    {
                        TryInstantiatePrefabInActiveScene(placePath);
                        _pendingPrefabPathForScenePlace = null;
                    }

                    Repaint();
                    ScrollToBottom();
                    return;
                }

                AppendCodeSaveSuccessBubble(msg, result.FilePath);
                _isGenerating = false;
                PersistChatHistory();
                Repaint();
                ScrollToBottom();
            }
            else
            {
                EditorUtility.DisplayDialog("保存失败", result.Error ?? "未知错误", "确定");
            }
        }

        private void SavePrefab(ChatMessage msg)
        {
            if (msg.PrefabDescription == null || string.IsNullOrEmpty(msg.PrefabName)) return;

            msg.PrefabDescription.prefabName = msg.PrefabName;
            var ensureScript = !string.IsNullOrEmpty(msg.ScriptName) &&
                               (msg.Mode == GenerateMode.Combined || msg.Mode == GenerateMode.CombinedPrefabFirst)
                ? msg.ScriptName
                : null;
            var result = PrefabGenerator.Generate(msg.PrefabDescription, ResolvePrefabSaveFolder(), ensureScript);

            if (result.Success)
            {
                if (msg.Mode == GenerateMode.CombinedPrefabFirst)
                {
                    msg.SavedPrefabPath = result.AssetPath;
                    msg.PrefabWarnings = result.Warnings;
                    _combinedPrefabGenTime = msg.GenerationTime;
                    _combinedPrefabTokens = msg.TokensUsed;
                    _pendingPrefabPathForScenePlace = ContentRequestsScenePlacement(msg.Content)
                        ? result.AssetPath
                        : null;
                    AddTextBubble("⏳ 联合生成 (第2步): 正在生成代码...");
                    // 第二步必须用新实例：msg 已在历史中，不能再让 GenerateCodeAsync 就地改写同一引用
                    var codePhase = new ChatMessage
                    {
                        Role = ChatRole.Assistant,
                        Mode = GenerateMode.CombinedPrefabFirst,
                        Content = msg.Content,
                        CodeType = msg.CodeType,
                        CombinedPrefabFirst = true,
                        SavedPrefabPath = msg.SavedPrefabPath,
                        PrefabWarnings = new List<string>(msg.PrefabWarnings)
                    };
                    GenerateCodeAsync(codePhase);
                    Repaint();
                    ScrollToBottom();
                    return;
                }

                if (msg.Mode == GenerateMode.Combined)
                {
                    msg.CodeGenerationTime = _combinedCodeGenTime;
                    msg.CodeTokensUsed = _combinedCodeTokens;
                }

                AppendPrefabSaveSuccessBubble(msg, result.AssetPath, result.Warnings);
                _isGenerating = false;

                if (ContentRequestsScenePlacement(msg.Content))
                    TryInstantiatePrefabInActiveScene(result.AssetPath);

                PersistChatHistory();
                Repaint();
                ScrollToBottom();
            }
            else
            {
                string err = result.Error ?? "未知错误";
                if (result.Warnings.Count > 0) err += "\n" + string.Join("\n", result.Warnings);
                EditorUtility.DisplayDialog("保存失败", err, "确定");
            }
        }

        private void SaveCodeAndContinueCombined(ChatMessage msg)
        {
            if (string.IsNullOrEmpty(msg.ScriptName)) return;

            if (ScriptGenerator.ScriptExists(msg.ScriptName))
            {
                if (!EditorUtility.DisplayDialog("文件已存在", $"{msg.ScriptName}.cs 已存在，是否覆盖？", "覆盖", "取消"))
                    return;
            }

            _combinedCodeGenTime = msg.GenerationTime;
            _combinedCodeTokens = msg.TokensUsed;

            var result = ScriptGenerator.SaveScript(msg.ScriptName, msg.GeneratedCode);
            if (!result.Success)
            {
                EditorUtility.DisplayDialog("保存失败", result.Error ?? "未知错误", "确定");
                return;
            }

            msg.SavedScriptPath = result.FilePath;
            AppendCodeSaveSuccessBubble(msg, result.FilePath);

            var waitMsg = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Type = MessageTypeEnum.WaitingCompile,
                Mode = GenerateMode.Combined,
                Content = msg.Content,
                CodeType = msg.CodeType,
                ScriptName = msg.ScriptName,
                GeneratedCode = msg.GeneratedCode,
                SavedScriptPath = result.FilePath,
                GenerationTime = msg.GenerationTime,
                TokensUsed = msg.TokensUsed,
                CodeGenerationTime = _combinedCodeGenTime,
                CodeTokensUsed = _combinedCodeTokens,
                CompileWaitTicks = 0
            };
            _chatHistory.Add(waitMsg);
            PersistChatHistory();

            _compilationDetected = false;
            _pendingMessage = waitMsg;
            EditorApplication.update += OnCompileWaitUpdate;
            Repaint();
            ScrollToBottom();
        }

        private void OnCompileWaitUpdate()
        {
            if (_pendingMessage == null)
            {
                EditorApplication.update -= OnCompileWaitUpdate;
                return;
            }

            _pendingMessage.CompileWaitTicks++;

            if (EditorApplication.isCompiling)
            {
                _compilationDetected = true;
            }
            else if (_compilationDetected || _pendingMessage.CompileWaitTicks > 150)
            {
                EditorApplication.update -= OnCompileWaitUpdate;

                _pendingMessage.CompileWaitFinished = true;
                PersistChatHistory();

                var savedScript = _pendingMessage.SavedScriptPath;
                var scriptName = _pendingMessage.ScriptName;
                var content = _pendingMessage.Content;

                _pendingMessage = new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Mode = GenerateMode.Combined,
                    Content = content,
                    ScriptName = scriptName,
                    SavedScriptPath = savedScript
                };

                AddTextBubble("⏳ 联合生成 (第2步): 编译完成，正在生成预制体...");
                GeneratePrefabAsync(_pendingMessage);
                return;
            }

            Repaint();
        }

        #endregion
    }
}
