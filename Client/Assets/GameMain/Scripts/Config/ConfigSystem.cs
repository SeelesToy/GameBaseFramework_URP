using cfg;
using GameBase;
using GameFramework;
using Luban;
using UnityEngine;

// 配置加载器单例
public class ConfigSystem : Singleton<ConfigSystem>
{
    private bool _init = false;

    private Tables _tables;

    public Tables Tables
    {
        get
        {
            if (!_init)
            {
                Load();
            }

            return _tables;
        }
    }
    
    /// <summary>
    /// 加载配置。
    /// </summary>
    public void Load()
    {
        _tables = new Tables(LoadByteBuf);
        _init = true;
    }
    
    /// <summary>
    /// 加载二进制配置。
    /// </summary>
    /// <param name="file">FileName</param>
    /// <returns>ByteBuf</returns>
    private ByteBuf LoadByteBuf(string file)
    {
        TextAsset textAsset = GameModule.Resource.LoadAsset<TextAsset>(file);
        if (textAsset == null)
            throw new GameFrameworkException($"LoadByteBuf failed: {file}");

        try
        {
            // TextAsset.bytes 返回托管副本，释放资源后 ByteBuf 仍可安全读取。
            return new ByteBuf(textAsset.bytes);
        }
        finally
        {
            GameModule.Resource.UnloadAsset(textAsset);
        }
    }
}