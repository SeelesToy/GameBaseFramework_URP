using System;
using Framework.Machine;
using UnityEngine;
using YooAsset;

/// <summary>
/// Shared data for the patch workflow.
/// </summary>
internal sealed class PatchContext
{
    public string PackageName { get; }
    public EPlayMode PlayMode { get; }
    // 负责实际执行协程的 MonoBehaviour。
    public MonoBehaviour CoroutineRunner { get; }
    public string PackageVersion { get; set; }
    public ResourceDownloaderOperation Downloader { get; set; }
    public FlowMachine<PatchState, PatchContext> Machine { get; set; }

    public PatchContext(string packageName, EPlayMode playMode, MonoBehaviour coroutineRunner)
    {
        if(coroutineRunner == null)
            throw new ArgumentNullException(nameof(coroutineRunner));
        PackageName = packageName;
        PlayMode = playMode;
        CoroutineRunner = coroutineRunner;
    }
}
