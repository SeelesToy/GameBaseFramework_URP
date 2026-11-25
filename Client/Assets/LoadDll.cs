using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HybridCLR;
using UnityEngine;
using YooAsset;

public static class LoadDll
{
    // YooAsset 的资源定位路径，不是磁盘绝对路径。
    private const string CodeRoot = "Assets/GameMain/Res/Code/";

    private static Assembly _loadedAssembly;

    public static Task<Assembly> LoadAsync(ResourcePackage package)
    {
        // 避免同一次启动中重复加载。
        if (_loadedAssembly != null)
            return Task.FromResult(_loadedAssembly);

#if UNITY_EDITOR
        // 编辑器已经编译并加载了业务程序集。
        _loadedAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "HotUpdate");

        if (_loadedAssembly == null)
            throw new Exception("找不到 HotUpdate 程序集，请检查编译错误。");

        return Task.FromResult(_loadedAssembly);
#else
        return LoadPlayerAsync(package);
#endif
    }

    private static async Task<Assembly> LoadPlayerAsync(ResourcePackage package)
    {
        // 1. 读取需要补充元数据的程序集名单。
        byte[] listBytes = await LoadBytesAsync(package, CodeRoot + "AOTAssemblyList.txt");

        string listText = Encoding.UTF8.GetString(listBytes).TrimStart('\uFEFF');

        string[] dllNames = listText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // 2. 补充 AOT 元数据。
        foreach (string line in dllNames)
        {
            string dllName = line.Trim();
            if (dllName.Length == 0) continue;

            byte[] metadataBytes = await LoadBytesAsync(package, CodeRoot + "AOT/" + dllName + ".bytes");

            var result = RuntimeApi.LoadMetadataForAOTAssembly(metadataBytes, HomologousImageMode.SuperSet);

            // 允许失败后重试时，遇到此前已成功加载的元数据。
            if (result != LoadImageErrorCode.OK && result != LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED)
            {
                throw new Exception($"AOT 元数据加载失败：{dllName}，错误：{result}");
            }
        }

        // 3. 读取并加载业务 DLL。
        byte[] hotDllBytes = await LoadBytesAsync(package, CodeRoot + "HotUpdate.dll.bytes");

        _loadedAssembly = Assembly.Load(hotDllBytes);

        Debug.Log($"代码加载完成：{_loadedAssembly.GetName().Name}"); 
        return _loadedAssembly;
    }

    private static async Task<byte[]> LoadBytesAsync(ResourcePackage package, string location)
    {
        var handle = package.LoadAssetAsync<TextAsset>(location);

        try
        {
            await handle;

            // await 完成不等于加载成功，需要检查状态。
            if (handle.Status != EOperationStatus.Succeeded) throw new Exception($"资源加载失败：{location}\n{handle.Error}");

            var asset = handle.AssetObject as TextAsset;
            if (asset == null)
                throw new Exception($"资源不是 TextAsset：{location}");

            return asset.bytes;
        }
        finally
        {
            handle.Release();
        }
    }
}