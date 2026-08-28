using System;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// 1枚シェーダの結果を出力するステージ
    /// </summary>
    public class ShaderStage : StageBase
    {
        [SerializeField] private Shader _shader;
        
        private Material _material;

        private void Start()
        {
            if (_shader == null) return;
            _material = new Material(_shader);
        }

        private void Update()
        {
            if (_material == null || OutputTexture == null) return;
            Graphics.Blit(null, OutputTexture, _material);
        }
    }
}