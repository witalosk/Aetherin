using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;
using UnitySimpleContainer;

namespace Aetherin
{
    /// <summary>
    /// カメラで撮ったシーンをそのまま出力するステージ
    /// カメラと被写体はこのオブジェクトの子に置く想定
    /// (Nextとして複製されたときはStageManagerがワールドオフセットを加えるため、複製元と互いに映り込まない)
    /// </summary>
    public partial class CameraStage : StageBase
    {
        public override IReadOnlyList<StageLayer> Layers => _layers;
        public int LayerRevision { get; private set; }
        public Camera StageCamera
        {
            get
            {
                if (_camera == null) _camera = GetComponentInChildren<Camera>(true);
                return _camera;
            }
        }

        [SerializeField] private Camera _camera;
        [SerializeField] private ModelLayerLibrary _modelLibrary;
        [SerializeField] private VfxGraphLibrary _vfxGraphLibrary;
        [SerializeField] private FontAssetLibrary _fontAssetLibrary;
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
            InitializeCameraWork();
            RefreshLayers();
        }

        private void Update()
        {
            _camera.backgroundColor = _deckStateProvider.GetState(Deck).Palette?.BackgroundColor1 ?? Color.black;
            UpdateCameraWork();
        }

        /// <summary>子にあるレイヤーを、非アクティブなものも含めて描画順に収集する。</summary>
        public void RefreshLayers()
        {
            _layers = GetComponentsInChildren<StageLayer>(true)
                .Where(layer => layer != null && layer.gameObject.activeSelf && layer.transform.parent == transform)
                .ToArray();
            Array.Sort(_layers, (a, b) => a.Order.CompareTo(b.Order));
            LayerRevision++;
        }

        public void SetLayerVisible(int index, bool visible)
        {
            if (index < 0 || index >= _layers.Length) return;
            _layers[index].Visible = visible;
        }

        public ShapeLayer AddShapeLayer(Transform parent = null)
        {
            var layerObject = new GameObject("Shape Layer");
            layerObject.transform.SetParent(parent != null ? parent : transform, false);
            layerObject.AddComponent<MeshFilter>();
            layerObject.AddComponent<MeshRenderer>();
            var layer = layerObject.AddComponent<ShapeLayer>();
            layer.Initialize(_audioFeatureProvider, _beatManager, _deckStateProvider);
            layer.Order = GetNextLayerOrder(layerObject.transform.parent);
            RefreshLayers();
            return layer;
        }

        public Primitive3DLayer AddPrimitive3DLayer(Transform parent = null)
        {
            var layerObject = new GameObject("Primitive 3D Layer");
            layerObject.transform.SetParent(parent != null ? parent : transform, false);
            layerObject.AddComponent<MeshFilter>();
            layerObject.AddComponent<MeshRenderer>();
            var layer = layerObject.AddComponent<Primitive3DLayer>();
            layer.Initialize(_audioFeatureProvider, _beatManager, _deckStateProvider);
            layer.Order = GetNextLayerOrder(layerObject.transform.parent);
            RefreshLayers();
            return layer;
        }

        public GameObject ResolveModel(string key)
        {
            EnsureModelLibrary();
            return _modelLibrary?.Resolve(key);
        }

        public IReadOnlyList<string> GetModelKeys()
        {
            EnsureModelLibrary();
            return _modelLibrary?.GetKeys() ?? Array.Empty<string>();
        }

        private void EnsureModelLibrary()
        {
            if (_modelLibrary == null)
                _modelLibrary = FindFirstObjectByType<ModelLayerLibrary>(FindObjectsInactive.Include);
        }

        public VisualEffectAsset ResolveVfxGraph(string key)
        {
            EnsureVfxGraphLibrary();
            return _vfxGraphLibrary?.Resolve(key);
        }

        public IReadOnlyList<string> GetVfxGraphKeys()
        {
            EnsureVfxGraphLibrary();
            return _vfxGraphLibrary?.GetKeys() ?? Array.Empty<string>();
        }

        private void EnsureVfxGraphLibrary()
        {
            if (_vfxGraphLibrary == null)
                _vfxGraphLibrary = FindFirstObjectByType<VfxGraphLibrary>(FindObjectsInactive.Include);
        }

        public TMP_FontAsset ResolveFontAsset(string key)
        {
            EnsureFontAssetLibrary();
            return _fontAssetLibrary?.Resolve(key);
        }

