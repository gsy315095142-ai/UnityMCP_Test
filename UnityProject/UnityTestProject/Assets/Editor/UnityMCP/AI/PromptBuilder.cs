#nullable enable

using UnityMCP.Core;

namespace UnityMCP.AI
{
    /// <summary>
    /// Prompt 构建器。
    /// 负责将用户需求和项目上下文组合成完整的 AI 提示词。
    /// </summary>
    public static class PromptBuilder
    {
        /// <summary>
        /// 构建代码生成的系统 Prompt
        /// </summary>
        /// <param name="context">项目上下文</param>
        /// <returns>系统提示词</returns>
        public static string BuildCodeSystemPrompt(ProjectContext context)
        {
            return $@"你是一个专业的 Unity C# 代码生成助手。请根据用户的需求生成高质量的 C# 代码。

## 输出规则（必须严格遵守）
1. 只输出一个完整的 C# 源文件，用 ```csharp 和 ``` 包裹
2. 代码必须是完整的、可直接编译的
3. 必须包含所有需要的 using 语句
4. 类名使用 PascalCase
5. 私有字段使用 camelCase 并添加 [SerializeField] 特性
6. 所有公共成员添加中文 XML 文档注释（/// <summary>）
7. 不要输出多个代码块，只输出一个
8. 不要输出额外的说明文字，只输出代码块

## 代码风格
- 使用 #region 分组（Fields, Unity Methods, Public Methods, Private Methods）
- Unity 生命周期方法按执行顺序排列（Awake, OnEnable, Start, Update, OnDisable, OnDestroy）
- 优先使用 [SerializeField] private 而非 public 字段
- 添加合理的默认值
- 确保代码在 VR 环境下也有良好的性能

{context.ToPromptContext()}";
        }

        /// <summary>
        /// 构建代码生成的用户 Prompt
        /// </summary>
        /// <param name="userRequest">用户需求描述</param>
        /// <returns>用户提示词</returns>
        public static string BuildCodeUserPrompt(string userRequest)
        {
            return $@"请根据以下需求生成 Unity C# 脚本：

{userRequest}

请直接输出完整的 C# 代码文件（用 ```csharp 包裹），不要输出其他内容。";
        }

        /// <summary>
        /// 构建预制体生成的系统 Prompt（Phase 2 使用）
        /// </summary>
        public static string BuildPrefabSystemPrompt(ProjectContext context)
        {
            return $@"你是一个专业的 Unity 预制体生成助手。请根据用户的需求描述，输出 JSON 格式的预制体定义。

## 输出格式（必须严格遵守）
用 ```json 和 ``` 包裹输出，格式如下：
{{
  ""prefabName"": ""名称"",
  ""rootObject"": {{
    ""name"": ""根对象名"",
    ""position"": [0, 0, 0],
    ""rotation"": [0, 0, 0],
    ""scale"": [1, 1, 1],
    ""components"": [
      {{
        ""type"": ""完整类型名（如 UnityEngine.Rigidbody）"",
        ""properties"": {{
          ""属性名"": ""属性值""
        }}
      }}
    ],
    ""children"": []
  }}
}}

不要输出额外的说明文字，只输出 JSON。

{context.ToPromptContext()}";
        }

        /// <summary>
        /// 构建预制体生成的用户 Prompt（Phase 2 使用）
        /// </summary>
        public static string BuildPrefabUserPrompt(string userRequest)
        {
            return $@"请根据以下需求生成 Unity 预制体定义（JSON 格式）：

{userRequest}

请直接输出 JSON（用 ```json 包裹），不要输出其他内容。";
        }
    }
}
