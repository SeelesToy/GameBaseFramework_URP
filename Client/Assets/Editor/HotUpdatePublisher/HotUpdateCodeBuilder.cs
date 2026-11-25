using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.HotUpdate;
using HybridCLR.Editor.Meta;
using UnityEditor;
using UnityEngine;
using AotAnalyzer = HybridCLR.Editor.AOT.Analyzer;
using ManagedAssemblyName = System.Reflection.AssemblyName;

namespace TurnBased.Editor.HotUpdateTools
{
    /// <summary>Prepares code resources against the AOT assemblies of an existing player build.</summary>
    internal static class HotUpdateCodeBuilder
    {
        private const string HotAssemblyName = "HotUpdate";
        private const string HotAssemblyFile = HotAssemblyName + ".dll";
        private const string AotListFile = "AOTAssemblyList.txt";
        private static readonly string[] RequiredBaselineFiles = { "GameFramework.dll", "mscorlib.dll" };
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        internal static void ValidateBaseline(string baselineDirectory, string codeDirectory)
        {
            string baseline = GetAbsoluteDirectory(baselineDirectory, "AOT 主包基线");
            string code = GetAbsoluteDirectory(codeDirectory, "代码资源");
            if (IsSameOrChild(baseline, code) || IsSameOrChild(code, baseline))
                throw new InvalidOperationException("AOT 主包基线与代码资源目录不能相同或互相包含。");

            if (!Directory.Exists(baseline))
                throw new DirectoryNotFoundException("找不到 AOT 主包基线目录：" + baseline);
            if (File.Exists(code) || File.Exists(Path.Combine(code, "AOT")))
                throw new InvalidOperationException("代码资源目录或 AOT 子目录被同名文件占用。");

            ValidateDestination(Path.Combine(code, AotListFile));
            ValidateDestination(Path.Combine(code, HotAssemblyFile + ".bytes"));
            foreach (string dllName in RequiredBaselineFiles.Concat(ReadExistingAotList(code)).Distinct(StringComparer.OrdinalIgnoreCase))
                ValidateManagedAssembly(Path.Combine(baseline, dllName), dllName);
        }

        internal static void Prepare(BuildTarget target, bool development, string baselineDirectory,
            string codeDirectory, Action<string> log)
        {
            ValidateBaseline(baselineDirectory, codeDirectory);
            string baseline = GetAbsoluteDirectory(baselineDirectory, "AOT 主包基线");
            string code = GetAbsoluteDirectory(codeDirectory, "代码资源");
            Action<string> report = log ?? (_ => { });

            if (!SettingsUtil.Enable)
                throw new InvalidOperationException("HybridCLR 已禁用，请先启用后再发布热更新。");

            List<string> hotNames = SettingsUtil.HotUpdateAssemblyNamesExcludePreserved;
            List<string> allHotNames = SettingsUtil.HotUpdateAssemblyNamesIncludePreserved;
            if (hotNames.Count != 1 || hotNames[0] != HotAssemblyName ||
                allHotNames.Count != 1 || allHotNames[0] != HotAssemblyName)
                throw new InvalidOperationException("当前 LoadDll 仅加载 HotUpdate.dll，HybridCLR 热更程序集配置必须恰好只有 HotUpdate。");

            string compileDirectory = Path.Combine(SettingsUtil.ProjectDir, "Library", "HotUpdatePublisher",
                "Compile", Guid.NewGuid().ToString("N"));
            report("编译热更 DLL：" + target + (development ? " / Development" : " / Release"));
            CompileFresh(compileDirectory, target, development);

            string hotDll = Path.Combine(compileDirectory, HotAssemblyFile);
            ValidateManagedAssembly(hotDll, HotAssemblyFile);

            report("检查热更代码是否引用了主包中已被裁剪的类型或成员。");
            var checker = new MissingMetadataChecker(baseline, allHotNames);
            var metadataErrors = new List<string>();
            Application.LogCallback captureMissing = (message, stackTrace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    metadataErrors.Add(message);
            };
            bool metadataValid;
            Application.logMessageReceived += captureMissing;
            try
            {
                metadataValid = checker.Check(hotDll);
            }
            finally
            {
                Application.logMessageReceived -= captureMissing;
            }
            if (!metadataValid)
            {
                string details = string.Join("\n", metadataErrors.Take(8));
                if (details.Length > 0)
                    report(details);
                throw new InvalidOperationException("热更代码引用了主包缺少的 AOT 程序集、类型或成员。请调整代码，或重新构建并发布主包；不能通过重新 GenerateAll 修复已安装的旧主包。\n" + details);
            }

            report("分析 AOT 泛型补充需求，复用指定主包基线。");
            var aotNames = new SortedSet<string>(ReadExistingAotList(code), StringComparer.Ordinal);
            aotNames.UnionWith(GetRequiredAotFiles(compileDirectory, baseline, hotNames));

            // Read and validate every input before changing any published code resource.
            var aotBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string dllName in aotNames)
            {
                ValidateDllFileName(dllName);
                string source = Path.Combine(baseline, dllName);
                ValidateManagedAssembly(source, dllName);
                ValidateDestination(Path.Combine(code, "AOT", dllName + ".bytes"));
                aotBytes.Add(dllName, File.ReadAllBytes(source));
            }
            byte[] hotBytes = File.ReadAllBytes(hotDll);
            byte[] listBytes = Utf8WithoutBom.GetBytes(
                aotNames.Count == 0 ? string.Empty : string.Join("\n", aotNames) + "\n");

