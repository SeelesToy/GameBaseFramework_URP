using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TurnBased.Editor.HotUpdateTools
{
    /// <summary>
    /// Publishes immutable YooAsset files before atomically switching the version pointer.
    /// Existing releases are retained so clients using an older manifest can finish downloading.
    /// </summary>
    public static class HotUpdateFilePublisher
    {
        private sealed class PublishFile
        {
            public string Source;
            public string Destination;
            public string RelativePath;
            public byte[] Hash;
        }

        public static void Publish(string sourceDirectory, string destinationDirectory, string versionFileName,
            Action<string> log = null, Func<bool> cancel = null)
        {
            ValidateFileName(versionFileName);
            string sourceRoot = NormalizeDirectory(sourceDirectory);
            string destinationRoot = NormalizeDirectory(destinationDirectory);
            if (string.Equals(destinationRoot, NormalizeDirectory(Path.GetPathRoot(destinationRoot)), PathComparison))
                throw new ArgumentException("CDN 发布目录不能是磁盘根目录。", "destinationDirectory");
            if (ContainsPath(sourceRoot, destinationRoot) || ContainsPath(destinationRoot, sourceRoot))
                throw new ArgumentException("构建输出目录与 CDN 发布目录不能相同或相互包含。");

            CheckNoReparsePoints(sourceRoot);
            CheckNoReparsePoints(destinationRoot);
            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException("构建输出目录不存在：" + sourceRoot);
            if (File.Exists(destinationRoot))
                throw new IOException("CDN 发布目录被同名文件占用：" + destinationRoot);
            CheckCanceled(cancel);

            string sourceVersion = Path.Combine(sourceRoot, versionFileName);
            if (!File.Exists(sourceVersion))
                throw new FileNotFoundException("构建输出缺少版本文件。", sourceVersion);
            CheckNoReparsePoints(sourceVersion);
            string version = File.ReadAllText(sourceVersion, Encoding.UTF8).Trim().TrimStart('\uFEFF').Trim();
            if (string.IsNullOrWhiteSpace(version) || version.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new InvalidDataException("版本文件必须包含一个非空的版本号：" + sourceVersion);

            var files = new List<PublishFile>();
            PublishFile versionFile = null;
            foreach (string path in EnumerateFilesWithoutLinks(sourceRoot))
            {
                CheckCanceled(cancel);
                if (string.Equals(Path.GetExtension(path), ".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                string relative = path.Substring(sourceRoot.Length + 1);
                var file = new PublishFile
                {
                    Source = path,
                    Destination = Path.Combine(destinationRoot, relative),
                    RelativePath = relative,
                    Hash = ComputeHash(path)
                };
                if (string.Equals(relative, versionFileName, PathComparison))
                    versionFile = file;
                else
                    files.Add(file);
            }
            if (versionFile == null)
                throw new FileNotFoundException("构建输出缺少版本文件。", sourceVersion);
            files.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.Ordinal));

            // Check all conflicts before copying anything. Files other than the pointer are immutable.
            foreach (PublishFile file in files)
            {
                CheckCanceled(cancel);
                CheckNoReparsePoints(file.Destination);
                CheckImmutableDestination(file);
            }
            CheckNoReparsePoints(versionFile.Destination);
            if (Directory.Exists(versionFile.Destination))
                throw new IOException("版本文件路径被目录占用：" + versionFile.Destination);

            EnsureDirectory(destinationRoot);
            foreach (PublishFile file in files)
            {
                CheckCanceled(cancel);
                CopyImmutableFile(file);
                SafeLog(log, "已校验并发布：" + file.RelativePath);
            }

            // Cancellation and copy/verification failures must never expose a partial release.
            CheckCanceled(cancel);
            CommitVersionFile(versionFile, cancel);
            SafeLog(log, "发布完成，版本已切换为：" + version);
        }

        private static StringComparison PathComparison
        {
            get { return Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal; }
        }

        private static string NormalizeDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("目录不能为空。");
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            return string.Equals(full, root, PathComparison)
                ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool ContainsPath(string parent, string child)
        {
            return string.Equals(parent, child, PathComparison)
                || child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar, PathComparison);
        }

        private static void ValidateFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName == "." || fileName == ".."
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0
                || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
                throw new ArgumentException("版本文件名必须是无路径的合法文件名。", "versionFileName");
        }

        private static IEnumerable<string> EnumerateFilesWithoutLinks(string root)
        {
            var directories = new Stack<string>();
            directories.Push(root);
            while (directories.Count > 0)
            {
                string directory = directories.Pop();
                CheckNoReparsePoints(directory);
                foreach (string file in Directory.GetFiles(directory))
                {
                    CheckNoReparsePoints(file);
                    yield return file;
                }
                foreach (string child in Directory.GetDirectories(directory))
                {
                    CheckNoReparsePoints(child);
                    directories.Push(child);
                }
            }
        }

        private static void CheckNoReparsePoints(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                // GetAttributes also checks existing dangling links, unlike File.Exists.
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("发布路径不能包含符号链接或目录联接：" + current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                string parent = Path.GetDirectoryName(current);
                if (string.Equals(parent, current, PathComparison))
                    break;
                current = parent;
            }
        }

        private static void EnsureDirectory(string directory)
        {
            CheckNoReparsePoints(directory);
            Directory.CreateDirectory(directory);
            CheckNoReparsePoints(directory);
        }

        private static void CheckImmutableDestination(PublishFile file)
        {
            if (Directory.Exists(file.Destination))
                throw new IOException("发布文件路径被目录占用：" + file.Destination);
            if (File.Exists(file.Destination) && !HashesEqual(file.Hash, ComputeHash(file.Destination)))
                throw new IOException("CDN 中已存在同名但内容不同的文件，已停止发布：" + file.RelativePath
                    + "。请使用新的资源版本号，不要覆盖已有版本。");
        }

        private static void CopyImmutableFile(PublishFile file)
        {
            CheckNoReparsePoints(file.Destination);
            CheckImmutableDestination(file);
            if (File.Exists(file.Destination))
                return;

            EnsureDirectory(Path.GetDirectoryName(file.Destination));
            string temporary = CreateVerifiedTemporaryFile(file);
            try
            {
                CheckNoReparsePoints(file.Destination);
                try
                {
                    // Move without overwrite prevents a concurrent publisher from replacing immutable files.
                    File.Move(temporary, file.Destination);
                }
                catch (IOException)
                {
                    if (!File.Exists(file.Destination))
                        throw;
                    CheckNoReparsePoints(file.Destination);
                    CheckImmutableDestination(file);
                }
            }
            finally
            {
                DeleteTemporaryFile(temporary);
            }
        }

        private static void CommitVersionFile(PublishFile file, Func<bool> cancel)
        {
            EnsureDirectory(Path.GetDirectoryName(file.Destination));
            string temporary = CreateVerifiedTemporaryFile(file);
            try
            {
                CheckCanceled(cancel);
                CheckNoReparsePoints(file.Destination);
                if (File.Exists(file.Destination))
                    File.Replace(temporary, file.Destination, null);
                else
                    File.Move(temporary, file.Destination);
            }
            finally
            {
                DeleteTemporaryFile(temporary);
            }
        }

        private static string CreateVerifiedTemporaryFile(PublishFile file)
        {
            CheckNoReparsePoints(file.Source);
            string temporary = Path.Combine(Path.GetDirectoryName(file.Destination),
                "." + Path.GetFileName(file.Destination) + ".hotupdate-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(file.Source, temporary, false);
                if (!HashesEqual(file.Hash, ComputeHash(temporary)))
                    throw new IOException("复制后哈希校验失败，源文件可能在发布期间发生变化：" + file.RelativePath);
                return temporary;
            }
            catch
            {
                DeleteTemporaryFile(temporary);
                throw;
            }
        }

        private static byte[] ComputeHash(string file)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create())
                return sha.ComputeHash(stream);
        }

        private static bool HashesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        private static void CheckCanceled(Func<bool> cancel)
        {
            if (cancel != null && cancel())
                throw new OperationCanceledException("已取消发布，CDN 版本号尚未切换。");
        }

        private static void SafeLog(Action<string> log, string message)
        {
            if (log == null)
                return;
            // A failing UI logger must not turn a successful atomic commit into a reported failure.
            try { log(message); }
            catch { }
        }

        private static void DeleteTemporaryFile(string file)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch { }
        }
    }
}
