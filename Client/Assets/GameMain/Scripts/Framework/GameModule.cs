using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 游戏模块统一入口。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(YooResourceComponent))]
public class GameModule : MonoBehaviour
{
    #region BaseComponents

    /// <summary>获取游戏基础组件。</summary>
    public static BaseComponent Base { get; private set; }

    /// <summary>获取调试组件。</summary>
    public static DebuggerComponent Debugger { get; private set; }

    /// <summary>获取下载组件。</summary>
    public static DownloadComponent Download { get; private set; }

    /// <summary>获取实体组件。</summary>
    public static EntityComponent Entity { get; private set; }

    /// <summary>获取事件组件。</summary>
    public static EventComponent Event { get; private set; }

    /// <summary>获取文件系统组件。</summary>
    public static FileSystemComponent FileSystem { get; private set; }

    /// <summary>获取有限状态机组件。</summary>
    public static FsmComponent Fsm { get; private set; }

    /// <summary>获取本地化组件。</summary>
    public static LocalizationComponent Localization { get; private set; }

    /// <summary>获取网络组件。</summary>
    public static NetworkComponent Network { get; private set; }

    /// <summary>获取对象池组件。</summary>
    public static ObjectPoolComponent ObjectPool { get; private set; }

    /// <summary>获取流程组件。</summary>
    public static ProcedureComponent Procedure { get; private set; }

    /// <summary>获取 YooAsset 资源组件。</summary>
    public static YooResourceComponent Resource { get; private set; }

    /// <summary>获取场景组件。</summary>
    public static SceneComponent Scene { get; private set; }

    /// <summary>获取配置组件。</summary>
    public static SettingComponent Setting { get; private set; }

    /// <summary>获取声音组件。</summary>
    public static SoundComponent Sound { get; private set; }

    /// <summary>获取界面组件。</summary>
    public static UIComponent UI { get; private set; }

    /// <summary>获取 Web 请求组件。</summary>
    public static WebRequestComponent WebRequest { get; private set; }

    /// <summary>获取临时数据管理组件。</summary>
    public static DataNodeComponent DataNode { get; private set; }

    #endregion

    private static readonly Dictionary<Type, GameFrameworkComponent> s_Components = new Dictionary<Type, GameFrameworkComponent>();
    private static GameModule s_Instance;

    /// <summary>
    /// 初始化系统框架模块，可重复调用。
    /// </summary>
    public static void InitFrameWorkComponents()
    {
        Base = Get<BaseComponent>();
        Debugger = Get<DebuggerComponent>();
        Download = Get<DownloadComponent>();
        Entity = Get<EntityComponent>();
        Event = Get<EventComponent>();
        FileSystem = Get<FileSystemComponent>();
        Fsm = Get<FsmComponent>();
        Localization = Get<LocalizationComponent>();
        Network = Get<NetworkComponent>();
        ObjectPool = Get<ObjectPoolComponent>();
        Procedure = Get<ProcedureComponent>();
        Resource = Get<YooResourceComponent>();
        Scene = Get<SceneComponent>();
        Setting = Get<SettingComponent>();
        Sound = Get<SoundComponent>();
        UI = Get<UIComponent>();
        WebRequest = Get<WebRequestComponent>();
        DataNode = Get<DataNodeComponent>();
    }

    public static void InitCustomComponents()
    {
    }

    public static T Get<T>() where T : GameFrameworkComponent
    {
        Type type = typeof(T);
        if (s_Components.TryGetValue(type, out GameFrameworkComponent component) && component != null)
        {
            return (T)component;
        }

        component = GameEntry.GetComponent<T>();
        Log.Assert(component != null, $"{typeof(T)} is null");
        if (component != null)
        {
            s_Components[type] = component;
        }

        return (T)component;
    }

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Log.Warning("GameModule already exists. The duplicate component will be removed.");
            enabled = false;
            Destroy(this);
            return;
        }

        s_Instance = this;
    }

    private void Start()
    {
        if (s_Instance == this)
        {
            Init();
        }
    }

    private void Init()
    {
        InitFrameWorkComponents();
        InitCustomComponents();
    }

    private void OnDestroy()
    {
        if (s_Instance != this)
        {
            return;
        }

        s_Instance = null;
        s_Components.Clear();
        Base = null;
        Debugger = null;
        Download = null;
        Entity = null;
        Event = null;
        FileSystem = null;
        Fsm = null;
        Localization = null;
        Network = null;
        ObjectPool = null;
        Procedure = null;
        Resource = null;
        Scene = null;
        Setting = null;
        Sound = null;
        UI = null;
        WebRequest = null;
        DataNode = null;
    }

    public static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
        Application.Quit();
    }
}
