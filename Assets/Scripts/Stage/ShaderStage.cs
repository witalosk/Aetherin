using System;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    /// <summary>
    /// 1枚シェーダの結果を出力するステージ
    /// </summary>
    public class ShaderStage : StageBase
    {
        [SerializeField] private Shader _shader;
        
        private Material _material;
        private IAudioFeatureProvider _audioFeatureProvider;
        private IColorPaletteManager _colorPaletteManager;

        [Inject]
        private void Construct(IAudioFeatureProvider audioFeatureProvider, IColorPaletteManager colorPaletteManager)
        {
            _audioFeatureProvider = audioFeatureProvider;
            _colorPaletteManager = colorPaletteManager;
        }

        protected override void Start()
        {
            base.Start();
            if (_shader == null) return;
            _material = new Material(_shader);
        }

        private void Update()
        {
            if (_material == null || OutputTexture == null) return;
            _material.SetTexture("_WaveTex", _audioFeatureProvider.WaveformTexture);
            _colorPaletteManager.SetToMaterial(_material);
            Graphics.Blit(null, OutputTexture, _material);
        }
    }
}
