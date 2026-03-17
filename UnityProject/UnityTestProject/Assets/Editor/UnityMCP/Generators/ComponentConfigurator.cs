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
        /// 常用组件类型的短名映射，AI 可以用短名来指定组件
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

            // UI
            { "Canvas", typeof(Canvas) },
            { "CanvasRenderer", typeof(CanvasRenderer) },
            { "RectTransform", typeof(RectTransform) },

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
        /// 根据组件描述向 GameObject 添加组件并设置属性
        /// </summary>
        /// <param name="go">目标 GameObject</param>
        /// <param name="desc">组件描述</param>
        /// <returns>添加结果，包含错误信息（如有）</returns>
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
        /// 解析类型名称为 Type 对象
        /// </summary>
        private static Type? ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            typeName = typeName.Trim();

            if (ShortNameMap.TryGetValue(typeName, out var mappedType))
                return mappedType;

            var lastDot = typeName.LastIndexOf('.');
            if (lastDot >= 0)
            {
                var shortName = typeName.Substring(lastDot + 1);
                if (ShortNameMap.TryGetValue(shortName, out var fromFull))
                    return fromFull;
            }

            var resolved = Type.GetType(typeName);
            if (resolved != null)
                return resolved;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                resolved = asm.GetTypes().FirstOrDefault(t =>
                    t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                    t.FullName != null && t.FullName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                if (resolved != null && typeof(Component).IsAssignableFrom(resolved))
                    return resolved;
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
