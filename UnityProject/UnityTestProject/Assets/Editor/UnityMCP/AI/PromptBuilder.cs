#nullable enable

using UnityEditor;
using UnityEngine.SceneManagement;
using UnityMCP.Core;
using UnityMCP.Tools;

namespace UnityMCP.AI
{
    /// <summary>
    /// AI 路由判断结果：本次应执行的生成类别（与 UI 中的生成模式对应）。
    /// </summary>
    public enum GenerationRoute
    {
        Code = 0,
        Prefab = 1,
        Both = 2,
        /// <summary>当前活动场景内层级操控（unity-ops JSON）</summary>
        SceneOps = 3,
        /// <summary>仅基于项目扫描结果回答（盘点预制体、已有资源等），不生成新资源</summary>
        ProjectQuery = 4,
        /// <summary>从 Project 删除资源（输出路径 JSON，由用户确认后执行）</summary>
        AssetDelete = 5,
        /// <summary>移动/重命名/复制/建文件夹（asset-ops JSON，执行前预览）</summary>
        AssetOps = 6
    }

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

        /// <summary>预制体 JSON 的简短 Few-shot（Phase 2-B，稳住 Qwen 等本地模型格式）。</summary>
        private const string PrefabFormatFewShot = @"
## 结构示例（字段名区分大小写，与本示例一致）
用户：做一个静止的立方体道具
应输出类似：
```json
{
  ""prefabName"": ""StaticCube"",
  ""rootObject"": {
    ""name"": ""CubeRoot"",
    ""primitive"": ""Cube"",
    ""tag"": ""Untagged"",
    ""active"": true,
    ""position"": [0, 0, 0],
    ""rotation"": [0, 0, 0],
    ""scale"": [1, 1, 1],
    ""components"": [
      { ""type"": ""BoxCollider"", ""properties"": { ""isTrigger"": ""false"" } }
    ],
    ""children"": []
  }
}
```
";

        private const string CodeFormatFewShot = @"
## 输出示例（仅演示围栏与类结构，不要照抄类名）
```csharp
using UnityEngine;

namespace Game.Generated
{
    /// <summary>
    /// 示例组件
    /// </summary>
    public class ExampleSpin : MonoBehaviour
    {
        #region Fields
        [SerializeField] private float speed = 90f;
        #endregion

        #region Unity Methods
        private void Update()
        {
            transform.Rotate(0f, speed * Time.deltaTime, 0f);
        }
        #endregion
    }
}
```
";

        private const string IntentRouteFewShot = @"
## 路由输出示例
用户：做一个带按钮和文字的登录界面预制体
```json
{
  ""generationTarget"": ""prefab"",
  ""codeType"": ""auto""
}
```

用户：你好，请帮忙生成一个带按钮的UI（未提当前场景 / Hierarchy）→ 仍选 prefab（可保存的 UI 预制体资源；不是当场改场景层级）。

用户：在当前打开的场景根下建 Canvas，下面放 Panel 和 Button（或明确：在 Hierarchy 里搭一套 UI）→ sceneOps：
```json
{
  ""generationTarget"": ""sceneOps"",
  ""codeType"": ""auto""
}
```

用户：帮我检查一下项目里现在有哪些预制体 / 列出 Assets 里所有 prefab → projectQuery（只读盘点，不生成新预制体）：
```json
{
  ""generationTarget"": ""projectQuery"",
  ""codeType"": ""auto""
}
```

用户：删掉 Assets/.../Old.prefab / 从工程里移除某些预制体 → assetDelete（删 Project 资源，**不要**选 code；不是写脚本）：
```json
{
  ""generationTarget"": ""assetDelete"",
  ""codeType"": ""auto""
}
```

用户：把 Assets/A 挪到 Assets/B、批量整理文件夹、复制材质、新建 Assets 下的文件夹 → assetOps（**不是**写脚本；输出 asset-ops JSON）：
```json
{
  ""generationTarget"": ""assetOps"",
  ""codeType"": ""auto""
}
```
";

