using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnitySimpleContainer;

namespace Aetherin
{
    /// <summary>
    /// カメラで撮ったシーンをそのまま出力するステージ
    /// カメラと被写体はこのオブジェクトの子に置く想定
    /// (Nextとして複製されたときはStageManagerがワールドオフセットを加えるため、複製元と互いに映り込まない)
    /// </summary>
    public class CameraStage : StageBase
    {
        public override IReadOnlyList<StageLayer> Layers => _layers;
        public int LayerRevision { get; private set; }

        [SerializeField] private Camera _camera;
        private StageLayer[] _layers = Array.Empty<StageLayer>();
        private IAudioFeatureProvider _audioFeatureProvider;
        private IBeatManager _beatManager;

        [Inject]
        private void ConstructLayers(IAudioFeatureProvider audioFeatureProvider, IBeatManager beatManager)
        {
            _audioFeatureProvider = audioFeatureProvider;
            _beatManager = beatManager;
        }

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
            _camera.GetUniversalAdditionalCameraData().requiresColorOption = CameraOverrideOption.On;
            RefreshLayers();
        }

        private void Update()
        {
            _camera.backgroundColor = _deckStateProvider.GetState(Deck).Palette?.BackgroundColor1 ?? Color.black;
        }

        /// <summary>子にあるレイヤーを、非アクティブなものも含めて描画順に収集する。</summary>
        public void RefreshLayers()
        {
            _layers = GetComponentsInChildren<StageLayer>(true)
                .Where(layer => layer != null && layer.gameObject.activeSelf)
                .ToArray();
            Array.Sort(_layers, (a, b) => a.Order.CompareTo(b.Order));
            LayerRevision++;
        }

        public void SetLayerVisible(int index, bool visible)
        {
            if (index < 0 || index >= _layers.Length) return;
            _layers[index].Visible = visible;
        }

        public ShapeLayer AddShapeLayer()
        {
            var layerObject = new GameObject("Shape Layer");
            layerObject.transform.SetParent(transform, false);
            layerObject.AddComponent<MeshFilter>();
            layerObject.AddComponent<MeshRenderer>();
            var layer = layerObject.AddComponent<ShapeLayer>();
            layer.Initialize(_audioFeatureProvider, _beatManager, _deckStateProvider);
            layer.Order = _layers.Length == 0 ? 0 : _layers.Max(existing => existing.Order) + 1;
            RefreshLayers();
            return layer;
        }

        public Primitive3DLayer AddPrimitive3DLayer()
        {
            var layerObject = new GameObject("Primitive 3D Layer");
            layerObject.transform.SetParent(transform, false);
            layerObject.AddComponent<MeshFilter>();
            layerObject.AddComponent<MeshRenderer>();
            var layer = layerObject.AddComponent<Primitive3DLayer>();
            layer.Initialize(_audioFeatureProvider, _beatManager, _deckStateProvider);
            layer.Order = _layers.Length == 0 ? 0 : _layers.Max(existing => existing.Order) + 1;
            RefreshLayers();
            return layer;
        }

        public GpuParticleLayer AddGpuParticleLayer()
        {
            var layerObject = new GameObject("GPU Particle Layer");
            layerObject.transform.SetParent(transform, false);
            var layer = layerObject.AddComponent<GpuParticleLayer>();
            layer.Initialize(_audioFeatureProvider, _beatManager, _deckStateProvider);
            layer.Order = _layers.Length == 0 ? 0 : _layers.Max(existing => existing.Order) + 1;
            RefreshLayers();
            return layer;
        }

        public void RemoveLayer(StageLayer layer)
        {
            if (layer == null || !Array.Exists(_layers, item => item == layer)) return;
            layer.gameObject.SetActive(false);
            Destroy(layer.gameObject);
            RefreshLayers();
        }

        public void MoveLayer(StageLayer layer, int direction)
        {
            var ordered = _layers.Where(item => item != null).OrderBy(item => item.Order).ToList();
            int index = ordered.IndexOf(layer);
            int nextIndex = index + direction;
            if (index < 0 || nextIndex < 0 || nextIndex >= ordered.Count) return;

            ordered.RemoveAt(index);
            ordered.Insert(nextIndex, layer);
            for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
            RefreshLayers();
        }

        public List<CameraStageLayerSaveData> CaptureLayers()
        {
            return _layers
                .Where(layer => layer is ShapeLayer or Primitive3DLayer or GpuParticleLayer)
                .Select(layer => new CameraStageLayerSaveData
                {
                    Type = layer switch
                    {
                        ShapeLayer => "shape",
                        Primitive3DLayer => "primitive3d",
                        GpuParticleLayer => "gpu-particle",
                        _ => string.Empty,
                    },
                    Name = layer.gameObject.name,
                    ParamsJson = JsonUtility.ToJson(layer.Params),
                })
                .ToList();
        }

        public void RestoreLayers(IEnumerable<CameraStageLayerSaveData> savedLayers)
        {
            foreach (var layer in _layers.Where(layer => layer != null).ToArray())
            {
                layer.gameObject.SetActive(false);
                Destroy(layer.gameObject);
            }
            _layers = Array.Empty<StageLayer>();

            foreach (var savedLayer in savedLayers ?? Enumerable.Empty<CameraStageLayerSaveData>())
            {
                StageLayer layer = savedLayer?.Type switch
                {
                    "shape" => AddShapeLayer(),
                    "primitive3d" => AddPrimitive3DLayer(),
                    "gpu-particle" => AddGpuParticleLayer(),
                    _ => null,
                };

                if (layer == null)
                {
                    Debug.LogWarning($"[CameraStage] 未対応のレイヤー型 '{savedLayer?.Type}' を読み込み時にスキップしました。", this);
                    continue;
                }

                string fallbackName = layer switch
                {
                    ShapeLayer => "Shape Layer",
                    Primitive3DLayer => "Primitive 3D Layer",
                    GpuParticleLayer => "GPU Particle Layer",
                    _ => "Layer",
                };
                layer.gameObject.name = string.IsNullOrWhiteSpace(savedLayer.Name) ? fallbackName : savedLayer.Name;
                if (!string.IsNullOrEmpty(savedLayer.ParamsJson)) JsonUtility.FromJsonOverwrite(savedLayer.ParamsJson, layer.Params);
            }

            RefreshLayers();
        }

        protected override void OnDestroy()
        {
            if (_camera != null) _camera.targetTexture = null;
            base.OnDestroy();
        }
    }

    [Serializable]
    public sealed class CameraStageLayerSaveData
    {
        public string Type;
        public string Name;
        public string ParamsJson;
    }
}
