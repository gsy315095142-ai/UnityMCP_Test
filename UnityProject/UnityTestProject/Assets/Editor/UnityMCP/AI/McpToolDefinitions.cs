#nullable enable

namespace UnityMCP.AI
{
    /// <summary>
    /// 所有暴露给 AI 的工具定义（OpenAI function-calling 格式的 JSON 数组字符串）。
    /// 每个工具对应 <see cref="McpToolNames"/> 中的常量名称。
    /// </summary>
    public static class McpToolDefinitions
    {
        /// <summary>完整的 tools JSON 数组，直接嵌入 API 请求的 "tools" 字段。</summary>
        public static string GetToolsJson() => @"[
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""get_scene_state"",
      ""description"": ""获取当前场景的 Hierarchy 树（所有 GameObject 完整路径）以及当前编辑器选中的物体。在执行任何场景操作之前必须先调用，以了解真实路径。"",
      ""parameters"": { ""type"": ""object"", ""properties"": {}, ""required"": [] }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""get_project_info"",
      ""description"": ""获取当前工程的资源摘要（C# 脚本列表、预制体列表、材质列表、贴图列表等）。在生成代码或引用已有资源时可先调用。"",
      ""parameters"": { ""type"": ""object"", ""properties"": {}, ""required"": [] }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""execute_scene_ops"",
      ""description"": ""在场景中执行操作（创建/修改/删除 GameObject 等）。operations_json 为 unity-ops 格式的 JSON 数组字符串（op 可用：createEmpty, createPrimitive, instantiatePrefab, destroy, duplicate, setParent, addComponent, setTransform, setComponentProperty, setRectTransform, setUiText, setActive, setLayer, setTag, openScene, saveScene）。执行后返回每一步的成功/失败信息。\n\n【重要】向量/位置字段必须使用逗号分隔字符串，不能用 JSON 对象：\n- setTransform 的 localPosition/localEulerAngles/localScale：字符串格式 x,y,z，例如 localPosition 填 -80,0,0\n- setRectTransform 的 anchoredPosition/anchorMin/anchorMax/sizeDelta/pivot/offsetMin/offsetMax：字符串格式 x,y，例如 anchoredPosition 填 -80,0\n\n错误示例（禁止）：localPosition: {x: -80, y: 0, z: 0}\n正确示例：localPosition: -80,0,0"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""operations_json"": {
            ""type"": ""string"",
            ""description"": ""unity-ops 操作数组的 JSON 字符串。向量字段（localPosition/localEulerAngles/localScale/anchoredPosition/anchorMin/anchorMax/sizeDelta/pivot 等）必须是逗号分隔字符串（如 localPosition 填 x,y,z 格式字符串：-80,0,0；anchoredPosition 填 x,y 格式字符串：-80,0），禁止使用 {x:...,y:...} JSON 对象。""
          }
        },
        ""required"": [""operations_json""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""generate_code"",
      ""description"": ""生成并保存 C# 脚本到工程中。由专门的代码生成 AI 完成，会自动避免与现有脚本重名。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""description"": { ""type"": ""string"", ""description"": ""脚本功能的详细描述"" },
          ""class_name"":  { ""type"": ""string"", ""description"": ""期望的类名（不含 .cs 后缀），留空则由 AI 自动决定"" },
          ""code_type"":   {
            ""type"": ""string"",
            ""enum"": [""Auto"", ""MonoBehaviour"", ""ScriptableObject"", ""Editor"", ""StaticUtility""],
            ""description"": ""代码类型，留空或 Auto 则由代码生成 AI 判断""
          }
        },
        ""required"": [""description""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""create_prefab"",
      ""description"": ""生成并保存 Unity 预制体（.prefab 文件）到工程中。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""description"":    { ""type"": ""string"", ""description"": ""预制体功能与外观的详细描述"" },
          ""prefab_name"":    { ""type"": ""string"", ""description"": ""期望的预制体名称，留空则由 AI 决定"" },
          ""place_in_scene"": { ""type"": ""boolean"", ""description"": ""生成后是否立即放入当前场景，默认 false"" }
        },
        ""required"": [""description""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""delete_assets"",
      ""description"": ""删除工程中的资源文件（脚本、预制体、材质等）。asset_paths 为 Assets/... 格式的路径列表（JSON 数组字符串）。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""asset_paths_json"": {
            ""type"": ""string"",
            ""description"": ""要删除的资源路径 JSON 数组字符串，每个路径必须以 Assets/ 开头。""
          }
        },
        ""required"": [""asset_paths_json""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""organize_assets"",
      ""description"": ""移动、复制、重命名 Assets 下的文件或文件夹（asset-ops 协议）。operations_json 为 asset-ops 数组（op: moveAsset/copyAsset/renameAsset/createFolder）。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""operations_json"": {
            ""type"": ""string"",
            ""description"": ""asset-ops 操作数组的 JSON 字符串""
          }
        },
        ""required"": [""operations_json""]
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""reply"",
      ""description"": ""向用户发送最终回复。当所有操作完成、或需要解释/说明时调用。调用此工具后对话本轮结束。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""message"": { ""type"": ""string"", ""description"": ""回复内容（支持 Markdown）"" }
        },
        ""required"": [""message""]
      }
    }
  }
]";
    }

    /// <summary>工具名称常量，与 JSON 中的 name 字段保持一致。</summary>
    public static class McpToolNames
    {
        public const string GetSceneState   = "get_scene_state";
        public const string GetProjectInfo  = "get_project_info";
        public const string ExecuteSceneOps = "execute_scene_ops";
        public const string GenerateCode    = "generate_code";
        public const string CreatePrefab    = "create_prefab";
        public const string DeleteAssets    = "delete_assets";
        public const string OrganizeAssets  = "organize_assets";
        public const string Reply           = "reply";
    }
}