        private const string SceneOpsFormatFewShot = @"
## unity-ops 示例 1：建空物体并加碰撞体、改本地坐标
用户：在场景根下建一个空物体 Doorway，加个 BoxCollider，本地位置设为原点上方 1 米
应输出类似：
```json
{
  ""unityOpsVersion"": 1,
  ""operations"": [
    { ""op"": ""createEmpty"", ""name"": ""Doorway"", ""parentPath"": """" },
    { ""op"": ""addComponent"", ""path"": ""Doorway"", ""typeName"": ""BoxCollider"" },
    { ""op"": ""setTransform"", ""path"": ""Doorway"", ""localPosition"": ""0,1,0"" }
  ]
}
```

## unity-ops 示例 2：设父节点到选中物体
用户：把层级路径 Props/Crate 挂到当前选中的物体下面（保持世界坐标）
应输出类似：
```json
{
  ""unityOpsVersion"": 1,
  ""operations"": [
    { ""op"": ""setParent"", ""path"": ""Props/Crate"", ""newParentPath"": ""__selection__"", ""worldPositionStays"": true }
  ]
}
```

## unity-ops 示例 3：实例化预制体
用户：把 Assets/Prefabs/Generated/Enemy.prefab 实例化到 Combat/Spawns 下，缩放 1.2 倍
应输出类似：
```json
{
  ""unityOpsVersion"": 1,
  ""operations"": [
    { ""op"": ""instantiatePrefab"", ""prefabAssetPath"": ""Assets/Prefabs/Generated/Enemy.prefab"", ""parentPath"": ""Combat/Spawns"", ""localScale"": ""1.2,1.2,1.2"" }
  ]
}
```

## unity-ops 示例 4：在已有 UI 下加按钮（勿用 __selection__，须写明层级路径）
用户：在 Canvas 里的面板上加一个可点按钮，文字为「确认」
应输出类似（parentPath 用从场景根起的路径；常见为 Canvas/Panel 或 Canvas 下实际容器名）：
```json
{
  ""unityOpsVersion"": 1,
  ""operations"": [
    { ""op"": ""createEmpty"", ""name"": ""BtnConfirm"", ""parentPath"": ""Canvas/Panel"" },
    { ""op"": ""addComponent"", ""path"": ""Canvas/Panel/BtnConfirm"", ""typeName"": ""UnityEngine.UI.Button"" }
  ]
}
```
说明：UI 文案改 Text 子物体或需额外步骤；此处仅示范**父路径必须明确**，不要用 __selection__。

## unity-ops 示例 5：TMP 文案与 RectTransform 锚点
```json
{
  ""unityOpsVersion"": 1,
  ""operations"": [
    { ""op"": ""setUiText"", ""path"": ""Canvas/Panel/Title"", ""uiText"": ""设置"" },
    { ""op"": ""setRectTransform"", ""path"": ""Canvas/Panel/Title"", ""anchorMin"": ""0,1"", ""anchorMax"": ""1,1"", ""anchoredPosition"": ""0,-20"", ""sizeDelta"": ""0,40"" }
  ]
}
```

## unity-ops 示例 6：删除物体、开关显示、Layer/Tag、打开场景
```json
{
  ""unityOpsVersion"": 1,
  ""operations"": [
    { ""op"": ""setActive"", ""path"": ""HUD/Old"", ""active"": false },
    { ""op"": ""setLayer"", ""path"": ""Player"", ""layerName"": ""Default"" },
    { ""op"": ""setTag"", ""path"": ""Player"", ""gameObjectTag"": ""Player"" },
    { ""op"": ""openScene"", ""sceneAssetPath"": ""Assets/Scenes/Main.unity"", ""openSceneAdditive"": false }
  ]
}
```
";

