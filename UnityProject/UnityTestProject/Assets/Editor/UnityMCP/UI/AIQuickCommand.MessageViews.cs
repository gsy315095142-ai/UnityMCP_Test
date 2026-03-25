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

        #region 消息内部状态绘制

        private static class InlineActionKeys
        {
            public const string SceneOpsPreviewJson = "scene_ops_preview";
            public const string SceneOpsExecute = "scene_ops_execute";
            public const string AssetDeleteConfirm = "asset_delete_confirm";
            public const string AssetDeleteCancel = "asset_delete_cancel";
            public const string AssetOpsExecute = "asset_ops_execute";
            public const string PrefabPreviewJson = "prefab_preview_json";
            public const string PrefabCreate = "prefab_create";
            public const string CodePreview = "code_preview";
            public const string CodeSave = "code_save";
            public const string CodeSaveAndContinue = "code_save_continue";
        }

        private static bool HasInlineAction(ChatMessage msg, string key)
        {
            if (string.IsNullOrEmpty(msg.InlineActionsClicked) || string.IsNullOrEmpty(key))
                return false;
            foreach (var part in msg.InlineActionsClicked.Split(','))
            {
                if (string.Equals(part.Trim(), key, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void AddInlineAction(ChatMessage msg, string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in (msg.InlineActionsClicked ?? "").Split(','))
            {
                var t = part.Trim();
                if (t.Length > 0)
                    set.Add(t);
            }

            set.Add(key);
            msg.InlineActionsClicked = string.Join(",", set);
        }

        private static void RemoveInlineAction(ChatMessage msg, string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            var list = new List<string>();
            foreach (var part in (msg.InlineActionsClicked ?? "").Split(','))
            {
                var t = part.Trim();
                if (t.Length > 0 && !string.Equals(t, key, StringComparison.Ordinal))
                    list.Add(t);
            }

            msg.InlineActionsClicked = list.Count > 0 ? string.Join(",", list) : "";
        }

        /// <summary>场景操控：预览 / 执行 两行按钮；已点的显示【已确认】类文案，未点的置灰。</summary>
        private void DrawSceneOpsInlineActions(ChatMessage msg, bool readOnlySummary)
        {
            EditorGUILayout.BeginHorizontal();
            if (HasInlineAction(msg, InlineActionKeys.SceneOpsPreviewJson))
            {
                EditorGUILayout.LabelField("【已预览】 JSON", EditorStyles.boldLabel, GUILayout.Height(24));
            }
            else
            {
                EditorGUI.BeginDisabledGroup(readOnlySummary);
                if (GUILayout.Button("预览 JSON", GUILayout.Height(25)))
                {
                    AddInlineAction(msg, InlineActionKeys.SceneOpsPreviewJson);
                    PreviewWindow.ShowWindow("unity-ops JSON 预览", msg.RawJson);
                    PersistChatHistory();
                    Repaint();
                }

                EditorGUI.EndDisabledGroup();
            }

            if (HasInlineAction(msg, InlineActionKeys.SceneOpsExecute))
            {
                EditorGUILayout.LabelField("【已确认】执行场景操作", EditorStyles.boldLabel, GUILayout.Height(24));
            }
            else
            {
                EditorGUI.BeginDisabledGroup(readOnlySummary);
                if (GUILayout.Button("执行场景操作", GUILayout.Height(25)))
                {
                    AddInlineAction(msg, InlineActionKeys.SceneOpsExecute);
                    PersistChatHistory();
                    Repaint();
                    ExecuteSceneOps(msg);
                }

                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAssetDeleteInlineActions(ChatMessage msg)
        {
            var confirmDone = HasInlineAction(msg, InlineActionKeys.AssetDeleteConfirm);
            var cancelDone = HasInlineAction(msg, InlineActionKeys.AssetDeleteCancel);
            var decided = confirmDone || cancelDone;

            EditorGUILayout.BeginHorizontal();
            if (confirmDone)
                EditorGUILayout.LabelField("【已确认】删除", EditorStyles.boldLabel, GUILayout.Height(28));
            else
            {
                EditorGUI.BeginDisabledGroup(decided);
                if (GUILayout.Button("确认删除", GUILayout.Height(28)))
                {
                    AddInlineAction(msg, InlineActionKeys.AssetDeleteConfirm);
                    PersistChatHistory();
                    Repaint();
                    ExecuteConfirmedAssetDelete(msg);
                }

                EditorGUI.EndDisabledGroup();
            }

            if (cancelDone)
                EditorGUILayout.LabelField("【已取消】", EditorStyles.boldLabel, GUILayout.Height(28));
            else
            {
                EditorGUI.BeginDisabledGroup(decided);
                if (GUILayout.Button("取消", GUILayout.Height(28)))
                {
                    AddInlineAction(msg, InlineActionKeys.AssetDeleteCancel);
                    PersistChatHistory();
                    AddTextBubble("已取消删除。");
                    Repaint();
                }

                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAssetDeleteResolvedBarForText(ChatMessage msg)
        {
            if (!HasInlineAction(msg, InlineActionKeys.AssetDeleteConfirm) &&
                !HasInlineAction(msg, InlineActionKeys.AssetDeleteCancel))
                return;

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (HasInlineAction(msg, InlineActionKeys.AssetDeleteConfirm))
                EditorGUILayout.LabelField("【已确认】删除", EditorStyles.boldLabel, GUILayout.Height(24));
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                GUILayout.Button("确认删除", GUILayout.Height(24));
                EditorGUI.EndDisabledGroup();
            }

            if (HasInlineAction(msg, InlineActionKeys.AssetDeleteCancel))
                EditorGUILayout.LabelField("【已取消】", EditorStyles.boldLabel, GUILayout.Height(24));
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                GUILayout.Button("取消", GUILayout.Height(24));
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAssetOpsInlineActions(ChatMessage msg, bool readOnlySummary)
        {
            if (HasInlineAction(msg, InlineActionKeys.AssetOpsExecute))
            {
                EditorGUILayout.LabelField("【已确认】执行资源整理", EditorStyles.boldLabel, GUILayout.Height(28));
                return;
            }

            EditorGUI.BeginDisabledGroup(readOnlySummary);
            if (GUILayout.Button("执行资源整理", GUILayout.Height(28)))
            {
                AddInlineAction(msg, InlineActionKeys.AssetOpsExecute);
                PersistChatHistory();
                Repaint();
                ExecuteAssetOps(msg);
            }

            EditorGUI.EndDisabledGroup();
        }

        private void DrawCodeGeneratedState(ChatMessage msg)
        {
            var tw = AssistantBubbleTextWidth();
            string title = msg.Mode switch
            {
                GenerateMode.CombinedPrefabFirst => "✅ <b>第 2 步完成</b>: 代码已生成！",
                GenerateMode.Combined => "✅ <b>第 1 步完成</b>: 代码已生成！",
                _ => "✅ 代码已生成！"
            };
            DrawSelectableLabel(title, _assistantBubbleStyle!, tw);
            if (msg.Mode == GenerateMode.CombinedPrefabFirst)
                DrawSelectableLabel(
                    $"代码步骤: {msg.CodeGenerationTime:F1}秒 | Token: {msg.CodeTokensUsed}（预制体步骤: {msg.GenerationTime:F1}s / {msg.TokensUsed} tok）",
                    EditorStyles.miniLabel, tw);
            else
                DrawSelectableLabel($"耗时: {msg.GenerationTime:F1}秒 | Token: {msg.TokensUsed}", EditorStyles.miniLabel, tw);

            // 联合先预制体再脚本：保存脚本后保留本气泡为「第 2 步完成」，不改成最终成功态
            var combinedPrefabScriptSaved =
                msg.Mode == GenerateMode.CombinedPrefabFirst && !string.IsNullOrEmpty(msg.SavedScriptPath);
            if (combinedPrefabScriptSaved)
            {
                EditorGUILayout.Space(5);
                DrawSelectableLabel($"已保存: {msg.SavedScriptPath}", EditorStyles.miniLabel, tw);
                EditorGUILayout.Space(5);
                if (HasInlineAction(msg, InlineActionKeys.CodePreview))
                    EditorGUILayout.LabelField("【已预览】代码", EditorStyles.boldLabel, GUILayout.Height(25));
                else if (GUILayout.Button("预览代码", GUILayout.Height(25)))
                {
                    AddInlineAction(msg, InlineActionKeys.CodePreview);
                    PreviewWindow.ShowWindow($"{msg.ScriptName}.cs 预览", msg.GeneratedCode);
                    PersistChatHistory();
                    Repaint();
                }

                return;
            }
            
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("脚本名称:", GUILayout.Width(60));
            msg.ScriptName = EditorGUILayout.TextField(msg.ScriptName);
            EditorGUILayout.LabelField(".cs", GUILayout.Width(25));
            EditorGUILayout.EndHorizontal();

            if (ScriptGenerator.ScriptExists(msg.ScriptName))
            {
                DrawSelectableHelpPane($"文件 {msg.ScriptName}.cs 已存在，保存将覆盖", tw);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            
            if (HasInlineAction(msg, InlineActionKeys.CodePreview))
                EditorGUILayout.LabelField("【已预览】代码", EditorStyles.boldLabel, GUILayout.Height(25));
            else if (GUILayout.Button("预览代码", GUILayout.Height(25)))
            {
                AddInlineAction(msg, InlineActionKeys.CodePreview);
                PreviewWindow.ShowWindow($"{msg.ScriptName}.cs 预览", msg.GeneratedCode);
                PersistChatHistory();
                Repaint();
            }

            if (msg.Mode == GenerateMode.Combined)
            {
                if (HasInlineAction(msg, InlineActionKeys.CodeSaveAndContinue))
                    EditorGUILayout.LabelField("【已确认】保存并继续", EditorStyles.boldLabel, GUILayout.Height(25));
                else
                {
                    EditorGUI.BeginDisabledGroup(HasInlineAction(msg, InlineActionKeys.CodeSave));
                    if (GUILayout.Button("保存并继续生成预制体", GUILayout.Height(25)))
                    {
                        AddInlineAction(msg, InlineActionKeys.CodeSaveAndContinue);
                        PersistChatHistory();
                        Repaint();
                        SaveCodeAndContinueCombined(msg);
                    }

                    EditorGUI.EndDisabledGroup();
                }
            }
            else
            {
                if (HasInlineAction(msg, InlineActionKeys.CodeSave))
                    EditorGUILayout.LabelField("【已确认】保存文件", EditorStyles.boldLabel, GUILayout.Height(25));
                else
                {
                    EditorGUI.BeginDisabledGroup(HasInlineAction(msg, InlineActionKeys.CodeSaveAndContinue));
                    if (GUILayout.Button("保存文件", GUILayout.Height(25)))
                    {
                        AddInlineAction(msg, InlineActionKeys.CodeSave);
                        PersistChatHistory();
                        Repaint();
                        SaveScript(msg);
                    }

                    EditorGUI.EndDisabledGroup();
                }
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPrefabGeneratedState(ChatMessage msg)
        {
            var tw = AssistantBubbleTextWidth();
            string title = msg.Mode switch
            {
                GenerateMode.CombinedPrefabFirst => "✅ <b>第 1 步完成</b>: 预制体已生成！",
                GenerateMode.Combined => "✅ <b>第 2 步完成</b>: 预制体已生成！",
                _ => "✅ 预制体已生成！"
            };
            DrawSelectableLabel(title, _assistantBubbleStyle!, tw);
            DrawSelectableLabel($"耗时: {msg.GenerationTime:F1}秒 | Token: {msg.TokensUsed}", EditorStyles.miniLabel, tw);

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("预制体名称:", GUILayout.Width(70));
            msg.PrefabName = EditorGUILayout.TextField(msg.PrefabName);
            EditorGUILayout.LabelField(".prefab", GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            var saveDir = ResolvePrefabSaveFolder();
            DrawSelectableLabel($"保存目录: {saveDir}/", EditorStyles.miniLabel, tw);

            if (PrefabGenerator.PrefabExists(msg.PrefabName, saveDir))
                DrawSelectableHelpPane($"预制体 {msg.PrefabName}.prefab 已存在，保存将覆盖", tw);

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            if (HasInlineAction(msg, InlineActionKeys.PrefabPreviewJson))
                EditorGUILayout.LabelField("【已预览】 JSON", EditorStyles.boldLabel, GUILayout.Height(25));
            else if (GUILayout.Button("预览 JSON", GUILayout.Height(25)))
            {
                AddInlineAction(msg, InlineActionKeys.PrefabPreviewJson);
                PreviewWindow.ShowWindow($"{msg.PrefabName}.prefab JSON 预览", msg.RawJson);
                PersistChatHistory();
                Repaint();
            }

            if (HasInlineAction(msg, InlineActionKeys.PrefabCreate))
                EditorGUILayout.LabelField("【已确认】创建预制体", EditorStyles.boldLabel, GUILayout.Height(25));
            else if (GUILayout.Button("创建预制体", GUILayout.Height(25)))
            {
                AddInlineAction(msg, InlineActionKeys.PrefabCreate);
                PersistChatHistory();
                Repaint();
                SavePrefab(msg);
            }

            EditorGUILayout.EndHorizontal();
        }

        private static string BuildSceneOpsStepSummary(SceneOpsEnvelopeDto env)
        {
            if (env.operations == null || env.operations.Length == 0)
                return "（无步骤）";
            var parts = new List<string>();
            foreach (var op in env.operations)
            {
                var o = string.IsNullOrWhiteSpace(op.op) ? "?" : op.op.Trim();
                parts.Add(o);
            }

            return $"共 {env.operations.Length} 步: " + string.Join(" → ", parts);
        }

        private void DrawSceneOpsReadyState(ChatMessage msg)
        {
            var tw = AssistantBubbleTextWidth();
            DrawSelectableLabel("✅ 场景操控列表已生成（请预览后执行）", _assistantBubbleStyle!, tw);
            DrawSelectableLabel($"耗时: {msg.GenerationTime:F1}秒 | Token: {msg.TokensUsed}", EditorStyles.miniLabel, tw);

            if (msg.SceneOpsEnvelope != null)
            {
                EditorGUILayout.Space(4);
                DrawSelectableLabel(BuildSceneOpsStepSummary(msg.SceneOpsEnvelope), EditorStyles.wordWrappedMiniLabel, tw);
            }

            EditorGUILayout.Space(5);
            DrawSceneOpsInlineActions(msg, readOnlySummary: false);
        }

        private void DrawAssetDeleteReadyState(ChatMessage msg)
        {
            var tw = AssistantBubbleTextWidth();
            DrawSelectableLabel("✅ 已解析待删除资源（请确认后执行）", _assistantBubbleStyle!, tw);
            DrawSelectableLabel($"耗时: {msg.GenerationTime:F1}秒 | Token: {msg.TokensUsed}", EditorStyles.miniLabel, tw);

            if (!string.IsNullOrWhiteSpace(msg.Content))
            {
                EditorGUILayout.Space(4);
                DrawSelectableLabel(msg.Content, EditorStyles.wordWrappedMiniLabel, tw);
            }

            if (msg.AssetDeletePaths is { Count: > 0 })
            {
                EditorGUILayout.Space(4);
                foreach (var p in msg.AssetDeletePaths)
                    DrawSelectableLabel("• " + p, EditorStyles.miniLabel, tw);
            }

            EditorGUILayout.Space(5);
            DrawAssetDeleteInlineActions(msg);
        }

        private void ExecuteConfirmedAssetDelete(ChatMessage msg)
        {
            if (msg.AssetDeletePaths == null || msg.AssetDeletePaths.Count == 0)
                return;

            var ok = 0;
            var failed = new List<string>();
            foreach (var path in msg.AssetDeletePaths)
            {
                if (!AssetPathSecurity.TryValidateGenericAssetPath(path, out var norm, out var err))
                {
                    failed.Add($"{path}: {err}");
                    continue;
                }

                if (!AssetDeleteParser.AssetExistsForDelete(norm))
                {
                    failed.Add($"{norm}（资源不存在或已删除）");
                    continue;
                }

                if (AssetDatabase.DeleteAsset(norm))
                    ok++;
                else
                    failed.Add($"{norm}（DeleteAsset 失败）");
            }

            AssetDatabase.Refresh();
            var summary = failed.Count == 0
                ? $"已删除 {ok} 个资源。"
                : $"已删除 {ok} 个资源。\n\n失败或跳过：\n" + string.Join("\n", failed);
            AddTextBubble(summary);
            Repaint();
            ScrollToBottom();
        }

        private static string BuildAssetOpsStepSummary(AssetOpsEnvelopeDto env)
        {
            if (env.operations == null || env.operations.Length == 0)
                return "（无步骤）";
            var parts = new List<string>();
            foreach (var op in env.operations)
            {
                var o = string.IsNullOrWhiteSpace(op.op) ? "?" : op.op.Trim();
                parts.Add(o);
            }

            return $"共 {env.operations.Length} 步: " + string.Join(" → ", parts);
        }

        private void DrawAssetOpsReadyState(ChatMessage msg)
        {
            var tw = AssistantBubbleTextWidth();
            DrawSelectableLabel("✅ 资源整理步骤已生成（请预览后执行）", _assistantBubbleStyle!, tw);
            DrawSelectableLabel($"耗时: {msg.GenerationTime:F1}秒 | Token: {msg.TokensUsed}", EditorStyles.miniLabel, tw);

            if (msg.AssetOpsEnvelope != null)
            {
                EditorGUILayout.Space(4);
                DrawSelectableLabel(BuildAssetOpsStepSummary(msg.AssetOpsEnvelope), EditorStyles.wordWrappedMiniLabel, tw);
            }

            if (!string.IsNullOrWhiteSpace(msg.Content))
            {
                EditorGUILayout.Space(4);
                DrawSelectableLabel(msg.Content, EditorStyles.wordWrappedMiniLabel, tw);
            }

            EditorGUILayout.Space(5);
            DrawAssetOpsInlineActions(msg, readOnlySummary: false);
        }

        private void ExecuteAssetOps(ChatMessage msg)
        {
            if (msg.AssetOpsEnvelope == null)
            {
                RemoveInlineAction(msg, InlineActionKeys.AssetOpsExecute);
                PersistChatHistory();
                EditorUtility.DisplayDialog("资源整理", "内部错误：未找到已解析的操作列表。", "确定");
                Repaint();
                return;
            }

            var batch = MainThread.IsMainThread
                ? AssetOpsExecutor.Execute(msg.AssetOpsEnvelope)
                : MainThread.Run(() => AssetOpsExecutor.Execute(msg.AssetOpsEnvelope));

            if (batch.Success)
            {
                _isGenerating = false;
                AppendAssetOpsExecutionResultBubble(batch.StepsCompleted);
                Repaint();
                ScrollToBottom();
                return;
            }

            RemoveInlineAction(msg, InlineActionKeys.AssetOpsExecute);
            PersistChatHistory();
            EditorUtility.DisplayDialog(
                "资源整理失败",
                batch.Error ?? "未知错误",
                "确定");
            Repaint();
        }

        private void DrawWaitingCompileState(ChatMessage msg)
        {
            var tw = AssistantBubbleTextWidth();
            DrawSelectableLabel($"✓ 脚本已保存: {msg.SavedScriptPath}", EditorStyles.wordWrappedLabel, tw);

            if (msg.CompileWaitCancelled)
            {
                DrawSelectableLabel("已取消联合生成。", EditorStyles.wordWrappedLabel, tw);
                return;
            }

            if (msg.CompileWaitFinished)
            {
                DrawSelectableLabel("✓ 编译完成。", EditorStyles.wordWrappedLabel, tw);
                return;
            }

            var dots = new string('.', (msg.CompileWaitTicks / 10 % 4) + 1);
            var waitSeconds = msg.CompileWaitTicks * 0.1f;

            DrawSelectableLabel($"⟳ 等待 Unity 编译完成{dots} ({waitSeconds:F1}秒)", EditorStyles.wordWrappedLabel, tw);

            EditorGUILayout.Space(5);
            if (GUILayout.Button("取消联合生成", GUILayout.Width(150), GUILayout.Height(25)))
            {
                msg.CompileWaitCancelled = true;
                _isGenerating = false;
                EditorApplication.update -= OnCompileWaitUpdate;
                _pendingMessage = null;
                AddTextBubble("已取消联合生成，仅保留已保存的脚本。");
                PersistChatHistory();
                Repaint();
                ScrollToBottom();
            }

            Repaint();
        }

        private void DrawSuccessState(ChatMessage msg)
        {
            var tw = AssistantBubbleTextWidth();
            string text = msg.Mode switch
            {
                GenerateMode.SceneOps => "🎉 场景操控执行成功！",
                GenerateMode.AssetOps => "🎉 资源整理执行成功！",
                GenerateMode.Combined when !string.IsNullOrEmpty(msg.SavedPrefabPath) => "🎉 联合生成最终完成！",
                GenerateMode.CombinedPrefabFirst when !string.IsNullOrEmpty(msg.SavedPrefabPath) && !string.IsNullOrEmpty(msg.SavedScriptPath) => "🎉 联合生成最终完成！",
                GenerateMode.Code => "🎉 代码生成并保存成功！",
                _ => "🎉 预制体生成并保存成功！"
            };
                
            DrawSelectableLabel($"<b>{text}</b>", _assistantBubbleStyle!, tw);
            
            EditorGUILayout.Space(5);

            // 仅当本条仍是「带 envelope 的旧版单气泡成功态」时绘制只读按钮行；新版执行结果在独立气泡中，无 envelope。
            if (msg.Mode == GenerateMode.SceneOps && msg.SceneOpsEnvelope != null)
                DrawSceneOpsInlineActions(msg, readOnlySummary: true);
            if (msg.Mode == GenerateMode.AssetOps && msg.AssetOpsEnvelope != null)
                DrawAssetOpsInlineActions(msg, readOnlySummary: true);

            if (!string.IsNullOrEmpty(msg.SavedScriptPath))
                DrawSelectableLabel($"已生成脚本: {msg.SavedScriptPath}", EditorStyles.miniLabel, tw);
            
            if (!string.IsNullOrEmpty(msg.SavedPrefabPath))
                DrawSelectableLabel($"已生成预制体: {msg.SavedPrefabPath}", EditorStyles.miniLabel, tw);

            if (msg.Mode == GenerateMode.SceneOps)
                DrawSelectableLabel(
                    $"场景操控：已执行 {msg.SceneOpsExecutedStepCount} 步，跳过 {msg.SceneOpsSkippedStepCount} 步（工作区确认）",
                    EditorStyles.miniLabel, tw);

            if (msg.Mode == GenerateMode.AssetOps)
                DrawSelectableLabel($"资源整理：已执行 {msg.AssetOpsExecutedStepCount} 步。", EditorStyles.miniLabel, tw);

            if (msg.PrefabWarnings.Count > 0)
            {
                EditorGUILayout.Space(2);
                foreach (var w in msg.PrefabWarnings)
                    DrawSelectableHelpPane(w, tw);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            
            if (!string.IsNullOrEmpty(msg.SavedScriptPath) && GUILayout.Button("打开脚本", GUILayout.Height(25)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(msg.SavedScriptPath);
                if (asset != null) AssetDatabase.OpenAsset(asset);
            }
            
            if (!string.IsNullOrEmpty(msg.SavedPrefabPath) && GUILayout.Button("选中预制体", GUILayout.Height(25)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(msg.SavedPrefabPath);
                if (asset != null)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawErrorState(ChatMessage msg)
        {
            var tw = AssistantBubbleTextWidth();
            DrawSelectableLabel("❌ <b>生成失败</b>", _assistantBubbleStyle!, tw);
            if (!string.IsNullOrEmpty(msg.ErrorMessage))
                DrawSelectableHelpPane(msg.ErrorMessage, tw);
            
            EditorGUILayout.Space(5);
            if (GUILayout.Button("重试此任务", GUILayout.Width(100), GUILayout.Height(25)))
            {
                RetryTask(msg);
            }
        }

        #endregion

    }
}
