using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;
using UnityGameFramework.Runtime;
using YooAsset;
using Object = UnityEngine.Object;

/// <summary>
/// GameModule 的 YooAsset 资源组件。资源包由 GameBoot 完成更新后提供，本组件不重复初始化。
/// 所有接口在 Unity 主线程调用；每次成功加载均须对应一次 UnloadAsset。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Game Framework/Yoo Resource")]
public sealed class YooResourceComponent : GameFrameworkComponent
{
    /// <summary>默认资源包名称，与 GameBoot 的 packageName 保持一致。</summary>
    public string PackageName = "TurnBasePackage";

    private readonly Dictionary<Object, Stack<AssetHandle>> m_AssetHandles =
        new Dictionary<Object, Stack<AssetHandle>>();
    private readonly HashSet<AssetHandle> m_Handles = new HashSet<AssetHandle>();
    private readonly CancellationTokenSource m_DestroyCancellation = new CancellationTokenSource();
    private bool m_Destroyed;

    /// <summary>默认资源包是否已完成初始化。</summary>
    public bool IsInitialized => !m_Destroyed && YooAssets.IsInitialized
        && YooAssets.TryGetPackage(PackageName, out var package)
        && package.InitializeStatus == EOperationStatus.Succeeded;

    /// <summary>默认资源包版本。</summary>
    public string PackageVersion => GetPackage().GetPackageVersion();

    /// <summary>同步加载资源。资源加载失败时记录错误并返回 null。</summary>
    public T LoadAsset<T>(string location, string packageName = "") where T : Object
    {
        if (!ValidateLocation(location))
            return null;

        var handle = GetPackage(packageName).LoadAssetSync<T>(location);
        m_Handles.Add(handle);
        bool retained = false;
        try
        {
            var asset = RetainAsset<T>(handle, location);
            retained = asset != null;
            return asset;
        }
        finally
        {
            if (!retained)
                ReleaseHandle(handle);
        }
    }

    /// <summary>
    /// 异步加载资源。调用格式与 cardv 一致；取消或加载失败返回 null。
    /// 成功返回后由调用者通过 UnloadAsset 释放。
    /// </summary>
    public async UniTask<T> LoadAssetAsync<T>(string location,
        CancellationToken cancellationToken = default, string packageName = "") where T : Object
    {
        if (!ValidateLocation(location) || cancellationToken.IsCancellationRequested)
            return null;

        var handle = GetPackage(packageName).LoadAssetAsync<T>(location);
        m_Handles.Add(handle);
        bool retained = false;
        using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, m_DestroyCancellation.Token))
        {
            try
            {
                bool canceled = await UniTask.WaitUntil(() => handle.IsDone,
                    cancellationToken: linkedCancellation.Token).SuppressCancellationThrow();
                if (canceled || linkedCancellation.IsCancellationRequested || m_Destroyed)
                    return null;

                var asset = RetainAsset<T>(handle, location);
                retained = asset != null;
                return asset;
            }
            finally
            {
                if (!retained)
                    ReleaseHandle(handle);
            }
        }
    }

    /// <summary>回调式异步加载。失败时回调 null；成功后由回调接收者释放资源。</summary>
    public void LoadAsset<T>(string location, Action<T> callback,
        string customPackageName = "") where T : Object
    {
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        LoadAssetWithCallback(location, callback, customPackageName).Forget();
    }

    /// <summary>卸载一次加载引用。同一资源加载多次时，需要分别卸载。</summary>
    public void UnloadAsset(object asset)
    {
        var unityAsset = asset as Object;
        if (ReferenceEquals(unityAsset, null))
            return;

        if (!m_AssetHandles.TryGetValue(unityAsset, out var handles))
        {
            if (!m_Destroyed)
                Log.Warning("UnloadAsset ignored: asset was not loaded by GameModule.Resource.");
            return;
        }

        var handle = handles.Pop();
        if (handles.Count == 0)
            m_AssetHandles.Remove(unityAsset);
        ReleaseHandle(handle);
    }

    /// <summary>获取资源信息；可通过 IsValid 判断地址是否有效。</summary>
    public AssetInfo GetAssetInfo(string location, string packageName = "")
    {
        return GetPackage(packageName).GetAssetInfo(location);
    }

    public AssetInfo[] GetAssetInfos(string tag, string packageName = "")
    {
        return GetPackage(packageName).GetAssetInfos(tag);
    }

    public AssetInfo[] GetAssetInfos(string[] tags, string packageName = "")
    {
        return GetPackage(packageName).GetAssetInfos(tags);
    }

    public bool CheckLocationValid(string location, string packageName = "")
    {
        return !string.IsNullOrEmpty(location) && GetAssetInfo(location, packageName).IsValid;
    }

    private async UniTask LoadAssetWithCallback<T>(string location, Action<T> callback,
        string packageName) where T : Object
    {
        T asset = null;
        try
        {
            asset = await LoadAssetAsync<T>(location, packageName: packageName);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }

        // 销毁后不再向旧场景的业务对象回调。
        if (!m_Destroyed)
            callback(asset);
    }

    private T RetainAsset<T>(AssetHandle handle, string location) where T : Object
    {
        if (!handle.IsValid || handle.Status != EOperationStatus.Succeeded)
        {
            Log.Error("LoadAsset failed: '{0}'. {1}", location,
                handle.IsValid ? handle.Error : "Resource handle is no longer valid.");
            return null;
        }

        var asset = handle.AssetObject as T;
        if (asset == null)
        {
            Log.Error("LoadAsset type mismatch: '{0}', expected {1}.", location, typeof(T).FullName);
            return null;
        }

        if (!m_AssetHandles.TryGetValue(asset, out var handles))
        {
            handles = new Stack<AssetHandle>();
            m_AssetHandles.Add(asset, handles);
        }
        handles.Push(handle);
        return asset;
    }

    private ResourcePackage GetPackage(string packageName = "")
    {
        if (m_Destroyed)
            throw new ObjectDisposedException(nameof(YooResourceComponent));

        string resolvedName = string.IsNullOrEmpty(packageName) ? PackageName : packageName;
        if (!YooAssets.IsInitialized
            || !YooAssets.TryGetPackage(resolvedName, out var package)
            || package.InitializeStatus != EOperationStatus.Succeeded)
        {
            throw new GameFrameworkException(
                $"Resource package '{resolvedName}' is not ready. Start the game through GameBoot.");
        }
        return package;
    }

    private static bool ValidateLocation(string location)
    {
        if (!string.IsNullOrEmpty(location))
            return true;
        Log.Error("Asset name is invalid.");
        return false;
    }

    private void ReleaseHandle(AssetHandle handle)
    {
        // 销毁、取消和失败路径可能交汇，每个句柄只释放一次。
        if (m_Handles.Remove(handle) && handle.IsValid)
            handle.Release();
    }

    private void OnDestroy()
    {
        m_Destroyed = true;
        m_DestroyCancellation.Cancel();

        foreach (var handle in m_Handles)
        {
            if (handle.IsValid)
                handle.Release();
        }
        m_Handles.Clear();
        m_AssetHandles.Clear();
        m_DestroyCancellation.Dispose();
    }
}