        private const string LocalModelDiscipline = @"
## 本地模型（Ollama / Qwen / Llama 等）必读
- 只输出要求的**一个**代码块或 JSON 块，块外不要写任何解释（不要用 Markdown 标题复述需求）。
- JSON 中键名使用半角双引号；尽量**不要尾逗号**；布尔与数字在 properties 内也建议写成字符串（如 ""true""、""1.5""）以减少解析歧义。
";

        #region AI 意图路由（判断生成代码 / 预制体 / 联合）

        /// <summary>
        /// 构建「仅输出路由 JSON」的系统 Prompt。
        /// </summary>
        public static string BuildIntentRouteSystemPrompt(ProjectContext context)
        {
            return $@"你是 Unity 编辑器插件中的「意图路由」模块。根据用户一句自然语言，判断本次应执行哪一种生成任务。
只输出一个 JSON 对象，并且必须用 ```json 代码块包裹。不要输出其他任何文字。

{LocalModelDiscipline}

{IntentRouteFewShot}

JSON 格式（字段名必须一致）：
{{
  ""generationTarget"": ""code"" | ""prefab"" | ""both"" | ""sceneOps"" | ""projectQuery"" | ""assetDelete"" | ""assetOps"",
  ""codeType"": ""auto"" | ""monobehaviour"" | ""scriptableobject"" | ""manager"",
  ""combinedOrder"": ""prefabFirst"" | ""codeFirst""
}}

combinedOrder 仅当 generationTarget 为 ""both"" 时填写；可省略，省略等价于 ""codeFirst""（先代码后预制体）。若用户明确**先预制体再挂脚本 / 先 UI 再脚本**，必须填 ""prefabFirst""（避免先编译脚本导致域重载丢失会话）。

判断规则（generationTarget）：
- ""code""：用户主要需要 C# 脚本、类、逻辑、算法、配置数据类型（ScriptableObject）说明但仍在代码层面；或明确只要脚本不要预制体。
- ""prefab""：用户主要描述**生成预制体资源（Prefab 资产）**、可被保存到 Project 的物体模板；或明确要「做成 prefab 文件」。**含 UI / 界面 / Canvas / 按钮 / 面板 / 菜单 等描述时，若未明确说「在当前场景 / Hierarchy 里搭建」「不要 prefab」，一律优先 prefab**（常见需求是生成可复用的 UI 预制体，而不是当场改打开的场景）。
- ""sceneOps""：用户**明确**要在**当前正在编辑的场景**里直接改层级：创建空物体、改父节点、挂内置组件、改 Transform、把已有 .prefab **实例化进场景**；须体现「当前场景」「Hierarchy」「在场景根/某路径下建」「放到场景里」「给打开的场景加物体」等；仅有「做一个 UI」而无场景语境时**不要**选 sceneOps。
- ""both""：用户同时需要「新脚本逻辑」和「可被实例化的预制体」。若用户说**先预制体再脚本**、**先做 UI 再写脚本**、**先搭界面再加逻辑**，combinedOrder 须为 ""prefabFirst""。若未说明顺序，默认 ""codeFirst""（先代码后预制体）。
- ""projectQuery""：用户**只**想**了解 / 盘点 / 检查**当前工程已有内容（如：**有哪些预制体**、脚本大致数量、已装包列表等），**不要**生成新脚本、新预制体或改场景。**「检查一下项目」「看看有哪些 prefab」「列出 Assets 里的预制体」「工程里有多少个预制体」** 等均选此项。
- ""assetDelete""：用户要从 **Project 窗口 / Assets 里删除资源文件**（.prefab、.mat、贴图等），**不是**写脚本。**「删掉某个预制体/材质」「移除 Assets 下的文件」** 选此项。若删的是 **Hierarchy 里的实例**，选 ""sceneOps"" 的 destroy，不要选 assetDelete。
- ""assetOps""：用户要在 **Project / Assets** 里**移动、重命名、复制资源**或**新建文件夹**、批量整理路径，**不是**写 C#、**不是**改 Hierarchy 场景物体（那是 sceneOps）。

codeType（当 generationTarget 为 ""prefab"" 时也请给出，可固定为 ""auto""）：
- ""auto""：由后续代码生成步骤自动区分 MonoBehaviour / ScriptableObject / Manager
- ""monobehaviour""：明确需要挂载到物体的 MonoBehaviour
- ""scriptableobject""：明确是资源资产型配置（仅当需要脚本时与 generationTarget 为 code/both 组合）
- ""manager""：明确是持久化单例管理器

{context.ToPromptContext()}";
        }

        /// <summary>
        /// 「项目查询」模式：基于 <see cref="ProjectContext.ToPromptContext"/> 中的真实扫描数据回答用户问题。
        /// </summary>
        public static string BuildProjectQuerySystemPrompt(ProjectContext context)
        {
            return $@"你是 Unity 编辑器插件中的「项目查询」助手。下方数据来自当前工程的**真实扫描**（Assets 下 .prefab 路径、脚本类名等），请据此回答。
规则：
- 若用户要「列出 / 查看 / 盘点」预制体，请**仅使用**「工程中的预制体资源」一节列出的路径；若文中注明「共 N 个、仅列出前 200 条」，须在回答中说明总数与截断情况。
- 不要编造 Assets 下不存在的路径或预制体。
- 若用户问的是渲染管线、包、脚本命名冲突等，请结合其他章节。
- 使用**简体中文**，条理清晰；不要输出 JSON 或 C# 代码块，除非用户明确要求示例格式。

{context.ToPromptContext()}";
        }

        /// <summary>
        /// 「删除 Project 资源」：只输出待删除路径 JSON，由插件校验后由用户确认再执行删除。
        /// </summary>
        public static string BuildAssetDeleteSystemPrompt(ProjectContext context)
        {
            return $@"你是 Unity 编辑器插件中的「删除 Project 资源」助手。用户希望从 **Project（Assets）** 中删除某些**真实存在的**资源文件（可为 .prefab、.mat、.png、.unity 等）。
你必须**只输出一个 JSON 对象**，并用 ```json 代码块包裹。**禁止**输出 C# 代码、**禁止**写 Editor 脚本或 Runtime 脚本。

{LocalModelDiscipline}

## 输出格式（字段名必须一致）
```json
{{
  ""assetPaths"": [""Assets/路径/某.prefab"", ""...""],
  ""note"": ""简短说明（选填）""
}}
```

规则：
- assetPaths 中每一项必须是 **Assets/ 开头** 的合法资源路径，且应优先从下方列表中选取与用户描述匹配的项。
- 若无法确定具体路径或列表中无匹配项，将 assetPaths 设为空数组 []，并在 note 中说明原因（不要编造不存在的路径）。
- 若用户实际想删的是**场景 Hierarchy 里的物体**而不是磁盘上的资源文件，将 assetPaths 设为空数组，并在 note 中说明应使用场景操控（destroy）。

{context.ToPromptContext()}";
        }

