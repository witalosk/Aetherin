using System;
using RosettaUI;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public class SceneManagerParams : IParams
    {
        public Vector2Int Resolution = new(3840, 2160);
    }
    
    public class SceneManager : MonoBehaviour, ISaveAndUiTarget
    {
        public Vector2Int Resolution => _params.Resolution;
        public IParams Params => _params;
        
        [SerializeField] private SceneManagerParams _params = new();
    }
}
