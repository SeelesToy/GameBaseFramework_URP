using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

public sealed class GameBoot : MonoBehaviour
{
    private static GameBoot _instance;
    
    
    [SerializeField]
    private EPlayMode playMode = EPlayMode.EditorSimulateMode;

    [SerializeField]
    private string packageName = "TurnBasePackage";

    [SerializeField]
    private string launchScene = "Assets/GameMain/Res/Scenes/Launch.unity";

    [SerializeField]
    private GameObject patchWindowPrefab;

    private GameObject _patchWindow;
    private bool _servicesCreated;
    private bool _updating;
    private bool _enteringGame;

    private void Awake()
    {
        // 避免重复进入启动场景时创建第二套服务。
        if (_instance != null && _instance != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        Application.targetFrameRate = 60;
        Application.runInBackground = true;
    }

    private void Start()
    {
        if (patchWindowPrefab == null)
        {
            Debug.LogError("GameBootstrap 未配置更新界面预制体。");
            return;
        }

        // 更新界面会订阅事件，因此先创建事件服务。
        EventManager.Instance.Create();

        _servicesCreated = true;

        if (!YooAssets.IsInitialized)
            YooAssets.Initialize();

        // 挂在常驻启动对象下，进入 Launch 完成后再关闭。
        _patchWindow = Instantiate(patchWindowPrefab, transform);

#if UNITY_EDITOR
        var actualPlayMode = playMode;
#else
        var actualPlayMode = EPlayMode.HostPlayMode;
#endif
        PatchManager.Instance.Create(packageName, actualPlayMode, this);
        PatchManager.Instance.Completed += OnPatchCompleted;

        _updating = true;
        PatchManager.Instance.Start();
    }

    private void Update()
    {
        if (_updating)
            PatchManager.Instance.Update();
    }

    private async void OnPatchCompleted()
    {
        if (_enteringGame)
            return;

        _updating = false;
        _enteringGame = true;
        
        try
        {
            PatchStepChangedEvent.SendEventMessage("正在加载游戏代码……");
            var package = YooAssets.GetPackage(packageName);
            await LoadDll.LoadAsync(package);
            if (this == null)
                return;
            StartCoroutine(LoadLaunchScene());
        }
        catch (System.Exception exception)
        {
            if (this == null)
                return;
            
            _enteringGame = false;
            Debug.LogException(exception);
            PatchStepChangedEvent.SendEventMessage("代码加载失败，请检查日志。");
        }
    }

    private IEnumerator LoadLaunchScene()
    {
        PatchStepChangedEvent.SendEventMessage("正在进入游戏……");

        var package = YooAssets.GetPackage(packageName);
        var handle = package.LoadSceneAsync(launchScene, LoadSceneMode.Single);

        yield return handle;

        if (handle.Status != EOperationStatus.Succeeded)
        {
            _enteringGame = false;

            Debug.LogError($"进入场景 {launchScene} 失败：{handle.Error}");
            PatchStepChangedEvent.SendEventMessage("进入游戏失败，请检查日志。");
            yield break;
        }

        if (_patchWindow != null)
        {
            Destroy(_patchWindow);
            _patchWindow = null;
        }

        Debug.Log($"场景 {launchScene} 已加载，交由 GameFramework 启动业务流程。");
    }

    private void OnDestroy()
    {
        // 重复实例销毁时，不能清理正常实例的服务。
        if (_instance != this)
            return;

        _updating = false;
        StopAllCoroutines();

        if (_servicesCreated)
        {
            PatchManager.Instance.Completed -= OnPatchCompleted;
            PatchManager.Instance.Destroy();
            EventManager.Instance.Destroy();
        }

        _instance = null;
    }
}