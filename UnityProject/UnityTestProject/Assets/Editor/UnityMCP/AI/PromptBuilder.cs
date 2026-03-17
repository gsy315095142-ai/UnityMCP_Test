#nullable enable

using UnityMCP.Core;

namespace UnityMCP.AI
{
    /// <summary>
    /// 代码类型枚举，用于指导 AI 生成不同类型的脚本
    /// </summary>
    public enum CodeType
    {
        Auto = 0,
        MonoBehaviour = 1,
        ScriptableObject = 2,
        ManagerSingleton = 3
    }

    /// <summary>
    /// Prompt 构建器。
    /// 负责将用户需求和项目上下文组合成完整的 AI 提示词。
    /// </summary>
    public static class PromptBuilder
    {
        private static readonly string[] CODE_TYPE_LABELS = { "自动检测", "MonoBehaviour", "ScriptableObject", "Manager 单例" };
        public static string[] CodeTypeLabels => CODE_TYPE_LABELS;

        #region 代码生成 Prompt

        /// <summary>
        /// 构建代码生成的系统 Prompt
        /// </summary>
        public static string BuildCodeSystemPrompt(ProjectContext context, CodeType codeType = CodeType.Auto)
        {
            var typeGuidance = codeType switch
            {
                CodeType.ScriptableObject => SCRIPTABLEOBJECT_GUIDANCE,
                CodeType.ManagerSingleton => MANAGER_GUIDANCE,
                CodeType.MonoBehaviour => MONOBEHAVIOUR_GUIDANCE,
                _ => AUTO_GUIDANCE
            };

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

{typeGuidance}

## 代码风格
- 使用 #region 分组
- 优先使用 [SerializeField] private 而非 public 字段
- 添加合理的默认值
- 确保代码在 VR 环境下也有良好的性能

{context.ToPromptContext()}";
        }

        /// <summary>
        /// 构建代码生成的用户 Prompt
        /// </summary>
        public static string BuildCodeUserPrompt(string userRequest)
        {
            return $@"请根据以下需求生成 Unity C# 脚本：

{userRequest}

请直接输出完整的 C# 代码文件（用 ```csharp 包裹），不要输出其他内容。";
        }

        private const string AUTO_GUIDANCE = @"## 代码类型
根据用户描述自动判断应生成的代码类型：
- 如果描述涉及数据配置、属性定义，生成 ScriptableObject
- 如果描述涉及全局管理、单例，生成 Manager 单例类
- 其他情况默认生成 MonoBehaviour";

        private const string MONOBEHAVIOUR_GUIDANCE = @"## 代码类型要求：MonoBehaviour
- 继承 MonoBehaviour
- 使用 #region 分组（Fields, Unity Methods, Public Methods, Private Methods）
- Unity 生命周期方法按执行顺序排列（Awake, OnEnable, Start, Update, OnDisable, OnDestroy）";

        private const string SCRIPTABLEOBJECT_GUIDANCE = @"## 代码类型要求：ScriptableObject
- 继承 ScriptableObject
- 必须添加 [CreateAssetMenu] 特性，设置合理的 fileName 和 menuName
  示例: [CreateAssetMenu(fileName = ""NewWeaponConfig"", menuName = ""配置/武器配置"")]
- 使用 #region 分组（Configuration Fields, Computed Properties, Validation）
- 字段使用 [SerializeField] 并添加 [Header] 分组和 [Tooltip] 说明
- 可添加 OnValidate() 方法进行数据验证
- 可添加只读计算属性方便外部访问";

        private const string MANAGER_GUIDANCE = @"## 代码类型要求：Manager 单例
- 继承 MonoBehaviour
- 实现线程安全的单例模式
- 使用以下标准单例模板：
  ```
  public static ClassName Instance { get; private set; }
  private void Awake()
  {
      if (Instance != null && Instance != this) { Destroy(gameObject); return; }
      Instance = this;
      DontDestroyOnLoad(gameObject);
  }
  ```
- 使用 #region 分组（Singleton, Fields, Unity Methods, Public Methods, Private Methods）
- 在 OnDestroy 中清理 Instance 引用";

        #endregion

        #region 预制体生成 Prompt

        /// <summary>
        /// 构建预制体生成的系统 Prompt
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
    ""tag"": ""Untagged"",
    ""active"": true,
    ""position"": [0, 0, 0],
    ""rotation"": [0, 0, 0],
    ""scale"": [1, 1, 1],
    ""components"": [
      {{
        ""type"": ""组件类型名"",
        ""properties"": {{
          ""属性名"": ""属性值""
        }}
      }}
    ],
    ""children"": []
  }}
}}

## 组件类型名规则
- 使用短名即可，如 Rigidbody、BoxCollider、MeshRenderer、Light、Camera 等
- 支持的 3D 组件: Rigidbody, BoxCollider, SphereCollider, CapsuleCollider, MeshCollider, MeshFilter, MeshRenderer, SkinnedMeshRenderer, CharacterController, Animator, AudioSource, Light, Camera, NavMeshAgent, ParticleSystem
- 支持的 UI 组件: Canvas, CanvasScaler, GraphicRaycaster, Image, RawImage, Button, Text, InputField, Toggle, Slider, Dropdown, ScrollRect, HorizontalLayoutGroup, VerticalLayoutGroup, GridLayoutGroup, Mask, Outline, CanvasGroup, TextMeshProUGUI, TMP_InputField, TMP_Dropdown
- 自定义脚本使用类名即可

## UI 预制体特殊规则
如果用户要求创建 UI 元素：
1. 根对象必须添加 Canvas、CanvasScaler、GraphicRaycaster 三个组件
2. Canvas 的 renderMode 属性设为 ""ScreenSpaceOverlay""（或根据需求）
3. 所有 UI 子对象的 position/scale 用 RectTransform 属性，通过 properties 设置 anchorMin、anchorMax、sizeDelta 等
4. Button 需要一个子对象 Text 或 TextMeshProUGUI 显示按钮文字
5. InputField 需要子对象 Placeholder 和 Text

## 属性值格式
- 数字: 直接写数字，如 ""mass"": ""2.5""
- 布尔: ""true"" 或 ""false""
- 向量: ""0, 1, 0""
- 颜色: ""#FF0000"" 或 ""1, 0, 0, 1""
- 枚举: 直接写枚举名，如 ""interpolation"": ""Interpolate""
- 资源路径: ""Assets/Materials/xxx.mat""

不要输出额外的说明文字，只输出 JSON。

{context.ToPromptContext()}";
        }

        /// <summary>
        /// 构建预制体生成的用户 Prompt
        /// </summary>
        public static string BuildPrefabUserPrompt(string userRequest)
        {
            return $@"请根据以下需求生成 Unity 预制体定义（JSON 格式）：

{userRequest}

请直接输出 JSON（用 ```json 包裹），不要输出其他内容。";
        }

        #endregion

        #region 联合生成 Prompt

        /// <summary>
        /// 构建联合生成中「预制体阶段」的用户 Prompt。
        /// 在代码已生成后，要求 AI 生成使用该脚本的预制体。
        /// </summary>
        public static string BuildCombinedPrefabUserPrompt(string userRequest, string scriptName)
        {
            return $@"请根据以下需求生成 Unity 预制体定义（JSON 格式）：

{userRequest}

重要：预制体必须挂载刚才生成的脚本组件 ""{scriptName}""，将它添加到 components 列表中。
格式: {{""type"": ""{scriptName}"", ""properties"": {{}}}}

请直接输出 JSON（用 ```json 包裹），不要输出其他内容。";
        }

        #endregion
    }
}