        public static string BuildAssetDeleteUserPrompt(string userRequest)
        {
            return $@"用户需求（原文）：
{userRequest}

请只输出 JSON 代码块（```json ... ```），列出要删除的 Assets 资源路径。";
        }

        private const string AssetOpsFormatFewShot = @"
## asset-ops 示例：移动材质并新建文件夹
```json
{
  ""assetOpsVersion"": 1,
  ""operations"": [
    { ""op"": ""createFolder"", ""path"": ""Assets/Art/Materials/Archive"" },
    { ""op"": ""moveAsset"", ""path"": ""Assets/Old/Mat.mat"", ""destPath"": ""Assets/Art/Materials/Archive/Mat.mat"" }
  ]
}
```

## 示例：重命名与复制
```json
{
  ""assetOpsVersion"": 1,
  ""operations"": [
    { ""op"": ""renameAsset"", ""path"": ""Assets/Icons/app.png"", ""newName"": ""AppIcon.png"" },
    { ""op"": ""copyAsset"", ""path"": ""Assets/Prefabs/Player.prefab"", ""destPath"": ""Assets/Prefabs/Player_Backup.prefab"" }
  ]
}
```
";

        /// <summary>
        /// 「整理 Assets」：输出可执行的 asset-ops JSON。
        /// </summary>
        public static string BuildAssetOpsSystemPrompt(ProjectContext context)
        {
            return $@"你是 Unity 编辑器插件中的「资源整理」模块。根据用户自然语言，输出**可被执行的 asset-ops JSON**，用于在 Project 窗口下移动、重命名、复制资源或新建文件夹。
只输出**一个** JSON 对象，并且必须用 ```json 代码块包裹。不要输出代码块外的任何文字。

{LocalModelDiscipline}

{AssetOpsFormatFewShot}

## JSON 根对象
- ""assetOpsVersion"": 整数，必须为 {AssetOpsParser.SupportedVersion}
- ""operations"": 数组，按顺序执行；任一步失败则整批中止

## 允许的 op
| op | 说明 | 必填字段 |
|----|------|----------|
| moveAsset | 移动资源（同卷内） | path（源）；destPath（目标完整路径，含文件名） |
| renameAsset | 重命名资源 | path；newName（新文件名，须含扩展名） |
| createFolder | 创建文件夹（可多级） | path（如 Assets/Generated/UI） |
| copyAsset | 复制资源 | path（源）；destPath（目标完整路径） |

## 路径规则
- 所有路径必须以 **Assets/** 开头，禁止 **..** 段；不要臆造不存在的源路径。
- 若需求含糊，做**最小**步骤并优先使用下方列表中真实存在的路径。

{context.ToPromptContext()}";
        }

        public static string BuildAssetOpsUserPrompt(string userRequest)
        {
            return $@"用户需求（原文）：
{userRequest}

请只输出一个 ```json 代码块（asset-ops），不要添加解释。";
        }

