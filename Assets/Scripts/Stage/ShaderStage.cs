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

        [Inject]
        private void Construct(IAudioFeatureProvider audioFeatureProvider)
        {
            _audioFeatureProvider = audioFeatureProvider;
        }

        protected override void Start()
        {
            base.Start();
            if (_shader == null) return;
            _material = new Material(_shader);
        }

        protected override void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            base.OnDestroy();
        }

        private void Update()
        {
            if (_material == null || OutputTexture == null) return;
            _material.SetTexture("_WaveTex", _audioFeatureProvider.WaveformTexture);
            _deckStateProvider.GetState(Deck).Palette?.ApplyToMaterial(_material);
            Graphics.Blit(null, OutputTexture, _material);
        }
    }
}