            Directory.CreateDirectory(code);
            Directory.CreateDirectory(Path.Combine(code, "AOT"));
            foreach (var entry in aotBytes)
                File.WriteAllBytes(Path.Combine(code, "AOT", entry.Key + ".bytes"), entry.Value);
            File.WriteAllBytes(Path.Combine(code, HotAssemblyFile + ".bytes"), hotBytes);
            File.WriteAllBytes(Path.Combine(code, AotListFile), listBytes);
            report("代码资源准备完成：HotUpdate.dll + " + aotBytes.Count + " 个 AOT 补充程序集。");
        }

        private static void CompileFresh(string outputDirectory, BuildTarget target, bool development)
        {
            if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
                throw new IOException("本次编译目录已存在，无法保证产物为新生成：" + outputDirectory);

            var errors = new List<string>();
            Application.LogCallback capture = (message, stackTrace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    errors.Add(message);
            };
            Application.logMessageReceived += capture;
            try
            {
                CompileDllCommand.CompileDll(outputDirectory, target, development);
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }

            if (errors.Count != 0)
                throw new InvalidOperationException("热更 DLL 编译失败，未更新代码资源：\n" +
                    string.Join("\n", errors.Take(5)));
        }

        private static IEnumerable<string> GetRequiredAotFiles(string compileDirectory,
            string baseline, List<string> hotNames)
        {
            var resolver = new CombinedAssemblyResolver(
                new FixedSetAssemblyResolver(compileDirectory, hotNames),
                new PathAssemblyResolver(baseline));
            var collector = new AssemblyReferenceDeepCollector(resolver, hotNames);
            try
            {
                var analyzer = new AotAnalyzer(new AotAnalyzer.Options
                {
                    Collector = collector,
                    MaxIterationCount = Math.Max(1, Math.Min(20,
                        SettingsUtil.HybridCLRSettings.maxGenericReferenceIteration))
                });
                analyzer.Run();
                return analyzer.AotGenericTypes.Select(t => t.Type.Module.Name.ToString())
                    .Concat(analyzer.AotGenericMethods.Select(m => m.Method.Module.Name.ToString()))
                    .Distinct(StringComparer.Ordinal).ToArray();
            }
            finally
            {
                foreach (var module in collector.LoadedModules.Values)
                    module.Dispose();
            }
        }

        private static List<string> ReadExistingAotList(string codeDirectory)
        {
            string path = Path.Combine(codeDirectory, AotListFile);
            var names = new List<string>();
            if (!File.Exists(path))
                return names;

            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string name = line.Trim().TrimStart('\uFEFF');
                if (name.Length == 0)
                    continue;
                ValidateDllFileName(name);
                if (string.Equals(name, HotAssemblyFile, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("HotUpdate.dll 是热更程序集，不能加入 AOT 补充名单。");
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }
            return names;
        }

        private static void ValidateManagedAssembly(string path, string dllName)
        {
            ValidateDllFileName(dllName);
            if (!File.Exists(path))
                throw new FileNotFoundException("主包基线或本次编译产物缺少程序集：" + dllName, path);

            ManagedAssemblyName identity;
            try
            {
                identity = ManagedAssemblyName.GetAssemblyName(path);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("文件不是有效托管程序集：" + path, exception);
            }
            string expected = Path.GetFileNameWithoutExtension(dllName);
            if (!string.Equals(identity.Name, expected, StringComparison.Ordinal))
                throw new InvalidOperationException("程序集名称与文件名不符：" + path +
                    "，实际程序集名：" + identity.Name);
        }

        private static void ValidateDllFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".dll", StringComparison.Ordinal) ||
                name.Length <= 4 || name != Path.GetFileName(name) ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new InvalidOperationException("AOT 名单只能包含带 .dll 后缀的程序集文件名：" + name);
        }

        private static string GetAbsoluteDirectory(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
                throw new ArgumentException(label + "目录必须使用绝对路径。");
            string fullPath = Path.GetFullPath(value);
            string root = Path.GetPathRoot(fullPath);
            string suppliedRoot = Path.GetPathRoot(value);
            if (!string.Equals(suppliedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                (suppliedRoot.Length == 2 && suppliedRoot[1] == ':'))
                throw new ArgumentException(label + "目录必须使用完整绝对路径。");
            return fullPath.Length > root.Length
                ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : fullPath;
        }

        private static bool IsSameOrChild(string path, string parent)
        {
            return string.Equals(path, parent, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateDestination(string path)
        {
            if (Directory.Exists(path))
                throw new IOException("代码资源文件路径被同名目录占用：" + path);
        }
    }
}
