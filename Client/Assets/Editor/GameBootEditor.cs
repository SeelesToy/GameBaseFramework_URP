using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

[CustomEditor(typeof(GameBoot))]
[CanEditMultipleObjects]
public sealed class GameBootEditor : Editor
{
    private SerializedProperty _packageName;
    private SerializedProperty _launchScene;
    private BundleCollectorSetting _settings;
    private readonly List<string> _scenePaths = new List<string>();
    private readonly List<GUIContent> _sceneLabels = new List<GUIContent>();
    private string _cachedPackageName;
    private string _cachedSettingsJson;
    private string _settingsError;
    private string _sceneError;

    private void OnEnable()
    {
        _packageName = serializedObject.FindProperty("packageName");
        _launchScene = serializedObject.FindProperty("launchScene");
        EditorApplication.projectChanged += InvalidateChoices;
        Undo.undoRedoPerformed += InvalidateChoices;
    }

    private void OnDisable()
    {
        EditorApplication.projectChanged -= InvalidateChoices;
        Undo.undoRedoPerformed -= InvalidateChoices;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        DrawPropertiesExcluding(serializedObject, "m_Script", "packageName", "launchScene");

        LoadSettings();
        var packageNames = new List<string>();
        var packageLabels = new List<GUIContent>();
        if (_settings != null)
        {
            foreach (var package in _settings.Packages)
            {
                if (string.IsNullOrEmpty(package.PackageName) || packageNames.Contains(package.PackageName))
                    continue;
                packageNames.Add(package.PackageName);
                packageLabels.Add(new GUIContent(package.PackageName, package.PackageDesc));
            }
        }

        if (DrawChoice(_packageName, new GUIContent("Package Name", "YooAsset 收集配置中的资源包。"),
            packageNames, packageLabels, "请选择资源包", "未找到资源包"))
        {
            // A new package requires an explicit scene selection; never retain another package's scene.
            _launchScene.stringValue = string.Empty;
        }

        bool hasPackage = !_packageName.hasMultipleDifferentValues && packageNames.Contains(_packageName.stringValue);
        RefreshSceneChoices(hasPackage);
        DrawChoice(_launchScene, new GUIContent("Launch Scene", "所选 YooAsset 资源包中可直接加载的场景。"),
            _scenePaths, _sceneLabels, "请选择启动场景", "未收集到当前包");

        if (!string.IsNullOrEmpty(_settingsError))
            EditorGUILayout.HelpBox(_settingsError, MessageType.Error);
        else if (_settings == null || packageNames.Count == 0)
            EditorGUILayout.HelpBox("未找到 YooAsset 资源包，请先在资源收集配置中创建包。", MessageType.Warning);
        else if (_packageName.hasMultipleDifferentValues)
            EditorGUILayout.HelpBox("所选对象的资源包不同，请先选择相同的 Package Name，再选择启动场景。", MessageType.Info);
        else if (!hasPackage)
            EditorGUILayout.HelpBox("当前资源包不在 YooAsset 收集配置中，请重新选择 Package Name。", MessageType.Warning);
        else if (!string.IsNullOrEmpty(_sceneError))
            EditorGUILayout.HelpBox("读取资源包场景失败：" + _sceneError, MessageType.Error);
        else if (_scenePaths.Count == 0)
            EditorGUILayout.HelpBox("当前资源包没有可直接加载的场景，请检查场景收集路径、过滤规则和分组是否启用。", MessageType.Warning);
        else if (!_launchScene.hasMultipleDifferentValues && !_scenePaths.Contains(_launchScene.stringValue))
            EditorGUILayout.HelpBox("请从当前资源包的场景列表中选择 Launch Scene。", MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("资源收集配置"))
                BundleCollectorWindow.OpenWindow();
            if (GUILayout.Button("刷新列表"))
                InvalidateChoices();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void LoadSettings()
    {
        _settingsError = null;
        try
        {
            // Do not create a settings asset merely by inspecting GameBoot.
            if (_settings == null && BundleCollectorSettingData.HasSettingAsset())
                _settings = BundleCollectorSettingData.Setting;
        }
        catch (Exception exception)
        {
            _settingsError = "读取 YooAsset 收集配置失败：" + exception.Message;
        }
    }

    private void RefreshSceneChoices(bool hasPackage)
    {
        if (!hasPackage || _settings == null)
        {
            _scenePaths.Clear();
            _sceneLabels.Clear();
            _cachedSettingsJson = null;
            _sceneError = null;
            return;
        }

        // The collector window also edits settings in memory before saving them.
        // Compare their serialized content, but collect assets only when it changes.
        var settingsJson = EditorJsonUtility.ToJson(_settings);
        var packageName = _packageName.stringValue;
        if (_cachedPackageName == packageName && _cachedSettingsJson == settingsJson)
            return;

        _cachedPackageName = packageName;
        _cachedSettingsJson = settingsJson;
        _scenePaths.Clear();
        _sceneLabels.Clear();
        _sceneError = null;

        try
        {
            // Simulation collection applies YooAsset's active, ignore and filter rules
            // without building bundles or traversing dependencies/static collectors.
            var result = _settings.BeginCollect(packageName, true, false, true);
            foreach (var asset in result.CollectAssets)
            {
                if (asset.CollectorType == ECollectorType.MainAssetCollector &&
                    asset.AssetInfo.AssetType == typeof(SceneAsset))
                    _scenePaths.Add(asset.AssetInfo.AssetPath);
            }

            _scenePaths.Sort(StringComparer.Ordinal);
            foreach (var path in _scenePaths)
            {
                // Include the directory to distinguish scenes that share a filename.
                var directory = Path.GetDirectoryName(path).Replace('\\', '/').Replace("/", " › ");
                _sceneLabels.Add(new GUIContent($"{Path.GetFileNameWithoutExtension(path)} — {directory}", path));
            }
        }
        catch (Exception exception)
        {
            _scenePaths.Clear();
            _sceneLabels.Clear();
            _sceneError = exception.Message;
        }
    }

    private static bool DrawChoice(SerializedProperty property, GUIContent label,
        List<string> values, List<GUIContent> valueLabels, string placeholder, string missingLabel)
    {
        int selectedIndex = values.IndexOf(property.stringValue) + 1;
        var labels = new List<GUIContent> { new GUIContent(placeholder) };
        labels.AddRange(valueLabels);
        if (selectedIndex == 0 && !property.hasMultipleDifferentValues && !string.IsNullOrEmpty(property.stringValue))
            labels[0] = new GUIContent($"{missingLabel}：{property.stringValue}");

        var rect = EditorGUILayout.GetControlRect();
        EditorGUI.BeginProperty(rect, label, property);
        bool previousMixedValue = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
        bool changed;
        using (new EditorGUI.DisabledScope(values.Count == 0))
        {
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUI.Popup(rect, label, selectedIndex, labels.ToArray());
            changed = EditorGUI.EndChangeCheck();
            if (changed)
                property.stringValue = newIndex > 0 ? values[newIndex - 1] : string.Empty;
        }
        EditorGUI.showMixedValue = previousMixedValue;
        EditorGUI.EndProperty();
        return changed;
    }

    private void InvalidateChoices()
    {
        _settings = null;
        _cachedSettingsJson = null;
        Repaint();
    }
}
