using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace TurnBased.Editor.HotUpdateTools
{
    [Serializable]
    internal sealed class HotUpdatePublishConfig
    {
        public string packageName = "TurnBasePackage";
        public string clientVersion = "v1.0";
        public string cdnRoot = "";
        public string buildOutputRoot = "";
        public string aotBaselineDirectory = "";
        public bool automaticVersion = true;
        public string resourceVersion = "";
        public ECompressOption compression = ECompressOption.LZ4;
        public string note = "";
    }

    [FilePath("UserSettings/HotUpdatePublisher.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class HotUpdatePublisherSettings : ScriptableSingleton<HotUpdatePublisherSettings>
    {
        public HotUpdatePublishConfig config = new HotUpdatePublishConfig();
        public void SaveSettings() { Save(true); }
    }

    public sealed class HotUpdatePublisherWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _status = "准备就绪";
        private MessageType _statusType = MessageType.Info;
        private readonly List<string> _logs = new List<string>();
        private static bool _scheduled;
        private string _lastOutput;

        [MenuItem("Tools/HotUpdate/PublishWindow", priority = 100)]
        public static void Open()
        {
            var window = GetWindow<HotUpdatePublisherWindow>("热更新发布");
            window.minSize = new Vector2(620, 610);
            window.Show();
        }

        private void OnEnable()
        {
            var settings = HotUpdatePublisherSettings.instance;
            var c = settings.config;
            if (string.IsNullOrWhiteSpace(c.cdnRoot))
                c.cdnRoot = Path.GetFullPath(Path.Combine(HotUpdatePublisherPipeline.ProjectRoot, "../CDNFile/CDN"));
            if (string.IsNullOrWhiteSpace(c.buildOutputRoot))
                c.buildOutputRoot = Path.Combine(HotUpdatePublisherPipeline.ProjectRoot, "Bundles");
            if (string.IsNullOrWhiteSpace(c.resourceVersion))
                c.resourceVersion = HotUpdatePublisherPipeline.NewVersion();
            if (string.IsNullOrWhiteSpace(c.aotBaselineDirectory))
                c.aotBaselineDirectory = FindPlayerBaseline();
            settings.SaveSettings();
        }

        private static string FindPlayerBaseline()
        {
            string buildRoot = Path.Combine(HotUpdatePublisherPipeline.ProjectRoot, "Build");
            if (!Directory.Exists(buildRoot)) return "";
            // Only look in the project's existing player builds, never use freshly generated AOT DLLs.
            return Directory.GetDirectories(buildRoot, "*BackUpThisFolder_ButDontShipItWithYourGame", SearchOption.AllDirectories)
                .Select(p => Path.Combine(p, "Managed"))
                .Where(p => File.Exists(Path.Combine(p, "mscorlib.dll")))
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? "";
        }

        private void OnGUI()
        {
            var settings = HotUpdatePublisherSettings.instance;
            var c = settings.config;
            bool busy = _scheduled || HotUpdatePublisherPipeline.IsRunning;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("热更新发布", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("编译业务代码 → 校验主包兼容性 → 构建资源 → 发布到 CDN 目录。\n发布完成后，客户端下次启动会检测并下载新版本。", MessageType.Info);
            using (new EditorGUI.DisabledScope(busy))
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.LabelField("构建平台", EditorUserBuildSettings.activeBuildTarget.ToString());
                EditorGUILayout.LabelField("代码编译模式", EditorUserBuildSettings.development ? "Development（与当前构建设置一致）" : "Release");
                c.packageName = EditorGUILayout.TextField(new GUIContent("资源包名称", "应与 GameBoot 的 packageName 保持一致。"), c.packageName);
                c.clientVersion = EditorGUILayout.TextField(new GUIContent("客户端资源目录", "当前客户端使用 v1.0。修改目录不会改变已安装客户端的下载地址。"), c.clientVersion);
                c.cdnRoot = FolderField("CDN 根目录", c.cdnRoot);
                string destination;
                try { destination = HotUpdatePublisherPipeline.GetPublishDirectory(c, EditorUserBuildSettings.activeBuildTarget); }
                catch { destination = "请填写有效的 CDN 根目录和客户端资源目录"; }
                EditorGUILayout.LabelField("最终发布目录", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(destination, EditorStyles.textField, GUILayout.Height(36));
                EditorGUILayout.HelpBox("CDN 根目录是本地或挂载磁盘上的 CDN 文件夹，不是 http 地址。现有客户端访问 /CDN/平台/v1.0；修改这里不会修改客户端 URL，也不会启动 HTTP 服务。", MessageType.None);
                EditorGUILayout.Space(6);
                c.automaticVersion = EditorGUILayout.Toggle("自动生成资源版本", c.automaticVersion);
                using (new EditorGUI.DisabledScope(c.automaticVersion))
                    c.resourceVersion = EditorGUILayout.TextField("资源版本号", c.resourceVersion);
                c.buildOutputRoot = FolderField("资源构建输出目录", c.buildOutputRoot);
                c.compression = (ECompressOption)EditorGUILayout.EnumPopup("资源压缩", c.compression);
                c.aotBaselineDirectory = FolderField("主包 AOT 目录", c.aotBaselineDirectory);
                EditorGUILayout.HelpBox("选择当前客户端构建时保存的 BackUp…/Managed 目录或其固定备份。每次发布复用该主包基线，自动分析并同步所需的补充元数据；发新主包后再切换基线。", MessageType.None);
                c.note = EditorGUILayout.TextField("版本备注", c.note);
                if (EditorGUI.EndChangeCheck()) settings.SaveSettings();

                EditorGUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("检查配置", GUILayout.Height(30)))
                    {
                        try
                        {
                            var snapshot = Snapshot();
                            if (snapshot.automaticVersion) snapshot.resourceVersion = HotUpdatePublisherPipeline.NewVersion();
                            HotUpdatePublisherPipeline.Validate(snapshot, EditorUserBuildSettings.activeBuildTarget, true);
                            SetStatus("配置检查通过。执行时还会编译并检查新 DLL 与主包的兼容性。", MessageType.Info);
                        }
                        catch (Exception e) { SetStatus(e.Message, MessageType.Error); }
                    }
                    if (GUILayout.Button("仅构建", GUILayout.Height(30))) Schedule(false);
                    if (GUILayout.Button("构建并发布", GUILayout.Height(30))) Schedule(true);
                }
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(_status, _statusType);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("打开 CDN 目录"))
                {
                    try { RevealExisting(HotUpdatePublisherPipeline.GetPublishDirectory(c, EditorUserBuildSettings.activeBuildTarget)); }
                    catch (Exception e) { SetStatus(e.Message, MessageType.Error); }
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_lastOutput)))
                    if (GUILayout.Button("打开构建结果")) RevealExisting(_lastOutput);
                if (GUILayout.Button("复制日志")) EditorGUIUtility.systemCopyBuffer = string.Join("\n", _logs);
            }
            if (_logs.Count > 0)
                EditorGUILayout.TextArea(string.Join("\n", _logs), GUILayout.MinHeight(100));
            EditorGUILayout.EndScrollView();
        }

        private static string FolderField(string label, string path)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                path = EditorGUILayout.TextField(label, path);
                if (GUILayout.Button("选择", GUILayout.Width(48)))
                {
                    string selected = EditorUtility.OpenFolderPanel(label, Directory.Exists(path) ? path : HotUpdatePublisherPipeline.ProjectRoot, "");
                    if (!string.IsNullOrEmpty(selected)) path = selected;
                }
            }
            return path;
        }

        private static void RevealExisting(string path)
        {
            string existing = path;
            while (!string.IsNullOrEmpty(existing) && !Directory.Exists(existing))
                existing = Path.GetDirectoryName(existing);
            if (!string.IsNullOrEmpty(existing)) EditorUtility.RevealInFinder(existing);
        }

        private static HotUpdatePublishConfig Snapshot()
        {
            return JsonUtility.FromJson<HotUpdatePublishConfig>(JsonUtility.ToJson(HotUpdatePublisherSettings.instance.config));
        }

        private void Schedule(bool publish)
        {
            var c = Snapshot();
            if (c.automaticVersion) c.resourceVersion = HotUpdatePublisherPipeline.NewVersion();
            var target = EditorUserBuildSettings.activeBuildTarget;
            bool development = EditorUserBuildSettings.development;
            _scheduled = true;
            _logs.Clear();
            SetStatus("准备执行，资源版本：" + c.resourceVersion, MessageType.Info);
            EditorApplication.delayCall += () =>
            {
                try
                {
                    _lastOutput = HotUpdatePublisherPipeline.Run(c, target, development, publish, Log, true);
                    HotUpdatePublisherSettings.instance.config.resourceVersion = c.resourceVersion;
                    HotUpdatePublisherSettings.instance.SaveSettings();
                    SetStatus(publish ? "发布成功：" + c.resourceVersion + "。客户端下次启动将检测更新。" :
                        "构建完成：" + c.resourceVersion + "。尚未发布到 CDN。", MessageType.Info);
                }
                catch (OperationCanceledException) { SetStatus("已取消，CDN 版本号未切换。", MessageType.Warning); }
                catch (Exception e)
                {
                    Log(e.ToString());
                    Debug.LogException(e);
                    SetStatus("执行失败：" + e.Message, MessageType.Error);
                }
                finally
                {
                    _scheduled = false;
                    if (this != null) Repaint();
                }
            };
        }

        private void Log(string message)
        {
            _logs.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            if (_logs.Count > 150) _logs.RemoveAt(0);
            if (this != null) Repaint();
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
            Repaint();
        }
    }
}
