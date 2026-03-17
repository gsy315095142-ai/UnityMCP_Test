#nullable enable

using System;
using System.Collections.Generic;

namespace UnityMCP.Generators
{
    /// <summary>
    /// 预制体描述根节点。
    /// AI 返回的 JSON 会被反序列化为此结构。
    /// </summary>
    [Serializable]
    public class PrefabDescription
    {
        /// <summary>预制体名称（同时作为 .prefab 文件名）</summary>
        public string prefabName = "";

        /// <summary>根 GameObject 描述</summary>
        public GameObjectDescription rootObject = new();
    }

    /// <summary>
    /// 单个 GameObject 的描述
    /// </summary>
    [Serializable]
    public class GameObjectDescription
    {
        /// <summary>GameObject 名称</summary>
        public string name = "GameObject";

        /// <summary>标签</summary>
        public string tag = "Untagged";

        /// <summary>图层（数字）</summary>
        public int layer = 0;

        /// <summary>是否激活</summary>
        public bool active = true;

        /// <summary>本地坐标</summary>
        public float[] position = { 0, 0, 0 };

        /// <summary>本地旋转（欧拉角）</summary>
        public float[] rotation = { 0, 0, 0 };

        /// <summary>本地缩放</summary>
        public float[] scale = { 1, 1, 1 };

        /// <summary>要添加的组件列表</summary>
        public List<ComponentDescription> components = new();

        /// <summary>子 GameObject 列表</summary>
        public List<GameObjectDescription> children = new();
    }

    /// <summary>
    /// 组件描述
    /// </summary>
    [Serializable]
    public class ComponentDescription
    {
        /// <summary>
        /// 组件类型名。
        /// 支持短名（如 "Rigidbody"）或完整名（如 "UnityEngine.Rigidbody"）
        /// </summary>
        public string type = "";

        /// <summary>
        /// 组件属性键值对。
        /// Key 为属性名，Value 为属性值（字符串形式，运行时解析）
        /// </summary>
        public Dictionary<string, object> properties = new();
    }
}