        /// <summary>
        /// 意图路由的用户 Prompt。
        /// </summary>
        public static string BuildIntentRouteUserPrompt(string userRequest)
        {
            return $@"用户需求（原文）：
{userRequest}

请只输出 JSON 代码块（```json ... ```），不要添加解释。";
        }

        #endregion

        #region 场景操控 Prompt（unity-ops / A.3）

        /// <summary>
        /// 当前活动场景名，供 <see cref="BuildSceneOpsUserPrompt"/> 使用；无效场景时返回空字符串。
        /// </summary>
        public static string GetActiveSceneNameForPrompt()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return "";
            return string.IsNullOrEmpty(scene.name) ? scene.path : scene.name;
        }

        /// <summary>
        /// 构建「仅输出 unity-ops JSON」的系统 Prompt：允许的 op、路径约定、Few-shot。
        /// 项目上下文使用轻量摘要，避免附带完整脚本列表。
        /// </summary>
        public static string BuildSceneOpsSystemPrompt(ProjectContext context)
        {
            return $@"你是 Unity 编辑器插件中的「场景操控」模块。根据用户自然语言，输出**可被执行的 unity-ops JSON**，用于在当前活动场景中按顺序创建/调整物体。
只输出**一个** JSON 对象，并且必须用 ```json 代码块包裹。不要输出代码块外的任何文字。

{LocalModelDiscipline}

{SceneOpsFormatFewShot}

## JSON 根对象（字段名必须一致）
- ""unityOpsVersion"": 整数，必须为 {SceneOpsParser.SupportedVersion}
- ""operations"": 数组，按顺序执行；任一步失败则整批中止

## 每一步操作 operations[] 的通用字段
- ""op"": 字符串，见下表**允许取值**（大小写不敏感，可用 snake_case，如 create_empty）
- 未使用的字段可省略或留空字符串

## 允许的 op 与专用字段
| op | 说明 | 必填字段 |
|----|------|----------|
| createEmpty | 新建空 GameObject | name；parentPath 可选（空=场景根） |
| setParent | 修改父节点 | path；newParentPath（或 __selection__）；worldPositionStays 可选 |
| addComponent | 挂组件 | path；typeName |
| setTransform | 改本地 Transform | path；localPosition / localEulerAngles / localScale 至少一项，""x,y,z"" |
| instantiatePrefab | 实例化预制体 | prefabAssetPath（.prefab）；parentPath 可选；位姿字段可选 |
| destroy | 销毁场景物体 | path |
| duplicate | 复制物体 | path；duplicateNewName 可选 |
| setActive | 显隐 | path；active（bool，默认 true） |
| setLayer | Layer | path；layerIndex（0–31）或 layerName |
| setTag | Tag | path；gameObjectTag（须在工程中已定义） |
| openScene | 打开场景 | sceneAssetPath（Assets 下 .unity）；openSceneAdditive 可选 |
| setComponentProperty | 改序列化字段 | path；typeName（组件）；serializedPropertyPath；propertyValue（按类型解析） |
| setRectTransform | UI 布局 | path；anchorMin/anchorMax/anchoredPosition/sizeDelta/pivot/offsetMin/offsetMax 至少一项，""x,y"" |
| setUiText | UI/TMP 文字 | path；uiText（目标物体上 Unity UI Text 或 TMP_Text） |

## 路径规则（与插件解析一致）
- **层级路径 path**：从**活动场景根**下第一级子物体名开始，用英文斜杠拼接，如 Canvas/Panel/BtnOk；每一级取**同名第一个**子物体。
- **parentPath / newParentPath**：同上规则；留空或省略表示**场景根**下创建/实例化。
- **__selection__**：仅当用户**明确说了**「挂到当前选中的物体」「在 Hierarchy 选中的下面」「用选中物体作父」等时才可用；且执行时用户必须在 Hierarchy 里已选中父物体。**用户说「在这个 UI 上」「给界面加按钮」而未提选中时，一律写明确路径（如 Canvas/Panel），禁止 __selection__**（否则极易因未选中而执行失败）。
- **prefabAssetPath**：必须以 Assets/ 开头，以 .prefab 结尾，禁止 "".."" 段。

## 注意
- 不要输出 C# 或预制体 JSON（prefabName/rootObject 那套）；本任务**只输出 unity-ops**。
- 若需求含糊，做**最小安全**操作并少步完成；不要臆造不存在的预制体路径。
- 追加/修改 UI 时：若用户未给出路径，可假设常见层级 **Canvas** 或 **Canvas/Panel**（与场景实际命名一致）；仍**不要**用 __selection__ 代替猜测。
- 向量与欧拉角字符串使用**英文逗号**，不要额外空格也可（插件按 InvariantCulture 解析）。

{context.ToPromptContextSceneOpsBrief()}";
        }

        /// <summary>
        /// 场景操控的用户 Prompt：附带当前活动场景名；可选再附一段项目摘要（与系统 Prompt 二选一或叠加简短提醒）。
        /// </summary>
        /// <param name="userRequest">用户自然语言需求</param>
        /// <param name="activeSceneName">当前活动场景名（含 .unity 与否均可，仅作说明）</param>
        /// <param name="appendProjectBrief">为 true 时在用户消息末尾追加 <see cref="ProjectContext.ToPromptContextSceneOpsBrief"/>（若系统消息已含完整摘要可传 false）</param>
        /// <param name="projectContext">当 <paramref name="appendProjectBrief"/> 为 true 时使用；为 null 时内部 <see cref="ProjectContext.Collect"/></param>
        public static string BuildSceneOpsUserPrompt(
            string userRequest,
            string activeSceneName,
            bool appendProjectBrief = false,
            ProjectContext? projectContext = null)
        {
            var sceneLine = string.IsNullOrWhiteSpace(activeSceneName)
                ? "（未能读取活动场景名，仍请仅输出 unity-ops JSON。）"
                : activeSceneName.Trim();

            var body = $@"## 当前活动场景
{sceneLine}

{BuildSceneOpsHierarchyEditorHint()}

## 用户需求（原文）
{userRequest}

请只输出一个 ```json 代码块（unity-ops），不要添加解释。";

            if (!appendProjectBrief)
                return body;

            var ctx = projectContext ?? ProjectContext.Collect();
            return body + "\n\n" + ctx.ToPromptContextSceneOpsBrief();
        }

        /// <summary>
        /// 告诉模型当前是否选中物体；未选中时禁止产出 __selection__，减少执行失败。
        /// </summary>
        private static string BuildSceneOpsHierarchyEditorHint()
        {
            var scene = SceneManager.GetActiveScene();
            var sel = Selection.activeGameObject;
            if (sel != null && scene.IsValid())
            {
                var path = HierarchyLocator.GetHierarchyPath(scene, sel);
                if (!string.IsNullOrEmpty(path))
                {
                    return "## 编辑器状态（Hierarchy）\n" +
                           $"当前**已选中**物体，层级路径为：`{path}`\n" +
                           "若用户希望挂到「这个 UI」且与选中一致，可把 createEmpty / instantiatePrefab 的 parentPath **直接写成上述路径**（优于 __selection__，避免用户改选后路径漂移）。\n" +
                           "仅在用户**原文明确要求**用当前选中作父时，才使用 `__selection__`。";
                }

                return "## 编辑器状态（Hierarchy）\n" +
                       "当前有选中物体，但无法解析其在活动场景下的层级路径（可能不属于活动场景）。\n" +
                       "请勿使用 `__selection__`；请根据用户需求写明确路径（如 Canvas/Panel）。";
            }

            return "## 编辑器状态（Hierarchy）\n" +
                   "当前**未选中**任何 GameObject。\n" +
                   "**禁止** 在 JSON 的 parentPath / newParentPath 中使用 `__selection__`（会导致执行第一步就失败）。\n" +
                   "请根据用户需求写**从场景根起的完整路径**（如 `Canvas`、`Canvas/Panel`）。用户说「在这个 UI 上」时，用场景中已有的 UI 根/容器路径，不要写 __selection__。";
        }

        #endregion

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

