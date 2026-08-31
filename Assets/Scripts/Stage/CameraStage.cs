using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// カメラで撮ったシーンをそのまま出力するステージ
    /// カメラと被写体はこのオブジェクトの子に置く想定
    /// (Nextとして複製されたときはStageManagerがワールドオフセットを加えるため、複製元と互いに映り込まない)
    /// </summary>
    public class CameraStage : StageBase
    {
        public IReadOnlyList<StageLayer> Layers => _layers;

        [SerializeField] private Camera _camera;
        private StageLayer[] _layers = Array.Empty<StageLayer>();

        protected override void Start()
        {
            base.Start();

            if (_camera == null) _camera = GetComponentInChildren<Camera>();
            if (_camera == null)
            {
                Debug.LogError($"[CameraStage] {name} にカメラが設定されていません", this);
                return;
            }

            _camera.targetTexture = OutputTexture;
            RefreshLayers();
        }

        /// <summary>子にあるレイヤーを、非アクティブなものも含めて描画順に収集する。</summary>
        public void RefreshLayers()
        {
            _layers = GetComponentsInChildren<StageLayer>(true);
            Array.Sort(_layers, (a, b) => a.Order.CompareTo(b.Order));
        }

        public void SetLayerVisible(int index, bool visible)
        {
            if (index < 0 || index >= _layers.Length) return;
            _layers[index].Visible = visible;
        }

        protected override void OnDestroy()
        {
            if (_camera != null) _camera.targetTexture = null;
            base.OnDestroy();
        }
    }
}