        public IReadOnlyList<string> GetFontAssetKeys()
        {
            EnsureFontAssetLibrary();
            return _fontAssetLibrary?.GetKeys() ?? Array.Empty<string>();
        }

        private void EnsureFontAssetLibrary()
        {
            if (_fontAssetLibrary == null)
                _fontAssetLibrary = FindFirstObjectByType<FontAssetLibrary>(FindObjectsInactive.Include);
        }

        public ModelLayer AddModelLayer(Transform parent = null)
        {
            var layerObject = new GameObject("Model Layer");
            layerObject.transform.SetParent(parent != null ? parent : transform, false);
            var layer = layerObject.AddComponent<ModelLayer>();
            layer.Initialize(_audioFeatureProvider, _beatManager, _deckStateProvider);
            layer.Order = GetNextLayerOrder(layerObject.transform.parent);
            RefreshLayers();
            return layer;
        }

        public GpuParticleLayer AddGpuParticleLayer(Transform parent = null)
        {
            var layerObject = new GameObject("GPU Particle Layer");
            layerObject.transform.SetParent(parent != null ? parent : transform, false);
            var layer = layerObject.AddComponent<GpuParticleLayer>();
            layer.Initialize(_audioFeatureProvider, _beatManager, _deckStateProvider);
            layer.Order = GetNextLayerOrder(layerObject.transform.parent);
            RefreshLayers();
            return layer;
        }

        public TextLayer AddTextLayer(Transform parent = null)
        {
            var layerObject = new GameObject("Text Layer");
            layerObject.transform.SetParent(parent != null ? parent : transform, false);
            layerObject.AddComponent<MeshRenderer>();
            layerObject.AddComponent<TMPro.TextMeshPro>();
            var layer = layerObject.AddComponent<TextLayer>();
            layer.Initialize(_audioFeatureProvider, _beatManager, _deckStateProvider);
            layer.Order = GetNextLayerOrder(layerObject.transform.parent);
            RefreshLayers();
            return layer;
        }

        public RuntimeShaderLayer AddRuntimeShaderLayer(Transform parent = null)
        {
            var layerObject = new GameObject("Runtime Shader Layer");
            layerObject.transform.SetParent(parent != null ? parent : transform, false);
            layerObject.AddComponent<MeshFilter>();
            layerObject.AddComponent<MeshRenderer>();
            var layer = layerObject.AddComponent<RuntimeShaderLayer>();
            layer.Initialize(_audioFeatureProvider, _beatManager, _deckStateProvider);
            layer.Order = GetNextLayerOrder(layerObject.transform.parent);
            RefreshLayers();
            return layer;
        }

        public GroupLayer AddGroupLayer(Transform parent = null)
        {
            var layerObject = new GameObject("Group Layer");
            layerObject.transform.SetParent(parent != null ? parent : transform, false);
            var layer = layerObject.AddComponent<GroupLayer>();
            layer.Initialize(_audioFeatureProvider, _beatManager);
            layer.Order = GetNextLayerOrder(layerObject.transform.parent);
            RefreshLayers();
            return layer;
        }

        private int GetNextLayerOrder(Transform parent)
        {
            var siblings = parent.GetComponentsInChildren<StageLayer>(true)
                .Where(layer => layer != null && layer.transform.parent == parent).ToArray();
            return siblings.Length == 0 ? 0 : siblings.Max(layer => layer.Order) + 1;
        }

        public void RemoveLayer(StageLayer layer)
        {
            if (layer == null || layer.GetComponentInParent<CameraStage>() != this) return;
            layer.gameObject.SetActive(false);
            Destroy(layer.gameObject);
            RefreshLayers();
        }

        public void MoveLayer(StageLayer layer, int direction)
        {
            Transform parent = layer.transform.parent;
            var ordered = parent.GetComponentsInChildren<StageLayer>(true)
                .Where(item => item != null && item.transform.parent == parent)
                .OrderBy(item => item.Order).ToList();
            int index = ordered.IndexOf(layer);
            int nextIndex = index + direction;
            if (index < 0 || nextIndex < 0 || nextIndex >= ordered.Count) return;

            ordered.RemoveAt(index);
            ordered.Insert(nextIndex, layer);
            for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
            RefreshLayers();
        }

