using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameBaseFramework.Editor.LubanTools
{
    /// <summary>使用项目现有 Luban 配置导表；所有路径从当前 Unity 项目推导。</summary>
    public static class LubanEditorMenu
    {
        private const string GenerateMenu = "Tools/Luban/打表";
        private const string OpenSourceMenu = "Tools/Luban/打开源表文件夹";
        private static bool s_IsGenerating;

        private static string ConfigDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../Config/DataTables"));

        [Serializable]
        private sealed class LubanConfiguration
        {
            public string dataDir;
        }

        [MenuItem(GenerateMenu, priority = 200)]
        public static async void Generate()
        {
            if (!CanGenerate())
                return;

            bool reloadLocked = false;
            bool refreshDisabled = false;
            bool started = false;
            s_IsGenerating = true;
            try
            {
                string configDirectory = ConfigDirectory;
                string configuration = Path.Combine(configDirectory, "luban.conf");
                string lubanDll = Path.GetFullPath(Path.Combine(configDirectory, "../Tools/Luban/Luban.dll"));
                string templates = Path.Combine(configDirectory, "Templates");
                RequireFile(configuration);
                RequireFile(lubanDll);
                GetSourceDirectory();
                if (!Directory.Exists(templates))
                    throw new DirectoryNotFoundException("找不到 Luban 自定义模板目录：" + templates);

                // 保留 cs-bin/bin、严格校验和自定义模板，与现有导表配置一致。
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = Quote(lubanDll)
                        + " --conf " + Quote(configuration)
                        + " --customTemplateDir " + Quote(templates)
                        + " -t client -c cs-bin -d bin --strict"
                        + " -x " + Quote("outputCodeDir=" + Path.Combine(Application.dataPath, "GameMain/Scripts/Generated/Luban"))
                        + " -x " + Quote("outputDataDir=" + Path.Combine(Application.dataPath, "GameMain/Res/DataTables/Luban")),
                    WorkingDirectory = configDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                // 生成过程中暂停自动导入，避免只写完部分文件时触发编译或程序集重载。
                EditorApplication.LockReloadAssemblies();
                reloadLocked = true;
                AssetDatabase.DisallowAutoRefresh();
                refreshDisabled = true;
                if (!Application.isBatchMode)
                    EditorUtility.DisplayProgressBar("Luban 打表", "正在生成配置代码和二进制数据……", 0.5f);

                Debug.Log("[Luban] 开始打表：" + configuration);
                started = true;
                string output = await Task.Run(() => RunGenerator(startInfo));
                if (!string.IsNullOrWhiteSpace(output))
                    Debug.Log("[Luban]\n" + output.Trim());
                Debug.Log("[Luban] 打表完成，正在刷新生成的代码和资源。");
            }
            catch (Win32Exception exception)
            {
                Debug.LogError("[Luban] 无法启动 dotnet。请确认已安装 .NET SDK，且 Unity 的 PATH 中可以找到 dotnet。\n"
                    + exception.Message);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Luban] 打表失败：\n" + exception.Message);
            }
            finally
            {
                if (!Application.isBatchMode && started)
                    EditorUtility.ClearProgressBar();
                if (refreshDisabled)
                    AssetDatabase.AllowAutoRefresh();
                if (reloadLocked)
                    EditorApplication.UnlockReloadAssemblies();
                s_IsGenerating = false;

                // 失败时也刷新，确保 Unity 与磁盘上可能已经写出的文件保持一致。
                if (started)
                    AssetDatabase.Refresh();
            }
        }

        [MenuItem(GenerateMenu, true)]
        private static bool CanGenerate()
        {
            return !s_IsGenerating && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem(OpenSourceMenu, priority = 201)]
        public static void OpenSourceDirectory()
        {
            try
            {
                // 读取 dataDir，打开配置所指向的源表目录。
                using (Process.Start(new ProcessStartInfo
                {
                    FileName = GetSourceDirectory(),
                    UseShellExecute = true
                }))
                {
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[Luban] 无法打开源表文件夹：\n" + exception.Message);
            }
        }

        private static string GetSourceDirectory()
        {
            string configuration = Path.Combine(ConfigDirectory, "luban.conf");
            RequireFile(configuration);
            var config = JsonUtility.FromJson<LubanConfiguration>(File.ReadAllText(configuration));
            if (config == null || string.IsNullOrWhiteSpace(config.dataDir))
                throw new InvalidDataException("luban.conf 未配置 dataDir。");

            string directory = Path.GetFullPath(Path.Combine(ConfigDirectory, config.dataDir));
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException("找不到源表文件夹：" + directory);
            return directory;
        }

        private static string RunGenerator(ProcessStartInfo startInfo)
        {
            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                    throw new InvalidOperationException("未能启动 Luban 进程。");

                // 同时读取两个管道，避免日志较多时阻塞外部进程。
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                string output = stdout.GetAwaiter().GetResult() + "\n" + stderr.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Luban 退出码：" + process.ExitCode + "\n" + output.Trim());
                return output;
            }
        }

        private static void RequireFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("找不到文件，请检查 Config 子模块是否完整：" + path, path);
        }

        private static string Quote(string argument)
        {
            return "\"" + argument + "\"";
        }
    }
}
