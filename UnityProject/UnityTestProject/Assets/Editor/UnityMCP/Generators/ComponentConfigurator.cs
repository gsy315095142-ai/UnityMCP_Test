#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Generators
{
    /// <summary>
    /// 组件配置器。
    /// 负责将 AI 描述的组件信息映射到 Unity API，添加组件并设置属性。
    /// </summary>
    public static class ComponentConfigurator
    {
        /// <summary>
        /// 常用组件类型的短名映射（引擎内置类型，编译期可确定）
        /// </summary>
        private static readonly Dictionary<string, Type> ShortNameMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // 物理
            { "Rigidbody", typeof(Rigidbody) },
            { "BoxCollider", typeof(BoxCollider) },
            { "SphereCollider", typeof(SphereCollider) },
            { "CapsuleCollider", typeof(CapsuleCollider) },
            { "MeshCollider", typeof(MeshCollider) },
            { "CharacterController", typeof(CharacterController) },

            // 渲染
            { "MeshRenderer", typeof(MeshRenderer) },
            { "MeshFilter", typeof(MeshFilter) },
            { "SkinnedMeshRenderer", typeof(SkinnedMeshRenderer) },
            { "SpriteRenderer", typeof(SpriteRenderer) },
            { "LineRenderer", typeof(LineRenderer) },
            { "TrailRenderer", typeof(TrailRenderer) },
            { "Light", typeof(Light) },
            { "Camera", typeof(Camera) },

            // 音频
            { "AudioSource", typeof(AudioSource) },
            { "AudioListener", typeof(AudioListener) },

            // UI 基础
            { "Canvas", typeof(Canvas) },
            { "CanvasRenderer", typeof(CanvasRenderer) },
            { "RectTransform", typeof(RectTransform) },
            { "CanvasGroup", typeof(CanvasGroup) },

            // 动画
            { "Animator", typeof(Animator) },
            { "Animation", typeof(Animation) },

            // 粒子
            { "ParticleSystem", typeof(ParticleSystem) },

            // 导航
            { "NavMeshAgent", typeof(UnityEngine.AI.NavMeshAgent) },
            { "NavMeshObstacle", typeof(UnityEngine.AI.NavMeshObstacle) },
        };

        /// <summary>
        /// UGUI / TextMeshPro 组件的全限定名映射。
        /// 这些类型位于独立程序集，不能在编译期用 typeof() 引用，
        /// 通过运行时按 FullName 在已加载程序集中查找。
        /// </summary>
        private static readonly Dictionary<string, string> QualifiedNameMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // UnityEngine.UI (com.unity.ugui)
            { "Image", "UnityEngine.UI.Image" },
            { "RawImage", "UnityEngine.UI.RawImage" },
            { "Button", "UnityEngine.UI.Button" },
            { "Toggle", "UnityEngine.UI.Toggle" },
            { "Slider", "UnityEngine.UI.Slider" },
            { "Scrollbar", "UnityEngine.UI.Scrollbar" },
            { "Dropdown", "UnityEngine.UI.Dropdown" },
            { "InputField", "UnityEngine.UI.InputField" },
            { "Text", "UnityEngine.UI.Text" },
            { "ScrollRect", "UnityEngine.UI.ScrollRect" },
            { "Mask", "UnityEngine.UI.Mask" },
            { "RectMask2D", "UnityEngine.UI.RectMask2D" },
            { "CanvasScaler", "UnityEngine.UI.CanvasScaler" },
            { "GraphicRaycaster", "UnityEngine.UI.GraphicRaycaster" },
            { "Outline", "UnityEngine.UI.Outline" },
            { "Shadow", "UnityEngine.UI.Shadow" },
            { "LayoutElement", "UnityEngine.UI.LayoutElement" },
            { "HorizontalLayoutGroup", "UnityEngine.UI.HorizontalLayoutGroup" },
            { "VerticalLayoutGroup", "UnityEngine.UI.VerticalLayoutGroup" },
            { "GridLayoutGroup", "UnityEngine.UI.GridLayoutGroup" },
            { "ContentSizeFitter", "UnityEngine.UI.ContentSizeFitter" },
            { "AspectRatioFitter", "UnityEngine.UI.AspectRatioFitter" },

            // TMPro (com.unity.textmeshpro)
            { "TextMeshProUGUI", "TMPro.TextMeshProUGUI" },
            { "TextMeshPro", "TMPro.TextMeshPro" },
            { "TMP_InputField", "TMPro.TMP_InputField" },
            { "TMP_Dropdown", "TMPro.TMP_Dropdown" },
        };

        /// <summary>
        /// 类型解析缓存，避免重复扫描程序集
        /// </summary>
        private static readonly Dictionary<string, Type?> _resolveCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 按与 <see cref="AddAndConfigure"/> 相同的规则解析组件类型（供场景操控工具等复用）。
        /// </summary>
        public static Type? ResolveComponentTypeForTools(string typeName) => ResolveType(typeName);

        /// <summary>
        /// 根据组件描述向 GameObject 添加组件并设置属性
        /// </summary>
        public static ComponentResult AddAndConfigure(GameObject go, ComponentDescription desc)
        {
            var componentType = ResolveType(desc.type);
            if (componentType == null)
            {
                return ComponentResult.Fail(
                    $"无法识别组件类型: {desc.type}。请使用完整类名（如 UnityEngine.Rigidbody）或常用短名。");
            }

            try
            {
                var existing = go.GetComponent(componentType);
                var component = existing != null ? existing : go.AddComponent(componentType);

                if (component == null)
                {
                    return ComponentResult.Fail($"无法添加组件 {desc.type} 到 {go.name}");
                }

                var errors = new List<string>();
                foreach (var kvp in desc.properties)
                {
                    var error = SetProperty(component, kvp.Key, kvp.Value);
                    if (error != null)
                        errors.Add(error);
                }

                if (errors.Count > 0)
                {
                    return ComponentResult.Partial(component, errors);
                }

                return ComponentResult.Ok(component);
            }
            catch (Exception ex)
            {
                return ComponentResult.Fail($"添加组件 {desc.type} 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析类型名称为 Type 对象。
        /// 优先级: ShortNameMap → QualifiedNameMap → Type.GetType → 程序集扫描
        /// </summary>
        private static Type? ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            typeName = typeName.Trim();

            if (_resolveCache.TryGetValue(typeName, out var cached))
                return cached;

            var resolved = ResolveTypeInternal(typeName);
            _resolveCache[typeName] = resolved;
            return resolved;
        }

        private static Type? ResolveTypeInternal(string typeName)
        {
            // 1) 内置短名直接映射
            if (ShortNameMap.TryGetValue(typeName, out var mapped))
                return mapped;

            // 2) "UnityEngine.Rigidbody" 等全名 → 提取短名再试
            var lastDot = typeName.LastIndexOf('.');
            if (lastDot >= 0)
            {
                var shortName = typeName.Substring(lastDot + 1);
                if (ShortNameMap.TryGetValue(shortName, out var fromFull))
                    return fromFull;
            }

            // 3) UGUI/TMPro 全限定名映射 → 按 FullName 搜索程序集
            string? qualifiedTarget = null;
            if (QualifiedNameMap.TryGetValue(typeName, out var qn))
            {
                qualifiedTarget = qn;
            }
            else if (lastDot >= 0)
            {
                var shortName = typeName.Substring(lastDot + 1);
                if (QualifiedNameMap.TryGetValue(shortName, out var qn2))
                    qualifiedTarget = qn2;
            }

            if (qualifiedTarget != null)
            {
                var found = FindTypeByFullName(qualifiedTarget);
                if (found != null) return found;
            }

            // 4) Type.GetType 尝试（对程序集限定名有效）
            var direct = Type.GetType(typeName);
            if (direct != null && typeof(Component).IsAssignableFrom(direct))
                return direct;

            // 5) 全程序集按 Name / FullName 扫描（兜底，处理用户自定义脚本）
            return FindTypeInAllAssemblies(typeName);
        }

        private static Type? FindTypeByFullName(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName);
                    if (t != null && typeof(Component).IsAssignableFrom(t))
                        return t;
                }
                catch (ReflectionTypeLoadException) { }
            }
            return null;
        }

        private static Type? FindTypeInAllAssemblies(string typeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var found = asm.GetTypes().FirstOrDefault(t =>
                        typeof(Component).IsAssignableFrom(t) &&
                        (t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                         (t.FullName != null && t.FullName.Equals(typeName, StringComparison.OrdinalIgnoreCase))));
                    if (found != null)
                        return found;
                }
                catch (ReflectionTypeLoadException) { }
            }
            return null;
        }

        /// <summary>
        /// 设置组件的单个属性
        /// </summary>
        private static string? SetProperty(Component component, string propertyName, object value)
        {
            var type = component.GetType();

            var prop = type.GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null && prop.CanWrite)
            {
                try
                {
                    var converted = ConvertValue(value, prop.PropertyType);
                    prop.SetValue(component, converted);
                    return null;
                }
                catch (Exception ex)
                {
                    return $"{component.GetType().Name}.{propertyName} 设置失败: {ex.Message}";
                }
            }

            var field = type.GetField(propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null)
            {
                try
                {
                    var converted = ConvertValue(value, field.FieldType);
                    field.SetValue(component, converted);
                    return null;
                }
                catch (Exception ex)
                {
                    return $"{component.GetType().Name}.{propertyName} 设置失败: {ex.Message}";
                }
            }

            return $"{component.GetType().Name} 上找不到属性 {propertyName}";
        }

        /// <summary>
        /// 将 JSON 值转换为目标类型
        /// </summary>
        private static object? ConvertValue(object value, Type targetType)
        {
            if (value == null)
                return null;

            var strVal = value.ToString() ?? "";

            if (targetType == typeof(bool))
                return bool.Parse(strVal);

            if (targetType == typeof(int))
                return int.Parse(strVal, CultureInfo.InvariantCulture);

            if (targetType == typeof(float))
                return float.Parse(strVal, CultureInfo.InvariantCulture);

            if (targetType == typeof(double))
                return double.Parse(strVal, CultureInfo.InvariantCulture);

            if (targetType == typeof(string))
                return strVal;

            if (targetType.IsEnum)
                return Enum.Parse(targetType, strVal, true);

            if (targetType == typeof(Vector2))
                return ParseVector2(strVal);

            if (targetType == typeof(Vector3))
                return ParseVector3(strVal);

            if (targetType == typeof(Vector4))
                return ParseVector4(strVal);

            if (targetType == typeof(Color))
                return ParseColor(strVal);

            if (targetType == typeof(Material))
                return LoadAsset<Material>(strVal);

            if (targetType == typeof(Mesh))
                return LoadAsset<Mesh>(strVal);

            if (targetType == typeof(Sprite))
                return LoadAsset<Sprite>(strVal);

            if (targetType == typeof(PhysicMaterial))
                return LoadAsset<PhysicMaterial>(strVal);

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        private static Vector2 ParseVector2(string s)
        {
            var nums = ParseFloatArray(s);
            return new Vector2(
                nums.Length > 0 ? nums[0] : 0,
                nums.Length > 1 ? nums[1] : 0);
        }

        private static Vector3 ParseVector3(string s)
        {
            var nums = ParseFloatArray(s);
            return new Vector3(
                nums.Length > 0 ? nums[0] : 0,
                nums.Length > 1 ? nums[1] : 0,
                nums.Length > 2 ? nums[2] : 0);
        }

        private static Vector4 ParseVector4(string s)
        {
            var nums = ParseFloatArray(s);
            return new Vector4(
                nums.Length > 0 ? nums[0] : 0,
                nums.Length > 1 ? nums[1] : 0,
                nums.Length > 2 ? nums[2] : 0,
                nums.Length > 3 ? nums[3] : 0);
        }

        private static Color ParseColor(string s)
        {
            if (ColorUtility.TryParseHtmlString(s, out var color))
                return color;

            var nums = ParseFloatArray(s);
            return new Color(
                nums.Length > 0 ? nums[0] : 1,
                nums.Length > 1 ? nums[1] : 1,
                nums.Length > 2 ? nums[2] : 1,
                nums.Length > 3 ? nums[3] : 1);
        }

        private static float[] ParseFloatArray(string s)
        {
            s = s.Trim('(', ')', '[', ']', ' ');
            var parts = s.Split(',', ' ');
            return parts
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => float.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0)
                .ToArray();
        }

        private static T? LoadAsset<T>(string path) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;

            var guids = AssetDatabase.FindAssets($"{System.IO.Path.GetFileNameWithoutExtension(path)} t:{typeof(T).Name}");
            if (guids.Length > 0)
            {
                var foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<T>(foundPath);
            }

            return null;
        }
    }

    /// <summary>
    /// 组件操作结果
    /// </summary>
    public class ComponentResult
    {
        public bool Success { get; set; }
        public Component? Component { get; set; }
        public List<string> Warnings { get; set; } = new();
        public string? Error { get; set; }

        public static ComponentResult Ok(Component comp) => new()
        {
            Success = true,
            Component = comp
        };

        public static ComponentResult Partial(Component comp, List<string> warnings) => new()
        {
            Success = true,
            Component = comp,
            Warnings = warnings
        };

        public static ComponentResult Fail(string error) => new()
        {
            Success = false,
            Error = error
        };
    }
}
