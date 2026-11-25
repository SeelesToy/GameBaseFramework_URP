using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace TurnBased.Editor.HotUpdateTools
{
    internal static class HotUpdatePublisherPipeline
    {
        internal const string CodeAssetDirectory = "Assets/GameMain/Res/Code";
        internal static string ProjectRoot { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        internal static bool IsRunning { get; private set; }

        internal static string NewVersion() { return DateTime.Now.ToString("yyyy-MM-dd-HHmmss-fff"); }

        internal static string PlatformDirectory(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "IPhone";
                case BuildTarget.WebGL: return "WebGL";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64: return "PC";
                default: throw new InvalidOperationException("现有客户端下载逻辑尚未配置该平台：" + target);
            }
        }

        internal static string GetPublishDirectory(HotUpdatePublishConfig c, BuildTarget target)
        {
            ValidateSegment(c.clientVersion, "客户端资源目录");
            return Path.Combine(AbsoluteDirectory(c.cdnRoot, "CDN 根目录"), PlatformDirectory(target), c.clientVersion);
        }

        internal static void Validate(HotUpdatePublishConfig c, BuildTarget target, bool publish)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || UnityEditor.BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("请退出运行模式，等待 Unity 编译/导入/构建完成后再执行。");
            if (target != EditorUserBuildSettings.activeBuildTarget)
                throw new InvalidOperationException("构建平台已改变，请重新打开或执行发布。");
            ValidateSegment(c.packageName, "资源包名称");
            ValidateSegment(c.resourceVersion, "资源版本号");
            ValidateSegment(c.clientVersion, "客户端资源目录");
            string outputRoot = AbsoluteDirectory(c.buildOutputRoot, "资源构建输出目录");
            string code = Path.Combine(ProjectRoot, CodeAssetDirectory);
            string baseline = AbsoluteDirectory(c.aotBaselineDirectory, "主包 AOT 目录");
            RejectProjectDataDirectory(outputRoot, "资源构建输出目录");
            RequireSeparate(outputRoot, baseline, "构建输出目录与主包 AOT 目录");
            HotUpdateCodeBuilder.ValidateBaseline(baseline, code);
            var setting = BundleCollectorSettingData.Setting;
            var package = setting.Packages.FirstOrDefault(p => p.PackageName == c.packageName);
            if (package == null) throw new InvalidOperationException("YooAsset 收集配置中没有资源包：" + c.packageName);
            package.CheckConfigError();
            bool collectsCode = package.Groups.SelectMany(g => g.Collectors).Any(collector =>
            {
                string p = (collector.CollectPath ?? "").Replace('\\', '/').TrimEnd('/');
                return p.Length > 0 && (CodeAssetDirectory == p || CodeAssetDirectory.StartsWith(p + "/", StringComparison.Ordinal));
            });
            if (!collectsCode) throw new InvalidOperationException("所选资源包没有收集代码资源目录：" + CodeAssetDirectory);
            string expectedOutput = Path.Combine(outputRoot, target.ToString(), c.packageName, c.resourceVersion);
            if (Directory.Exists(expectedOutput) || File.Exists(expectedOutput))
                throw new InvalidOperationException("资源版本的构建目录已经存在，请使用新版本号：" + expectedOutput);

            if (publish)
            {
                string destination = GetPublishDirectory(c, target);
                RejectProjectDataDirectory(destination, "CDN 发布目录");
                RequireSeparate(destination, outputRoot, "CDN 发布目录与构建输出目录");
                RequireSeparate(destination, baseline, "CDN 发布目录与主包 AOT 目录");
                EnsureNoReparsePoint(destination);
                string manifest = YooAssetConfiguration.GetManifestBinaryFileName(c.packageName, c.resourceVersion);
                if (File.Exists(Path.Combine(destination, manifest)))
                    throw new InvalidOperationException("CDN 已存在相同版本，请使用新版本号：" + c.resourceVersion);
            }
        }

        internal static string Run(HotUpdatePublishConfig c, BuildTarget target, bool development,
            bool publish, Action<string> log, bool interactive)
        {
            if (IsRunning) throw new InvalidOperationException("已有热更新任务正在执行。");
            Validate(c, target, publish);
            IsRunning = true;
            var history = new List<string>();
            Action<string> report = message =>
            {
                history.Add(DateTime.Now.ToString("HH:mm:ss") + " " + message);
                Debug.Log("[热更新发布] " + message);
                try { if (log != null) log(message); }
                catch (Exception e) { Debug.LogWarning("发布日志回调失败：" + e.Message); }
            };
            EditorApplication.LockReloadAssemblies();
            string resultDirectory = null;
            try
            {
                Step("1/4 编译并检查业务代码", 0.05f, report, interactive);
                report("资源版本：" + c.resourceVersion + "；平台：" + target);
                report("主包 AOT 基线：" + c.aotBaselineDirectory);
                HotUpdateCodeBuilder.Prepare(target, development, c.aotBaselineDirectory,
                    Path.Combine(ProjectRoot, CodeAssetDirectory), report);

                Step("2/4 导入代码资源并构建 YooAsset", 0.35f, report, interactive);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.SaveAssets();
                var parameters = new ScriptableBuildParameters
                {
                    BuildOutputRoot = AbsoluteDirectory(c.buildOutputRoot, "资源构建输出目录"),
                    BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
                    BuildPipeline = EBuildPipeline.ScriptableBuildPipeline.ToString(),
                    BuildBundleType = (int)EBundleType.AssetBundle,
                    BuildTarget = target,
                    PackageName = c.packageName,
                    PackageVersion = c.resourceVersion,
                    PackageNote = c.note,
                    EnableSharePackRule = true,
                    VerifyBuildingResult = true,
                    FileNameStyle = EFileNameStyle.HashName,
                    CompressOption = c.compression,
                    BundledCopyOption = EBundledCopyOption.None,
                    ClearBuildCacheFiles = false,
                    UseAssetDependencyDB = false,
                    BundleEncryptor = new EncryptionNone(),
                    ManifestEncryptor = new ManifestEncryptorNone(),
                    ManifestDecryptor = new ManifestDecryptorNone(),
                    BuiltinShadersBundleName = DefaultBundlePackRule.CreateShadersPackRuleResult()
                        .GetBundleName(c.packageName, BundleCollectorSettingData.Setting.UniqueBundleName)
                };
                var result = new YooAsset.Editor.ScriptableBuildPipeline().Run(parameters, true);
                if (!result.Success)
                    throw new InvalidOperationException("YooAsset 构建失败：" + result.FailedTask + "\n" + result.ErrorInfo);
                resultDirectory = Path.GetFullPath(result.OutputPackageDirectory);

                Step("3/4 检查构建结果", 0.8f, report, interactive);
                string staging = VerifyAndStage(resultDirectory, c, target);
                report("构建产物：" + resultDirectory);
                if (publish)
                {
                    Step("4/4 发布文件，最后切换版本号", 0.9f, report, interactive);
                    string destination = GetPublishDirectory(c, target);
                    HotUpdateFilePublisher.Publish(staging, destination,
                        YooAssetConfiguration.GetPackageVersionFileName(c.packageName), report,
                        interactive ? (Func<bool>)(() => EditorUtility.DisplayCancelableProgressBar(
                            "热更新发布", "正在校验并发布文件；完成后更新版本号", 0.95f)) : null);
                    report("发布成功：" + destination);
                }
                else
                    report("构建检查通过，未写入 CDN。可发布文件：" + staging);
                return resultDirectory;
            }
            catch (Exception e)
            {
                report(e is OperationCanceledException ? "操作已取消。" : "失败：" + e.Message);
                throw;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                EditorApplication.UnlockReloadAssemblies();
                IsRunning = false;
                // Logging failure must never turn a completed version commit into an apparent failure.
                try
                {
                    string reportDirectory = Path.Combine(ProjectRoot, "Library/HotUpdatePublisher/Logs");
                    Directory.CreateDirectory(reportDirectory);
                    File.WriteAllLines(Path.Combine(reportDirectory, c.resourceVersion + "-" + Guid.NewGuid().ToString("N") + ".log"),
                        history, new UTF8Encoding(false));
                }
                catch (Exception e) { Debug.LogWarning("发布日志保存失败：" + e.Message); }
            }
        }

        private static string VerifyAndStage(string output, HotUpdatePublishConfig c, BuildTarget target)
        {
            string reportName = YooAssetConfiguration.GetBuildReportFileName(c.packageName, c.resourceVersion);
            var buildReport = JsonUtility.FromJson<YooAsset.Editor.BuildReport>(File.ReadAllText(Path.Combine(output, reportName)));
            if (buildReport == null || buildReport.Summary.BuildPackageName != c.packageName ||
                buildReport.Summary.BuildPackageVersion != c.resourceVersion || buildReport.Summary.BuildTarget != target)
                throw new InvalidDataException("构建报告的包名、平台或版本与本次任务不一致。");
            var paths = new HashSet<string>(buildReport.AssetInfos.Select(a => a.AssetPath), StringComparer.Ordinal);
            var required = new List<string>
            {
                CodeAssetDirectory + "/HotUpdate.dll.bytes",
                CodeAssetDirectory + "/AOTAssemblyList.txt"
            };
            required.AddRange(File.ReadAllLines(Path.Combine(ProjectRoot, CodeAssetDirectory, "AOTAssemblyList.txt"))
                .Select(n => n.Trim()).Where(n => n.Length > 0).Select(n => CodeAssetDirectory + "/AOT/" + n + ".bytes"));
            foreach (string path in required)
                if (!paths.Contains(path))
                    throw new InvalidDataException("构建结果未收集客户端需要的代码资源：" + path);

            string versionFile = YooAssetConfiguration.GetPackageVersionFileName(c.packageName);
            if (File.ReadAllText(Path.Combine(output, versionFile)).Trim().TrimStart('\uFEFF') != c.resourceVersion)
                throw new InvalidDataException("构建输出的版本文件内容不匹配。");
            string manifestPath = Path.Combine(output, YooAssetConfiguration.GetManifestBinaryFileName(c.packageName, c.resourceVersion));
            string hashPath = Path.Combine(output, YooAssetConfiguration.GetPackageHashFileName(c.packageName, c.resourceVersion));
            if (!string.Equals(HashUtility.ComputeFileCrc32(manifestPath), File.ReadAllText(hashPath).Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("资源清单与 hash 文件不匹配，停止发布。");
            string staging = Path.Combine(ProjectRoot, "Library/HotUpdatePublisher/Publish", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            // Publish only files referenced by this report plus the runtime manifest/hash/version.
            // Build logs and link.xml are local diagnostics, not immutable CDN assets.
            foreach (var bundle in buildReport.BundleInfos)
            {
                ValidateFileName(bundle.FileName);
                string source = Path.Combine(output, bundle.FileName);
                if (!File.Exists(source) || new FileInfo(source).Length != bundle.FileSize)
                    throw new InvalidDataException("资源文件缺失或大小不符：" + source);
                if (HashUtility.ComputeFileCrc32AsUInt(source) != bundle.FileCrc)
                    throw new InvalidDataException("资源文件 CRC 校验失败：" + source);
                File.Copy(source, Path.Combine(staging, bundle.FileName), false);
            }
            foreach (string name in new[]
            {
                YooAssetConfiguration.GetManifestBinaryFileName(c.packageName, c.resourceVersion),
                YooAssetConfiguration.GetPackageHashFileName(c.packageName, c.resourceVersion),
                versionFile
            })
            {
                ValidateFileName(name);
                string source = Path.Combine(output, name);
                if (!File.Exists(source) || new FileInfo(source).Length == 0)
                    throw new InvalidDataException("资源清单文件缺失或为空：" + source);
                File.Copy(source, Path.Combine(staging, name), false);
            }
            return staging;
        }

        private static void Step(string name, float progress, Action<string> report, bool interactive)
        {
            report(name);
            if (interactive && EditorUtility.DisplayCancelableProgressBar("热更新发布", name, progress))
                throw new OperationCanceledException();
        }

        private static string AbsoluteDirectory(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(label + "必须填写完整磁盘路径。");
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string suppliedRoot = Path.GetPathRoot(path);
            if (!string.Equals(suppliedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                (suppliedRoot.Length == 2 && suppliedRoot[1] == ':'))
                throw new ArgumentException(label + "必须填写完整磁盘路径。");
            if (full == Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                throw new ArgumentException(label + "不能是磁盘根目录。");
            EnsureNoReparsePoint(full);
            return full;
        }

        private static void ValidateSegment(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value, @"^[A-Za-z0-9][A-Za-z0-9._-]*$") ||
                value.EndsWith(".", StringComparison.Ordinal))
                throw new ArgumentException(label + "只允许字母、数字、点、短横线和下划线，且不能包含目录分隔符。");
        }

        private static void ValidateFileName(string name)
        {
            if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || name.Contains(":") ||
                name == "." || name == ".." || name.Contains("/") || name.Contains("\\"))
                throw new InvalidDataException("构建结果包含非法文件名：" + name);
        }

        private static bool ContainsPath(string parent, string child)
        {
            parent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            child = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
                child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void RequireSeparate(string first, string second, string label)
        {
            if (ContainsPath(first, second) || ContainsPath(second, first))
                throw new InvalidOperationException(label + "不能相同或相互包含。");
        }

        private static void RejectProjectDataDirectory(string path, string label)
        {
            if (ContainsPath(path, ProjectRoot))
                throw new InvalidOperationException(label + "不能是项目根目录或其父目录。");
            foreach (string protectedFolder in new[] { "Assets", "Packages", "Library", "ProjectSettings", "UserSettings", ".git" })
                RequireSeparate(path, Path.Combine(ProjectRoot, protectedFolder), label + "与项目 " + protectedFolder);
        }

        private static void EnsureNoReparsePoint(string path)
        {
            for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("发布路径不能经过目录联接或符号链接：" + directory.FullName);
        }
    }
}
