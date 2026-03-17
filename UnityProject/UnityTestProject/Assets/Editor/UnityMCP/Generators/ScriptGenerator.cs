#nullable enable

using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Generators
{
    /// <summary>
    /// 脚本生成器。
    /// 负责将 AI 生成的代码保存为 Unity 项目中的 .cs 文件。
    /// </summary>
    public static class ScriptGenerator
    {
        private const string DEFAULT_SCRIPT_PATH = "Assets/Scripts/Generated";

        /// <summary>
        /// 脚本保存结果
        /// </summary>
        public class SaveResult
        {
            /// <summary>是否成功</summary>
            public bool Success { get; set; }

            /// <summary>保存的文件路径（Assets/... 格式）</summary>
            public string FilePath { get; set; } = "";

            /// <summary>保存的文件完整系统路径</summary>
            public string FullPath { get; set; } = "";

            /// <summary>错误信息（失败时）</summary>
            public string? Error { get; set; }
        }

        /// <summary>
        /// 将代码保存为 Unity 脚本文件
        /// </summary>
        /// <param name="scriptName">脚本名称（不含 .cs 后缀）</param>
        /// <param name="code">完整的 C# 代码</param>
        /// <param name="outputFolder">输出文件夹（Assets/... 格式），为 null 时使用默认路径</param>
        /// <returns>保存结果</returns>
        public static SaveResult SaveScript(string scriptName, string code, string? outputFolder = null)
        {
            var folder = outputFolder ?? DEFAULT_SCRIPT_PATH;

            if (!EnsureDirectoryExists(folder))
            {
                return new SaveResult
                {
                    Success = false,
                    Error = $"无法创建目录: {folder}"
                };
            }

            var assetPath = $"{folder}/{scriptName}.cs";
            var fullPath = Path.Combine(Application.dataPath, "..", assetPath);
            fullPath = Path.GetFullPath(fullPath);

            if (File.Exists(fullPath))
            {
                var backupPath = fullPath + ".bak";
                try
                {
                    File.Copy(fullPath, backupPath, true);
                }
                catch
                {
                    // 备份失败不阻止主流程
                }
            }

            try
            {
                File.WriteAllText(fullPath, code, System.Text.Encoding.UTF8);
                AssetDatabase.Refresh();

                Debug.Log($"[UnityMCP] 脚本已生成: {assetPath}");

                return new SaveResult
                {
                    Success = true,
                    FilePath = assetPath,
                    FullPath = fullPath
                };
            }
            catch (System.Exception ex)
            {
                return new SaveResult
                {
                    Success = false,
                    Error = $"写入文件失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 检查脚本名称是否已存在
        /// </summary>
        public static bool ScriptExists(string scriptName, string? outputFolder = null)
        {
            var folder = outputFolder ?? DEFAULT_SCRIPT_PATH;
            var assetPath = $"{folder}/{scriptName}.cs";
            var fullPath = Path.Combine(Application.dataPath, "..", assetPath);
            return File.Exists(fullPath);
        }

        /// <summary>
        /// 获取脚本的完整输出路径
        /// </summary>
        public static string GetScriptPath(string scriptName, string? outputFolder = null)
        {
            var folder = outputFolder ?? DEFAULT_SCRIPT_PATH;
            return $"{folder}/{scriptName}.cs";
        }

        private static bool EnsureDirectoryExists(string assetPath)
        {
            var fullPath = Path.Combine(Application.dataPath, "..", assetPath);
            fullPath = Path.GetFullPath(fullPath);

            try
            {
                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
