#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
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
        AssetOps = 6,
        /// <summary>调用图片 AI 生成贴图/图标，保存到 Assets</summary>
        TextureGenerate = 7,
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
## 结构示例一（3D 立方体）
用户：做一个静止的立方体道具
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

## 结构示例二（UI：Canvas + 对话框面板 + 两个左右排列的按钮）
用户：做一个带「确认」和「取消」两个按钮的 UI
关键布局规则（必须遵守）：
- Canvas 根：anchorMin [0,0] anchorMax [1,1] sizeDelta [0,0]（全屏）
- 面板/背景：anchorMin [0.5,0.5] anchorMax [0.5,0.5]（居中固定），sizeDelta 给出具体宽高
- 多个同级按钮水平排列：用 anchoredPosition 的 x 分量拉开间距，间距 ≥ (按钮宽度 + 40px)
- 每个 UI 子对象务必设置 anchorMin / anchorMax / anchoredPosition / sizeDelta；**禁止**全部留 [0,0,0]

```json
{
  ""prefabName"": ""TwoButtonUI"",
  ""rootObject"": {
    ""name"": ""Canvas"",
    ""tag"": ""Untagged"",
    ""active"": true,
    ""position"": [0, 0, 0],
    ""rotation"": [0, 0, 0],
    ""scale"": [1, 1, 1],
    ""anchoredPosition"": [0, 0],
    ""sizeDelta"": [0, 0],
    ""anchorMin"": [0, 0],
    ""anchorMax"": [1, 1],
    ""components"": [
      { ""type"": ""Canvas"",          ""properties"": { ""renderMode"": ""ScreenSpaceOverlay"" } },
      { ""type"": ""CanvasScaler"",     ""properties"": { ""uiScaleMode"": ""ScaleWithScreenSize"", ""referenceResolution"": ""1920, 1080"" } },
      { ""type"": ""GraphicRaycaster"", ""properties"": {} }
    ],
    ""children"": [
      {
        ""name"": ""DialogPanel"",
        ""active"": true,
        ""position"": [0, 0, 0],
        ""rotation"": [0, 0, 0],
        ""scale"": [1, 1, 1],
        ""anchoredPosition"": [0, 0],
        ""sizeDelta"": [800, 420],
        ""anchorMin"": [0.5, 0.5],
        ""anchorMax"": [0.5, 0.5],
        ""pivot"": [0.5, 0.5],
        ""components"": [
          { ""type"": ""Image"", ""properties"": { ""color"": ""#2C2C2CDD"" } }
        ],
        ""children"": [
          {
            ""name"": ""TitleText"",
            ""active"": true,
            ""position"": [0, 0, 0],
            ""rotation"": [0, 0, 0],
            ""scale"": [1, 1, 1],
            ""anchoredPosition"": [0, 140],
            ""sizeDelta"": [680, 60],
            ""anchorMin"": [0.5, 0.5],
            ""anchorMax"": [0.5, 0.5],
            ""pivot"": [0.5, 0.5],
            ""components"": [
              { ""type"": ""TextMeshProUGUI"", ""properties"": { ""text"": ""请确认操作"", ""fontSize"": ""36"", ""alignment"": ""Center"", ""color"": ""#FFFFFFFF"" } }
            ],
            ""children"": []
          },
          {
            ""name"": ""ConfirmButton"",
            ""active"": true,
            ""position"": [0, 0, 0],
            ""rotation"": [0, 0, 0],
            ""scale"": [1, 1, 1],
            ""anchoredPosition"": [-200, -140],
            ""sizeDelta"": [280, 80],
            ""anchorMin"": [0.5, 0.5],
            ""anchorMax"": [0.5, 0.5],
            ""pivot"": [0.5, 0.5],
            ""components"": [
              { ""type"": ""Image"",  ""properties"": { ""color"": ""#3A7FCAFF"" } },
              { ""type"": ""Button"", ""properties"": {} }
            ],
            ""children"": [
              {
                ""name"": ""Label"",
                ""active"": true,
                ""position"": [0, 0, 0],
                ""rotation"": [0, 0, 0],
                ""scale"": [1, 1, 1],
                ""anchoredPosition"": [0, 0],
                ""sizeDelta"": [0, 0],
                ""anchorMin"": [0, 0],
                ""anchorMax"": [1, 1],
                ""pivot"": [0.5, 0.5],
                ""components"": [
                  { ""type"": ""TextMeshProUGUI"", ""properties"": { ""text"": ""确认"", ""fontSize"": ""28"", ""alignment"": ""Center"", ""color"": ""#FFFFFFFF"" } }
                ],
                ""children"": []
              }
            ]
          },
          {
            ""name"": ""CancelButton"",
            ""active"": true,
            ""position"": [0, 0, 0],
            ""rotation"": [0, 0, 0],
            ""scale"": [1, 1, 1],
            ""anchoredPosition"": [200, -140],
            ""sizeDelta"": [280, 80],
            ""anchorMin"": [0.5, 0.5],
            ""anchorMax"": [0.5, 0.5],
            ""pivot"": [0.5, 0.5],
            ""components"": [
              { ""type"": ""Image"",  ""properties"": { ""color"": ""#C0392BFF"" } },
              { ""type"": ""Button"", ""properties"": {} }
            ],
            ""children"": [
              {
                ""name"": ""Label"",
                ""active"": true,
                ""position"": [0, 0, 0],
                ""rotation"": [0, 0, 0],
                ""scale"": [1, 1, 1],
                ""anchoredPosition"": [0, 0],
                ""sizeDelta"": [0, 0],
                ""anchorMin"": [0, 0],
                ""anchorMax"": [1, 1],
                ""pivot"": [0.5, 0.5],
                ""components"": [
                  { ""type"": ""TextMeshProUGUI"", ""properties"": { ""text"": ""取消"", ""fontSize"": ""28"", ""alignment"": ""Center"", ""color"": ""#FFFFFFFF"" } }
                ],
                ""children"": []
              }
            ]
          }
        ]
      }
    ]
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

用户：删掉某些 .cs 脚本 / 按类名删除脚本文件 → assetDelete（**不是**写代码清空类；须删 Project 里的脚本资源）：
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

用户：帮我生成一张草地贴图 / 生成角色头像图标 / 生成一张天空背景图 / 画一个 UI 图标 → generateTexture（调用图片 AI 生成贴图并保存到 Assets）：
```json
{
  ""generationTarget"": ""generateTexture"",
  ""imagePrompt"": ""seamless green grass texture, top-down view, game asset, tileable, 1024x1024"",
  ""saveFileName"": ""grass_texture"",
  ""codeType"": ""auto""
}
```
说明：imagePrompt 须为**英文描述**，描述清晰的风格/视角/用途；saveFileName 为文件名（不含扩展名）。

用户：把场景里 / Hierarchy 里某个物体涂成红色、改成红色、改 Image 颜色、换红色材质（针对**当前场景中的实例**）→ sceneOps（用 unity-ops 的 `setComponentProperty` 或 `instantiatePrefab` 后再改；**不要**只输出预制体 JSON 而不给层级路径）：
```json
{
  ""generationTarget"": ""sceneOps"",
  ""codeType"": ""auto""
}
```

用户：生成一个**新的**红色立方体 / 红色 UI 预制体资源（未强调「在当前场景里摆」）→ prefab（在 JSON 里用 Image 的 color，或 MeshRenderer 的 sharedMaterial 指向工程内红色 .mat）：
```json
{
  ""generationTarget"": ""prefab"",
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

## unity-ops 示例 4：从零在场景中搭建完整 UI（Canvas + 背景面板 + 按钮）
用户：在当前场景里创建一个登录界面，有确认和取消两个按钮
**关键规则**：
- Button 前必须先 addComponent Image（提供背景）；同一对象上先 Image 后 Button
- 设置颜色用 setComponentProperty + m_Color；设置布局用 setRectTransform
- Canvas 必须同时加 CanvasScaler 和 GraphicRaycaster
应输出类似：
```json
{
  ""unityOpsVersion"": 1,
  ""operations"": [
    { ""op"": ""createEmpty"", ""name"": ""LoginCanvas"" },
    { ""op"": ""addComponent"", ""path"": ""LoginCanvas"", ""typeName"": ""Canvas"" },
    { ""op"": ""addComponent"", ""path"": ""LoginCanvas"", ""typeName"": ""CanvasScaler"" },
    { ""op"": ""addComponent"", ""path"": ""LoginCanvas"", ""typeName"": ""GraphicRaycaster"" },
    { ""op"": ""createEmpty"", ""name"": ""Panel"", ""parentPath"": ""LoginCanvas"" },
    { ""op"": ""addComponent"", ""path"": ""LoginCanvas/Panel"", ""typeName"": ""Image"" },
    { ""op"": ""setComponentProperty"", ""path"": ""LoginCanvas/Panel"", ""typeName"": ""Image"", ""serializedPropertyPath"": ""m_Color"", ""propertyValue"": ""#1E1E1ECC"" },
    { ""op"": ""setRectTransform"", ""path"": ""LoginCanvas/Panel"", ""anchorMin"": ""0.5,0.5"", ""anchorMax"": ""0.5,0.5"", ""anchoredPosition"": ""0,0"", ""sizeDelta"": ""600,360"" },
    { ""op"": ""createEmpty"", ""name"": ""BtnConfirm"", ""parentPath"": ""LoginCanvas/Panel"" },
    { ""op"": ""addComponent"", ""path"": ""LoginCanvas/Panel/BtnConfirm"", ""typeName"": ""Image"" },
    { ""op"": ""setComponentProperty"", ""path"": ""LoginCanvas/Panel/BtnConfirm"", ""typeName"": ""Image"", ""serializedPropertyPath"": ""m_Color"", ""propertyValue"": ""#3A7FCAFF"" },
    { ""op"": ""addComponent"", ""path"": ""LoginCanvas/Panel/BtnConfirm"", ""typeName"": ""Button"" },
    { ""op"": ""setRectTransform"", ""path"": ""LoginCanvas/Panel/BtnConfirm"", ""anchorMin"": ""0.5,0.5"", ""anchorMax"": ""0.5,0.5"", ""anchoredPosition"": ""-150,-100"", ""sizeDelta"": ""200,60"" },
    { ""op"": ""createEmpty"", ""name"": ""Label"", ""parentPath"": ""LoginCanvas/Panel/BtnConfirm"" },
    { ""op"": ""addComponent"", ""path"": ""LoginCanvas/Panel/BtnConfirm/Label"", ""typeName"": ""TextMeshProUGUI"" },
    { ""op"": ""setUiText"", ""path"": ""LoginCanvas/Panel/BtnConfirm/Label"", ""uiText"": ""确认"" },
    { ""op"": ""setRectTransform"", ""path"": ""LoginCanvas/Panel/BtnConfirm/Label"", ""anchorMin"": ""0,0"", ""anchorMax"": ""1,1"", ""anchoredPosition"": ""0,0"", ""sizeDelta"": ""0,0"" },
    { ""op"": ""createEmpty"", ""name"": ""BtnCancel"", ""parentPath"": ""LoginCanvas/Panel"" },
    { ""op"": ""addComponent"", ""path"": ""LoginCanvas/Panel/BtnCancel"", ""typeName"": ""Image"" },
    { ""op"": ""setComponentProperty"", ""path"": ""LoginCanvas/Panel/BtnCancel"", ""typeName"": ""Image"", ""serializedPropertyPath"": ""m_Color"", ""propertyValue"": ""#C0392BFF"" },
    { ""op"": ""addComponent"", ""path"": ""LoginCanvas/Panel/BtnCancel"", ""typeName"": ""Button"" },
    { ""op"": ""setRectTransform"", ""path"": ""LoginCanvas/Panel/BtnCancel"", ""anchorMin"": ""0.5,0.5"", ""anchorMax"": ""0.5,0.5"", ""anchoredPosition"": ""150,-100"", ""sizeDelta"": ""200,60"" },
    { ""op"": ""createEmpty"", ""name"": ""Label"", ""parentPath"": ""LoginCanvas/Panel/BtnCancel"" },
    { ""op"": ""addComponent"", ""path"": ""LoginCanvas/Panel/BtnCancel/Label"", ""typeName"": ""TextMeshProUGUI"" },
    { ""op"": ""setUiText"", ""path"": ""LoginCanvas/Panel/BtnCancel/Label"", ""uiText"": ""取消"" },
    { ""op"": ""setRectTransform"", ""path"": ""LoginCanvas/Panel/BtnCancel/Label"", ""anchorMin"": ""0,0"", ""anchorMax"": ""1,1"", ""anchoredPosition"": ""0,0"", ""sizeDelta"": ""0,0"" }
  ]
}
```

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

## unity-ops 示例 6b：颜色与材质（涂红、改 UI 色、3D 换材质）
说明：**MeshRenderer 没有直接的「颜色」字段**；3D 物体通常要（1）把 **Image** 等组件的 **m_Color** 设为红色，或（2）把 **MeshRenderer** 的 **首个材质槽**换成工程里已有的红色材质（**propertyValue** 为 **Assets/.../xxx.mat**）。颜色字符串可用 **#FF0000** 或 **1,0,0,1**（RGBA，0~1）。

**UI Image 改红色**（路径按场景层级）：
```json
{
  ""unityOpsVersion"": 1,
  ""operations"": [
    { ""op"": ""setComponentProperty"", ""path"": ""Canvas/Panel/Icon"", ""typeName"": ""Image"", ""serializedPropertyPath"": ""m_Color"", ""propertyValue"": ""#FF0000"" }
  ]
}
```

**3D 物体换红色材质**（须使用「工程中的材质」一节或用户给出的真实 .mat 路径）：
```json
{
  ""unityOpsVersion"": 1,
  ""operations"": [
    { ""op"": ""setComponentProperty"", ""path"": ""Props/Crate"", ""typeName"": ""MeshRenderer"", ""serializedPropertyPath"": ""m_Materials.Array.data[0]"", ""propertyValue"": ""Assets/Materials/Red.mat"" }
  ]
}
```

## unity-ops 示例 7：仅保存当前场景（须已为 Assets 下 .unity）
用户：帮我把当前场景存一下 / 保存场景
应输出类似（不要写 C#）：
```json
{
  ""unityOpsVersion"": 1,
  ""operations"": [
    { ""op"": ""saveScene"" }
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
  ""generationTarget"": ""code"" | ""prefab"" | ""both"" | ""sceneOps"" | ""projectQuery"" | ""assetDelete"" | ""assetOps"" | ""generateTexture"",
  ""codeType"": ""auto"" | ""monobehaviour"" | ""scriptableobject"" | ""manager"",
  ""combinedOrder"": ""prefabFirst"" | ""codeFirst"",
  ""imagePrompt"": ""（仅 generateTexture 时填写，英文图片描述）"",
  ""saveFileName"": ""（仅 generateTexture 时填写，不含扩展名）""
}}

combinedOrder 仅当 generationTarget 为 ""both"" 时填写；可省略，省略等价于 ""codeFirst""（先代码后预制体）。若用户明确**先预制体再挂脚本 / 先 UI 再脚本**，必须填 ""prefabFirst""（避免先编译脚本导致域重载丢失会话）。

判断规则（generationTarget）：
- ""code""：用户主要需要 C# 脚本、类、逻辑、算法、配置数据类型（ScriptableObject）说明但仍在代码层面；或明确只要脚本不要预制体。
- ""prefab""：用户主要描述**生成预制体资源（Prefab 资产）**、可被保存到 Project 的物体模板；或明确要「做成 prefab 文件」。**含 UI / 界面 / Canvas / 按钮 / 面板 / 菜单 等描述时，若未明确说「在当前场景 / Hierarchy 里搭建」「不要 prefab」，一律优先 prefab**（常见需求是生成可复用的 UI 预制体，而不是当场改打开的场景）。
- ""sceneOps""：用户**明确**要在**当前正在编辑的场景**里直接操作：创建空物体、改父节点、挂组件、改 Transform、实例化 prefab、**保存当前场景到磁盘**、删除/复制物体、**改 Hierarchy 里物体的颜色 / 材质 / UI 色（涂红、换红材质等）**等；须体现「当前场景」「Hierarchy」「保存场景」「在场景里…」等；仅有「做一个 UI」预制体而无场景语境时**不要**选 sceneOps。
- ""both""：用户同时需要「新脚本逻辑」和「可被实例化的预制体」。若用户说**先预制体再脚本**、**先做 UI 再写脚本**、**先搭界面再加逻辑**，combinedOrder 须为 ""prefabFirst""。若未说明顺序，默认 ""codeFirst""（先代码后预制体）。
- ""projectQuery""：用户**只**想**了解 / 盘点 / 检查**当前工程已有内容（如：**有哪些预制体**、脚本大致数量、已装包列表等），**不要**生成新脚本、新预制体或改场景。**「检查一下项目」「看看有哪些 prefab」「列出 Assets 里的预制体」「工程里有多少个预制体」** 等均选此项。
- ""assetDelete""：用户要从 **Project 窗口 / Assets 里删除资源文件**（.prefab、**.cs**、.mat、贴图等），**不是**写脚本去「清空」类。**「删掉某个预制体/材质/脚本」「移除 Assets 下的文件」** 选此项。若删的是 **Hierarchy 里的实例**，选 ""sceneOps"" 的 destroy，不要选 assetDelete。
- ""assetOps""：用户要在 **Project / Assets** 里**移动、重命名、复制资源**或**新建文件夹**、批量整理路径，**不是**写 C#、**不是**改 Hierarchy 场景物体（那是 sceneOps）。
- ""generateTexture""：用户要**生成图片 / 贴图 / 纹理 / 图标 / 头像 / 背景图**等图像资源，调用图片 AI（DALL-E / Stable Diffusion 等）生成并保存到 Assets。**必须同时输出** `imagePrompt`（英文图片描述）和 `saveFileName`（文件名，不含扩展名）。

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
        /// 「删除 Project 资源」：AI 只输出结构化「删除意图」，**具体 Assets 路径由插件**根据工程扫描解析；禁止由模型编造路径。
        /// </summary>
        public static string BuildAssetDeleteSystemPrompt(ProjectContext context)
        {
            return $@"你是 Unity 编辑器插件中的「删除 Project 资源」助手。用户希望从 **Project（Assets）** 中删除某些资源。
你的任务是**理解自然语言意图**，输出**结构化 JSON**。**不要**在 JSON 里填写最终磁盘路径（尤其是 .cs）；插件会根据下方工程数据与 nameHint/pathHint **唯一解析**路径并让用户确认后再删。

你必须**只输出一个 JSON 对象**，并用 ```json 代码块包裹。**禁止**输出 C# 代码、**禁止**写 Editor 脚本或 Runtime 脚本。

{LocalModelDiscipline}

## 输出格式（字段名必须一致，version 固定为 1）
**仅输出用户实际需要删除的条目**，不要照抄示例填满 targets。示例（删一个脚本）：
```json
{{
  ""assetDeleteIntent"": {{
    ""version"": 1,
    ""targets"": [
      {{ ""kind"": ""script"", ""nameHint"": ""ObjectColorChanger"", ""pathHint"": """" }}
    ],
    ""note"": ""可选：简短说明""
  }}
}}
```
**kind** 取值：`script` | `prefab` | `material` | `scene` | `texture2d` | `asset_path` | `unknown`（每项填 **nameHint** 与/或 **pathHint**，无则空字符串）。

### kind 说明
- **script**：删 .cs 脚本时必填 **nameHint**（与类名/文件名一致，无 .cs 后缀）。路径由插件解析，你**不要**编造 Assets/ 路径。
- **prefab / material / scene / texture2d**：尽量给 **nameHint**（资源文件名，可带扩展名）或 **pathHint**（完整 Assets/ 路径或文件名片段）。
- **asset_path**：仅当用户明确给出完整 **Assets/...** 且你确信存在时使用 **pathHint**。
- **unknown**：无法分类时，把用户提到的标识填进 **nameHint** 或 **pathHint**，由插件尝试匹配。

### 规则
- **targets 至少 1 条**（除非用户明确取消删除，此时仍输出说明性 note，targets 可为解释性一条 unknown）。
- 若用户要删的是**场景 Hierarchy 里的物体**而非 Project 资源，在 **note** 中说明应使用场景操控（destroy），**targets 可为空**（插件会提示无法解析）。
- 结合下方「脚本文件路径」「预制体」等真实列表理解用户指的是哪几个资源；用 **nameHint** 表达「要删谁」，**不要**在 JSON 里写「列表里没有 .cs」类借口——路径解析是插件职责。

{context.ToPromptContextScriptPathsForDelete()}

{context.ToPromptContextAssetDeleteBrief()}

### 兼容旧版（不推荐）
若无法按上述格式输出，可退化为旧格式 `{{ ""assetPaths"": [...], ""note"": """" }}`，路径须为真实 **Assets/** 开头且存在于工程；否则请优先使用 **assetDeleteIntent**。";
        }

        /// <param name="selectionSnapshot">
        /// 由调用方在提交时捕获的编辑器选中路径快照（<c>AIQuickCommand.SnapshotEditorSelection()</c> 的结果）。
        /// 传 <c>null</c> 或空列表时会退回到实时读 <c>Selection.objects</c>（兼容旧调用路径）。
        /// </param>
        public static string BuildAssetDeleteUserPrompt(string userRequest, List<string>? selectionSnapshot = null)
        {
            return $@"用户需求（原文）：
{userRequest}

{BuildAssetDeleteEditorHint(selectionSnapshot)}

请只输出 ```json 代码块，使用 **assetDeleteIntent**（version=1）表达删除意图；由插件解析路径。不要输出解释性正文。";
        }

        /// <summary>
        /// 注入编辑器选中的资源路径，供 AI 填入 pathHint。
        /// 优先使用调用方在用户提交时捕获的快照（避免弹窗/异步导致 Selection 被清空）；
        /// 若快照为空则回退到实时读 Selection.objects 与 Hierarchy 预制体实例。
        /// </summary>
        private static string BuildAssetDeleteEditorHint(List<string>? snapshot = null)
        {
            // 使用快照；若快照为空则实时读（兜底）
            var selectedPaths = snapshot != null && snapshot.Count > 0
                ? snapshot
                : CollectEditorSelectionFallback();

            if (selectedPaths.Count == 0)
                return "## 编辑器选中状态（Project 窗口 / Hierarchy）\n当前**未选中**任何 Project 资源。若用户说「删除选中的/当前的资源」，targets 里请填写用户提到的名称到 nameHint，不要留空。";

            var lines = string.Join("\n", selectedPaths.Select(p => $"  - `{p}`"));
            return $@"## 编辑器选中状态（Project 窗口 / Hierarchy）
当前**已选中** {selectedPaths.Count} 个 Project 资源（这就是「当前选中」「选中的」「这个」所指的资源）：
{lines}

**规则**：若用户说「删除选中的/当前的/这个资源/预制体/脚本」，请把上述路径逐一填入 target 的 **pathHint** 字段（kind 按文件类型选 prefab/script/material 等，或用 asset_path）。**不要**把 nameHint 和 pathHint 都留空然后在 note 里说「依赖选中项」。";
        }

        /// <summary>
        /// 实时读编辑器选中状态的兜底方法（当快照为空时使用）。
        /// 同时处理 Project 窗口资源 和 Hierarchy 预制体实例。
        /// </summary>
        private static List<string> CollectEditorSelectionFallback()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in Selection.objects)
            {
                if (obj == null) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal))
                    result.Add(path);
            }
            foreach (var go in Selection.gameObjects)
            {
                if (go == null) continue;
                var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (!string.IsNullOrEmpty(prefabPath) && prefabPath.StartsWith("Assets/", StringComparison.Ordinal))
                    result.Add(prefabPath);
            }
            return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
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

        /// <summary>
        /// 将拖入附件列表拼接为追加在用户 Prompt 末尾的上下文段落。
        /// 返回空字符串表示没有附件，无需追加。
        /// </summary>
        public static string BuildDroppedAssetsContext(IReadOnlyList<string>? droppedAssets)
        {
            if (droppedAssets == null || droppedAssets.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("── 用户附带的资源文件（已拖入聊天窗口）──");
            foreach (var path in droppedAssets)
            {
                var ext  = System.IO.Path.GetExtension(path).ToLowerInvariant();
                var kind = ext switch
                {
                    ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" or ".exr" or ".hdr" or ".psd" => "图片/贴图",
                    ".prefab"  => "Prefab 预制体",
                    ".mat"     => "材质",
                    ".cs"      => "C# 脚本",
                    ".fbx" or ".obj" or ".dae" => "3D 模型",
                    ".mp3" or ".wav" or ".ogg" or ".aiff" or ".aif" or ".flac" => "音频",
                    ".mp4" or ".mov" or ".avi" or ".webm" or ".asf" or ".mpg" or ".mpeg" => "视频",
                    ".anim"    => "动画",
                    ".unity"   => "场景文件",
                    _          => "资源文件"
                };
                sb.AppendLine($"  • [{kind}] {path}");
            }
            sb.AppendLine("──────────────────────────────────────");
            sb.AppendLine("请结合上述附件路径理解用户需求（如需移动/引用/设置图片，路径已知）。");
            return sb.ToString();
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
| saveScene | 保存当前活动场景到磁盘 | 无额外字段（场景须已保存为 Assets 下 .unity，未命名场景无法保存） |
| setComponentProperty | 改序列化字段 | path；typeName（组件）；serializedPropertyPath；propertyValue（按类型解析；**Color** 支持 **#RRGGBB** 或 **r,g,b,a**；**材质引用** 填 **Assets/.../xxx.mat**） |
| setRectTransform | UI 布局 | path；anchorMin/anchorMax/anchoredPosition/sizeDelta/pivot/offsetMin/offsetMax 至少一项，""x,y"" |
| setUiText | UI/TMP 文字 | path；uiText（目标物体上 Unity UI Text 或 TMP_Text） |

## 路径规则（与插件解析一致）
- **层级路径 path**：从**活动场景根**下第一级子物体名开始，用英文斜杠拼接，如 Canvas/Panel/BtnOk；每一级取**同名第一个**子物体。
- **parentPath / newParentPath**：同上规则；留空或省略表示**场景根**下创建/实例化。
- **__selection__**：仅当用户**明确说了**「挂到当前选中的物体」「在 Hierarchy 选中的下面」「用选中物体作父」等时才可用；且执行时用户必须在 Hierarchy 里已选中父物体。**用户说「在这个 UI 上」「给界面加按钮」而未提选中时，一律写明确路径（如 Canvas/Panel），禁止 __selection__**（否则极易因未选中而执行失败）。
- **prefabAssetPath**：必须以 Assets/ 开头，以 .prefab 结尾，禁止 "".."" 段。

## 注意
- 不要输出 C# 或预制体 JSON（prefabName/rootObject 那套）；本任务**只输出 unity-ops**。
- 用户只要「保存当前场景」时，**只输出**含 `{{ ""op"": ""saveScene"" }}` 的 operations 即可；**禁止**用 C# 或 Editor 脚本代替 JSON。
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

{BuildSceneHierarchyDump()}

{BuildSceneOpsSelectionHint()}

## 用户需求（原文）
{userRequest}

请只输出一个 ```json 代码块（unity-ops），不要添加解释。";

            if (!appendProjectBrief)
                return body;

            var ctx = projectContext ?? ProjectContext.Collect();
            return body + "\n\n" + ctx.ToPromptContextSceneOpsBrief();
        }

        /// <summary>
        /// 把当前活动场景的 Hierarchy 树（最多 maxDepth 层、maxNodes 个节点）转为可读文本，
        /// 附带每个节点的**完整层级路径**，供 AI 直接引用，不再猜测。
        /// </summary>
        private static string BuildSceneHierarchyDump(int maxDepth = 6, int maxNodes = 120)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return "## 当前场景 Hierarchy\n（场景无效，无法获取 Hierarchy）";

            var sb = new StringBuilder();
            sb.AppendLine("## 当前场景 Hierarchy（真实路径列表，op 的 path/parentPath 必须从此选取或拼接）");
            sb.AppendLine("格式：`缩进树形` → 每行括号内为**可直接填入 path 字段的完整层级路径**");

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (count >= maxNodes) break;
                DumpGameObject(sb, root.transform, "", root.name, maxDepth, maxNodes, ref count);
            }

            if (count >= maxNodes)
                sb.AppendLine($"  …（仅展示前 {maxNodes} 个节点，场景可能有更多物体）");

            sb.AppendLine();
            sb.AppendLine("**规则：** `path` 字段必须与上表括号内路径完全一致（大小写、空格）；不要拼造表中不存在的路径。");
            return sb.ToString().TrimEnd();
        }

        private static void DumpGameObject(
            StringBuilder sb,
            Transform t,
            string indent,
            string fullPath,
            int maxDepth,
            int maxNodes,
            ref int count)
        {
            if (count >= maxNodes) return;
            count++;
            sb.AppendLine($"{indent}- {t.name}  (`{fullPath}`)");

            if (maxDepth <= 1) return;
            for (var i = 0; i < t.childCount; i++)
            {
                if (count >= maxNodes) break;
                var child = t.GetChild(i);
                DumpGameObject(sb, child, indent + "  ", fullPath + "/" + child.name, maxDepth - 1, maxNodes, ref count);
            }
        }

        /// <summary>
        /// 仅告知选中状态（已从 Hierarchy 树中知道路径，此处只做选中/未选中提示）。
        /// </summary>
        private static string BuildSceneOpsSelectionHint()
        {
            var scene = SceneManager.GetActiveScene();
            var sel = Selection.activeGameObject;
            if (sel != null && scene.IsValid())
            {
                var path = HierarchyLocator.GetHierarchyPath(scene, sel);
                if (!string.IsNullOrEmpty(path))
                {
                    return "## 编辑器状态（当前选中）\n" +
                           $"当前**已选中**物体，层级路径：`{path}`\n" +
                           "可直接把该路径用作 parentPath；仅在用户**原文明确要求**时才用 `__selection__`。";
                }
            }

            return "## 编辑器状态（当前选中）\n" +
                   "当前**未选中**任何 GameObject，**禁止**在 parentPath / newParentPath 使用 `__selection__`。";
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

## UI 预制体规则（重要）
如果用户要求创建 UI 元素，**严格按示例二的结构输出**：
1. **根对象**（Canvas）必须挂 Canvas + CanvasScaler + GraphicRaycaster；Canvas 的 renderMode 设 `ScreenSpaceOverlay`；CanvasScaler 的 uiScaleMode 设 `ScaleWithScreenSize`，referenceResolution `1920, 1080`
2. **背景面板（强制）**：Canvas 的**第一个子对象必须是背景面板**（名称如 `Panel` / `DialogPanel` / `Background`），挂 `Image` 组件并设置合适颜色（如半透明深色 `#1E1E1ECC` 或浅色 `#FFFFFFEE`）。面板居中（`anchorMin:[0.5,0.5]` `anchorMax:[0.5,0.5]`），`sizeDelta` 根据内容定（建议宽 600~900，高 300~600）。**所有按钮、文字等控件都作为面板的子对象**，而不是 Canvas 的直接子对象（除非是全屏背景图）。
3. **坐标协议**：所有 UI 对象（Canvas 根节点除外）使用顶层字段 **`anchoredPosition`**（像素，相对锚点）和 **`sizeDelta`**（像素宽高）；同时也保留 `position: [0,0,0]`（插件优先读 anchoredPosition）
4. **锚点**：Canvas 根 `anchorMin:[0,0]` `anchorMax:[1,1]`；Panel / Button / Image 等固定大小控件用 `anchorMin:[0.5,0.5]` `anchorMax:[0.5,0.5]`；全屏背景用 `anchorMin:[0,0]` `anchorMax:[1,1]` + `sizeDelta:[0,0]`；`pivot:[0.5,0.5]`
5. **Button**：每个 Button 必须有子对象挂 **TextMeshProUGUI** 显示按钮文字（`anchorMin:[0,0]` `anchorMax:[1,1]` `sizeDelta:[0,0]`）
6. **标题文字**：面板内顶部放一个 TextMeshProUGUI 作为标题，`anchoredPosition` y 值 ≈ 面板高/2 - 60
7. **InputField**：子对象需 Placeholder 和 Text（均挂 TextMeshProUGUI）
8. **不要**为 UI 对象设置 `primitive` 字段；`scale` 始终保持 `[1,1,1]`
9. 参考坐标：1920×1080 参考分辨率，面板内坐标原点在面板中心，左下角约 (-面板宽/2, -面板高/2)

## UI 排版指南（防止控件重叠）
- **多个按钮横排**：用 anchoredPosition 的 x 分量拉开距离。2 个按钮、宽 280：左按钮 x=-200，右按钮 x=+200（间距 (200-(-200)) - 280 = 120px 空白）；3 个按钮类推。
- **多个按钮竖排**：用 anchoredPosition 的 y 分量拉开。上按钮 y=+60，下按钮 y=-60（行高 80 + 40px 间距）。
- **面板内部布局**：标题放顶部（y = 面板高/2 - 60），按钮放底部（y = -(面板高/2 - 70)）。
- **禁止**多个同级 UI 控件全用 anchoredPosition [0,0]（会叠在同一位置）。
- **最小间距规则**：相邻控件边缘距离至少 20px；`| x差值 | ≥ (宽度1 + 宽度2)/2 + 20`。

## 属性值格式
- 数字: 直接写数字，如 ""mass"": ""2.5""
- 布尔: ""true"" 或 ""false""
- 向量: ""0, 1, 0""
- 颜色: ""#FF0000"" 或 ""1, 0, 0, 1""
- 枚举: 直接写枚举名，如 ""interpolation"": ""Interpolate""
- 资源路径: ""Assets/Materials/xxx.mat""

## 颜色与 3D 外观（重要）
- **UI Image / Text / TMP** 等：在对应组件的 **properties** 里设置 **color**（或 Text 的 **color**），与上文颜色格式一致。
- **带 primitive 的立方体等**：根物体已有 **MeshRenderer**。**MeshRenderer 没有单独的 color 属性**；要显示红色请二选一：
  1. 在 **components** 里增加或覆盖 **MeshRenderer**，设置 **sharedMaterial** 为工程内**已有**的红色材质路径（见上下文「材质」列表），例如：`""sharedMaterial"": ""Assets/Materials/Red.mat""`；
  2. 若工程中没有红色材质，可改用 **both** 模式生成一小段脚本，在 **Start** 里对 **Renderer** 设置 **material.color**（或让用户先在 Project 里创建红色 .mat 再填路径）。

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