{LocalModelDiscipline}

{CodeFormatFewShot}

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

{LocalModelDiscipline}

{PrefabFormatFewShot}

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

## 内置 3D 形体（重要）
- 若用户要立方体、球体、胶囊、圆柱、平面等，请在 **rootObject**（或对应子物体）上设置 **primitive** 字段，值为 Unity 的 **PrimitiveType** 名称：**Cube**、**Sphere**、**Capsule**、**Cylinder**、**Plane**、**Quad**（大小写不敏感）。
- 这样会使用引擎自带网格与碰撞体；**不要**仅用 MeshFilter+MeshRenderer 却不填 mesh（会导致 Mesh 为空）。
- 若必须用 MeshFilter，可在 properties 里设置 **mesh** 为 **builtin:Cube**（或 Sphere 等）以引用内置网格。

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

重要：
1. 预制体 **根物体** 的 components 里 **必须** 包含脚本组件，**type** 字段填 **C# 类名**（与刚生成的脚本里 **public class 后的名称完全一致**，含大小写）：""{scriptName}""
2. 格式示例: {{""type"": ""{scriptName}"", ""properties"": {{}}}}
3. 若联合流程里脚本已编译，保存预制体时会再尝试自动挂载该类；仍请你在 JSON 中写出该组件以便属性可配置。

请直接输出 JSON（用 ```json 包裹），不要输出其他内容。";
        }

        #endregion
    }
}
