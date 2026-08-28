using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    public class StageManager : MonoBehaviour
    {
        [SerializeField] private Renderer _currentRenderer;
        [SerializeField] private Renderer _nextRenderer;
        [Space]
        [SerializeField] private List<StageBase> _stages;
        
        private void Update()
        {
            _currentRenderer.sharedMaterial.SetTexture("_MainTex", _stages[0].OutputTexture);
        }
    }
}
