#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.AI;
using UnityMCP.Tools;

namespace UnityMCP.UI
{
    /// <summary>
    /// 将聊天历史写入 <see cref="SessionState"/>，在域重载（脚本编译）后恢复，避免记录丢失。
    /// </summary>
    public static class ChatHistoryPersistence
    {
        private const string SessionKey = "UnityMCP.LumiChatHistory.v1";
        private const int MaxGeneratedCodeChars = 120_000;

        [Serializable]
        private sealed class PersistedList
        {
            public PersistedMessage[] items = Array.Empty<PersistedMessage>();
        }

        [Serializable]
        private sealed class PersistedMessage
        {
            public int role;
            public int type;
            public int mode;
            public int codeType;
            public bool combinedPrefabFirst;
            public string content = "";
            public string errorMessage = "";
            public string generatedCode = "";
            public string scriptName = "";
            public string prefabName = "";
            public string rawJson = "";
            public string savedScriptPath = "";
            public string savedPrefabPath = "";
            public string prefabWarningsJoined = "";
            public float generationTime;
            public int tokensUsed;
            public float codeGenerationTime;
            public int codeTokensUsed;
            public int sceneOpsExecuted;
            public int sceneOpsSkipped;
            public int compileWaitTicks;
            public string assetDeletePathsJoined = "";
            public string inlineActionsClicked = "";
        }

        public static void Save(IReadOnlyList<ChatMessage> history)
        {
            try
            {
                var items = new List<PersistedMessage>();
                foreach (var m in history)
                    items.Add(ToPersisted(m));

                var wrap = new PersistedList { items = items.ToArray() };
                var json = JsonUtility.ToJson(wrap);
                SessionState.SetString(SessionKey, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityMCP] 保存聊天历史失败: {ex.Message}");
            }
        }

        public static List<ChatMessage>? TryLoad()
        {
            try
            {
                var json = SessionState.GetString(SessionKey, "");
                if (string.IsNullOrEmpty(json))
                    return null;

                var wrap = JsonUtility.FromJson<PersistedList>(json);
                if (wrap?.items == null || wrap.items.Length == 0)
                    return null;

                var list = new List<ChatMessage>();
                foreach (var p in wrap.items)
                {
                    var m = FromPersisted(p);
                    if (m != null)
                        list.Add(m);
                }

                return list.Count > 0 ? list : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityMCP] 加载聊天历史失败: {ex.Message}");
                return null;
            }
        }

        public static void Clear()
        {
            SessionState.EraseString(SessionKey);
        }

        private static PersistedMessage ToPersisted(ChatMessage m)
        {
            var code = m.GeneratedCode ?? "";
            if (code.Length > MaxGeneratedCodeChars)
                code = code.Substring(0, MaxGeneratedCodeChars) + "\n/* …已截断… */";

            return new PersistedMessage
            {
                role = (int)m.Role,
                type = (int)m.Type,
                mode = (int)m.Mode,
                codeType = (int)m.CodeType,
                combinedPrefabFirst = m.CombinedPrefabFirst,
                content = m.Content ?? "",
                errorMessage = m.ErrorMessage ?? "",
                generatedCode = code,
                scriptName = m.ScriptName ?? "",
                prefabName = m.PrefabName ?? "",
                rawJson = m.RawJson ?? "",
                savedScriptPath = m.SavedScriptPath ?? "",
                savedPrefabPath = m.SavedPrefabPath ?? "",
                prefabWarningsJoined = string.Join("\n", m.PrefabWarnings ?? new List<string>()),
                generationTime = m.GenerationTime,
                tokensUsed = m.TokensUsed,
                codeGenerationTime = m.CodeGenerationTime,
                codeTokensUsed = m.CodeTokensUsed,
                sceneOpsExecuted = m.SceneOpsExecutedStepCount,
                sceneOpsSkipped = m.SceneOpsSkippedStepCount,
                compileWaitTicks = m.CompileWaitTicks,
                assetDeletePathsJoined = string.Join("\n", m.AssetDeletePaths ?? new List<string>()),
                inlineActionsClicked = m.InlineActionsClicked ?? ""
            };
        }

        private static ChatMessage? FromPersisted(PersistedMessage p)
        {
            var m = new ChatMessage
            {
                Role = (ChatRole)p.role,
                Type = (MessageTypeEnum)p.type,
                Mode = (GenerateMode)p.mode,
                CodeType = (CodeType)p.codeType,
                CombinedPrefabFirst = p.combinedPrefabFirst,
                Content = p.content ?? "",
                ErrorMessage = p.errorMessage ?? "",
                GeneratedCode = p.generatedCode ?? "",
                ScriptName = p.scriptName ?? "",
                PrefabName = p.prefabName ?? "",
                RawJson = p.rawJson ?? "",
                SavedScriptPath = p.savedScriptPath ?? "",
                SavedPrefabPath = p.savedPrefabPath ?? "",
                GenerationTime = p.generationTime,
                TokensUsed = p.tokensUsed,
                CodeGenerationTime = p.codeGenerationTime,
                CodeTokensUsed = p.codeTokensUsed,
                SceneOpsExecutedStepCount = p.sceneOpsExecuted,
                SceneOpsSkippedStepCount = p.sceneOpsSkipped,
                CompileWaitTicks = p.compileWaitTicks,
                InlineActionsClicked = p.inlineActionsClicked ?? ""
            };

            if (!string.IsNullOrEmpty(p.assetDeletePathsJoined))
                m.AssetDeletePaths = p.assetDeletePathsJoined.Split('\n').Where(s => !string.IsNullOrEmpty(s)).ToList();

            if (!string.IsNullOrEmpty(p.prefabWarningsJoined))
                m.PrefabWarnings = p.prefabWarningsJoined.Split('\n').Where(s => !string.IsNullOrEmpty(s)).ToList();

            if (m.Type == MessageTypeEnum.PrefabGenerated && m.PrefabDescription == null &&
                !string.IsNullOrEmpty(m.RawJson))
            {
                var pr = ResponseParser.ParsePrefabResponse(m.RawJson);
                if (pr.Success && pr.Description != null)
                {
                    m.PrefabDescription = pr.Description;
                    if (string.IsNullOrEmpty(m.PrefabName))
                        m.PrefabName = pr.Description.prefabName;
                }
            }

            if (m.Type == MessageTypeEnum.SceneOpsReady && m.SceneOpsEnvelope == null && !string.IsNullOrEmpty(m.RawJson))
            {
                var sr = SceneOpsParser.Parse(m.RawJson);
                if (sr.Success && sr.Envelope != null)
                    m.SceneOpsEnvelope = sr.Envelope;
            }

            if (m.Type == MessageTypeEnum.AssetOpsReady && m.AssetOpsEnvelope == null && !string.IsNullOrEmpty(m.RawJson))
            {
                var ar = AssetOpsParser.Parse(m.RawJson);
                if (ar.Success && ar.Envelope != null)
                    m.AssetOpsEnvelope = ar.Envelope;
            }

            return m;
        }
    }
}
