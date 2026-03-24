#nullable enable

using System.Collections.Generic;
using UnityMCP.AI;
using UnityMCP.Generators;
using UnityMCP.Tools;

namespace UnityMCP.UI
{
    /// <summary>
    /// 聊天消息角色
    /// </summary>
    public enum ChatRole
    {
        User,
        Assistant
    }

    /// <summary>
    /// 消息类型，用于指导气泡内的 UI 绘制
    /// </summary>
    public enum MessageTypeEnum
    {
        Text,               // 纯文本消息（普通回复或等待提示）
        CodeGenerated,      // 代码生成完毕，等待用户保存
        WaitingCompile,     // 等待编译中
        PrefabGenerated,    // 预制体生成完毕，等待用户保存
        SuccessResult,      // 最终成功结果展示
        Error,               // 错误展示
        SceneOpsReady,       // 场景操控 JSON 已解析，等待用户执行
        AssetDeleteReady,    // 待删除资源路径已解析，等待用户确认
        AssetOpsReady        // asset-ops JSON 已解析，等待用户执行
    }

    /// <summary>
    /// 生成模式
    /// </summary>
    public enum GenerateMode
    {
        /// <summary>由 AI 根据自然语言判断生成代码、预制体或联合生成</summary>
        AiJudge = 0,
        Code = 1,
        Prefab = 2,
        Combined = 3,
        /// <summary>当前场景层级操控（unity-ops）</summary>
        SceneOps = 4,
        /// <summary>联合生成：先预制体再脚本（仅 AI 判断可进入，避免先编译丢会话）</summary>
        CombinedPrefabFirst = 5,
        /// <summary>基于项目扫描结果回答（盘点预制体、已有资源等），不生成新资源</summary>
        ProjectQuery = 6,
        /// <summary>从 Project 删除资源（AI 输出路径 JSON，确认后执行）</summary>
        AssetDelete = 7,
        /// <summary>移动/复制/建文件夹等（asset-ops JSON）</summary>
        AssetOps = 8
    }

    /// <summary>
    /// 单条聊天消息的数据结构
    /// </summary>
    public class ChatMessage
    {
        public ChatRole Role;
        public MessageTypeEnum Type = MessageTypeEnum.Text;
        public string Content = "";

        // 所属任务的状态关联
        public GenerateMode Mode;
        public CodeType CodeType;

        // 生成结果数据
        public string ErrorMessage = "";

        public string GeneratedCode = "";
        public string ScriptName = "";

        public PrefabDescription? PrefabDescription;
        public string PrefabName = "";
        public string RawJson = "";
        public List<string> PrefabWarnings = new();

        /// <summary>场景操控：解析成功的 envelope，供预览与执行。</summary>
        public SceneOpsEnvelopeDto? SceneOpsEnvelope;
        /// <summary>场景操控执行成功后完成的步数。</summary>
        public int SceneOpsExecutedStepCount;
        /// <summary>因工作区确认被跳过的步数。</summary>
        public int SceneOpsSkippedStepCount;

        public string SavedScriptPath = "";
        public string SavedPrefabPath = "";

        // 进度/耗时统计
        public float GenerationTime;
        public int TokensUsed;
        public float CodeGenerationTime;  // 联合模式步骤1耗时
        public int CodeTokensUsed;        // 联合模式步骤1 Token

        public int CompileWaitTicks;

        /// <summary>联合生成：等待编译阶段已结束（原气泡仍保持 WaitingCompile，仅 UI 冻结为「编译完成」）。</summary>
        public bool CompileWaitFinished;

        /// <summary>联合生成：用户在等待编译时取消了继续生成预制体。</summary>
        public bool CompileWaitCancelled;

        /// <summary>联合生成且为先预制体再脚本（由 AI 判断 combinedOrder 决定）。</summary>
        public bool CombinedPrefabFirst;

        /// <summary>待删除的资源路径（<see cref="MessageTypeEnum.AssetDeleteReady"/>）。</summary>
        public List<string> AssetDeletePaths = new();

        /// <summary>资源整理：解析成功的 envelope。</summary>
        public AssetOpsEnvelopeDto? AssetOpsEnvelope;
        public int AssetOpsExecutedStepCount;

        /// <summary>气泡内操作按钮已点击的键（逗号分隔），用于【已确认】与未选项置灰。</summary>
        public string InlineActionsClicked = "";

        // 快捷创建文本消息
        public static ChatMessage CreateText(ChatRole role, string text) => new()
        {
            Role = role,
            Type = MessageTypeEnum.Text,
            Content = text
        };

        /// <summary>复制一条消息用于新气泡，避免多条 UI 共用同一引用被异步就地改写。</summary>
        public static ChatMessage CloneSnapshot(ChatMessage a)
        {
            return new ChatMessage
            {
                Role = a.Role,
                Type = a.Type,
                Content = a.Content,
                Mode = a.Mode,
                CodeType = a.CodeType,
                ErrorMessage = a.ErrorMessage,
                GeneratedCode = a.GeneratedCode,
                ScriptName = a.ScriptName,
                PrefabDescription = a.PrefabDescription,
                PrefabName = a.PrefabName,
                RawJson = a.RawJson,
                PrefabWarnings = new List<string>(a.PrefabWarnings),
                SceneOpsEnvelope = a.SceneOpsEnvelope,
                SceneOpsExecutedStepCount = a.SceneOpsExecutedStepCount,
                SceneOpsSkippedStepCount = a.SceneOpsSkippedStepCount,
                SavedScriptPath = a.SavedScriptPath,
                SavedPrefabPath = a.SavedPrefabPath,
                GenerationTime = a.GenerationTime,
                TokensUsed = a.TokensUsed,
                CodeGenerationTime = a.CodeGenerationTime,
                CodeTokensUsed = a.CodeTokensUsed,
                CompileWaitTicks = a.CompileWaitTicks,
                CompileWaitFinished = a.CompileWaitFinished,
                CompileWaitCancelled = a.CompileWaitCancelled,
                CombinedPrefabFirst = a.CombinedPrefabFirst,
                AssetDeletePaths = new List<string>(a.AssetDeletePaths),
                AssetOpsEnvelope = a.AssetOpsEnvelope,
                AssetOpsExecutedStepCount = a.AssetOpsExecutedStepCount,
                InlineActionsClicked = a.InlineActionsClicked ?? ""
            };
        }
    }
}