        public void MoveLayerToGroup(StageLayer layer, GroupLayer group)
        {
            if (layer == null || group == null || layer == group) return;
            if (group.transform.IsChildOf(layer.transform)) return;
            layer.transform.SetParent(group.transform, true);
            layer.Order = GetNextLayerOrder(group.transform);
            RefreshLayers();
        }

        public void MoveLayerOutOfGroup(StageLayer layer)
        {
            if (layer == null || layer.transform.parent == transform) return;
            var parentGroup = layer.transform.parent.GetComponent<GroupLayer>();
            if (parentGroup == null) return;
            Transform destination = parentGroup.transform.parent;
            layer.transform.SetParent(destination, true);
            layer.Order = GetNextLayerOrder(destination);
            RefreshLayers();
        }

        public void SetLayerOrder(IReadOnlyList<StageLayer> orderedLayers)
        {
            if (orderedLayers == null || orderedLayers.Count != _layers.Length) return;
            if (orderedLayers.Any(layer => layer == null || !Array.Exists(_layers, existing => existing == layer))) return;

            for (int i = 0; i < orderedLayers.Count; i++) orderedLayers[i].Order = i;
            RefreshLayers();
        }

        public List<CameraStageLayerSaveData> CaptureLayers()
        {
            return _layers
                .Select(CaptureLayer)
                .ToList();
        }

        private static CameraStageLayerSaveData CaptureLayer(StageLayer layer) => new()
        {
            Type = layer switch
            {
                ShapeLayer => "shape", Primitive3DLayer => "primitive3d", ModelLayer => "model",
                GpuParticleLayer => "gpu-particle", TextLayer => "text",
                RuntimeShaderLayer => "runtime-shader", GroupLayer => "group", _ => string.Empty,
            },
            Name = layer.gameObject.name,
            ParamsJson = JsonUtility.ToJson(layer.Params),
            Children = layer is GroupLayer group ? group.Children.Select(CaptureLayer).ToList() : new List<CameraStageLayerSaveData>(),
        };

        public void RestoreLayers(IEnumerable<CameraStageLayerSaveData> savedLayers)
        {
            foreach (var layer in _layers.Where(layer => layer != null).ToArray())
            {
                layer.gameObject.SetActive(false);
                Destroy(layer.gameObject);
            }
            _layers = Array.Empty<StageLayer>();

            foreach (var savedLayer in savedLayers ?? Enumerable.Empty<CameraStageLayerSaveData>())
                RestoreLayer(savedLayer, transform);

            RefreshLayers();
        }

        private StageLayer RestoreLayer(CameraStageLayerSaveData savedLayer, Transform parent)
        {
            StageLayer layer = savedLayer?.Type switch
            {
                "shape" => AddShapeLayer(parent), "primitive3d" => AddPrimitive3DLayer(parent),
                "model" => AddModelLayer(parent), "gpu-particle" => AddGpuParticleLayer(parent),
                "text" => AddTextLayer(parent), "runtime-shader" => AddRuntimeShaderLayer(parent),
                "group" => AddGroupLayer(parent), _ => null,
            };

            if (layer == null)
            {
                Debug.LogWarning($"[CameraStage] 未対応のレイヤー型 '{savedLayer?.Type}' を読み込み時にスキップしました。", this);
                return null;
            }

                string fallbackName = layer switch
                {
                    ShapeLayer => "Shape Layer",
                    Primitive3DLayer => "Primitive 3D Layer",
                    ModelLayer => "Model Layer",
                    GpuParticleLayer => "GPU Particle Layer",
                    TextLayer => "Text Layer",
                    RuntimeShaderLayer => "Runtime Shader Layer", GroupLayer => "Group Layer",
                    _ => "Layer",
                };
            layer.gameObject.name = string.IsNullOrWhiteSpace(savedLayer.Name) ? fallbackName : savedLayer.Name;
            if (!string.IsNullOrEmpty(savedLayer.ParamsJson)) JsonUtility.FromJsonOverwrite(savedLayer.ParamsJson, layer.Params);
            if (layer is GroupLayer)
                foreach (var child in savedLayer.Children ?? new List<CameraStageLayerSaveData>()) RestoreLayer(child, layer.transform);
            return layer;
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
        public List<CameraStageLayerSaveData> Children = new();
    }
}
