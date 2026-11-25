using UnityEngine;
using UnityGameFramework.Runtime;

namespace GameBase
{
    public class Singleton<T> where  T : new()
    {
        private static T _instance;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new T();
                    Log.Assert(_instance != null, "_instance != null");
                }
                return _instance;
            }
        }
    }
}

