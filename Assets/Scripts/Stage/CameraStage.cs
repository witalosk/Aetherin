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
    public enum CameraStageBackgroundMode
    {
        Skybox,
        SolidColor,
    }

    public enum LitReflectionSource
    {
        [InspectorName("Always SkyBox")] AlwaysSkybox,
        [InspectorName("Match Background")] MatchBackground,
        [InspectorName("SolidColor Only")] SolidColor,
    }

    /// <summary>
    /// カメラで撮ったシーンをそのまま出力するステージ
    /// カメラと被写体はこのオブジェクトの子に置く想定
    /// (Nextとして複製されたときはStageManagerがワールドオフセットを加えるため、複製元と互いに映り込まない)
    /// </summary>
    public partial class CameraStage : StageBase, IStageLayerHost
    {
        private const string CurrentRenderingLayerName = "StageCurrent";
        private const string NextRenderingLayerName = "StageNext";

        public override IReadOnlyList<StageLayer> Layers
        {
            get
            {
                EnsureLayersInitialized();
                return _layers;
            }
        }
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
        [SerializeField] private CameraStageBackgroundMode _backgroundMode;
        [SerializeField] private PaletteColorSource _backgroundColor = PaletteColorSource.BackgroundColor1;
        private StageLayer[] _layers = Array.Empty<StageLayer>();
        private bool _layersInitialized;
        private IAudioFeatureProvider _audioFeatureProvider;
        private IBeatManager _beatManager;
        private IStageAssetCatalog _assetCatalog;

        private IStageAssetCatalog AssetCatalog => _assetCatalog ??= StageAssetCatalog.FindBestAvailable();

        public CameraStageBackgroundMode BackgroundMode
        {
            get => _backgroundMode;
            set => _backgroundMode = value;
        }

        public PaletteColorSource BackgroundColor
        {
            get => _backgroundColor;
            set => _backgroundColor = value;
        }

        /// <summary>
        /// Current / Nextごとに描画LayerとCameraのcullingMaskを分け、
        /// Color・Depth・DepthNormals（SSR入力）と照明・影が別デッキを参照しないようにする。
        /// Stage生成・昇格時だけ呼ばれ、毎フレームの階層走査は行わない。
        /// </summary>
        internal void ConfigureDeckRenderingIsolation()
        {
            int layer = GetDeckRenderingLayer();
            if (layer < 0)
            {
                Debug.LogError($"CameraStage rendering layer is missing: " +
                               $"{(Deck == StageDeck.Current ? CurrentRenderingLayerName : NextRenderingLayerName)}", this);
                return;
            }

            SetLayerRecursively(transform, layer);
            Camera camera = StageCamera;
            if (camera != null) camera.cullingMask = 1 << layer;
        }

        /// <summary>このStage内で新規生成・プール復帰した部分木に、所属デッキのLayerを一度だけ設定する。</summary>
        internal void ApplyDeckRenderingLayer(GameObject root)
        {
            if (root == null) return;
            int layer = GetDeckRenderingLayer();
            if (layer >= 0) SetLayerRecursively(root.transform, layer);
        }

        private int GetDeckRenderingLayer() => LayerMask.NameToLayer(
            Deck == StageDeck.Current ? CurrentRenderingLayerName : NextRenderingLayerName);

        private void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            // URP lighting uses Rendering Layers independently of Camera.cullingMask.
            // Reserve bits 1/2 for the decks; bit 0 remains for non-stage objects.
            uint lightingMask = Deck == StageDeck.Current ? 1u << 1 : 1u << 2;
            foreach (Renderer renderer in root.GetComponents<Renderer>())
                renderer.renderingLayerMask = lightingMask;
            if (root.TryGetComponent(out Light light))
            {
                light.cullingMask = 1 << layer;
                UniversalAdditionalLightData data = light.GetUniversalAdditionalLightData();
                data.renderingLayers = lightingMask;
                data.customShadowLayers = false;
                data.shadowRenderingLayers = lightingMask;
            }
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }

        /// <summary>Litシェーダーが使用する環境反射の種類とSolidColor値を返す。</summary>
        public void ResolveLitReflection(LitReflectionSource source, out bool useSolidColor, out Color solidColor)
        {
            useSolidColor = source == LitReflectionSource.SolidColor ||
                            source == LitReflectionSource.MatchBackground &&
                            _backgroundMode == CameraStageBackgroundMode.SolidColor;
            ColorPalette palette = _deckStateProvider?.GetState(Deck).Palette;
            solidColor = PaletteColorParameter.Resolve(palette, _backgroundColor).linear;
        }

        [Inject]
        private void ConstructLayers(
            IAudioFeatureProvider audioFeatureProvider,
            IBeatManager beatManager,
            [Nullable] IStageAssetCatalog assetCatalog)
        {
            _audioFeatureProvider = audioFeatureProvider;
            _beatManager = beatManager;
            _assetCatalog = StageAssetCatalog.FindBestAvailable(assetCatalog);
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
            UniversalAdditionalCameraData cameraData = _camera.GetUniversalAdditionalCameraData();
            cameraData.requiresColorOption = CameraOverrideOption.On;
            // Current / NextはそれぞれこのカメラでRenderTextureへ描画するため、
            // URPのPost Processingを明示的に有効にしないとGlobal Volumeが評価されない。
            cameraData.renderPostProcessing = true;
            cameraData.volumeLayerMask = ~0;
            cameraData.volumeTrigger = _camera.transform;
            InitializeCameraWork();
            RefreshLayers();
        }

        private void Update()
        {
            if (_camera != null)
            {
                bool skybox = _backgroundMode == CameraStageBackgroundMode.Skybox;
                _camera.clearFlags = skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                if (!skybox)
                {
                    ColorPalette palette = _deckStateProvider?.GetState(Deck).Palette;
                    _camera.backgroundColor = PaletteColorParameter.Resolve(palette, _backgroundColor);
                }
            }
            UpdateCameraWork();
        }

        /// <summary>子にあるレイヤーを、非アクティブなものも含めて描画順に収集する。</summary>
        public void RefreshLayers()
        {
            EnsureLayerIds();
            _layers = GetComponentsInChildren<StageLayer>(true)
                .Where(layer => layer != null && layer.gameObject.activeSelf && layer.transform.parent == transform)
                .ToArray();
            Array.Sort(_layers, (a, b) => a.Order.CompareTo(b.Order));
            _layersInitialized = true;
            LayerRevision++;
        }

        public void EnsureLayerIds()
        {
            foreach (StageLayer layer in GetComponentsInChildren<StageLayer>(true))
                if (layer != null && layer.gameObject.activeSelf) layer.EnsureLayerId();
        }

        public StageLayer ResolveLayer(string layerId)
        {
            if (string.IsNullOrEmpty(layerId)) return null;
            return GetComponentsInChildren<StageLayer>(true)
                .FirstOrDefault(layer => layer != null && layer.gameObject.activeSelf && layer.LayerId == layerId);
        }

        public Transform ResolveLayerTarget(string layerId, string objectPath)
        {
            StageLayer layer = ResolveLayer(layerId);
            if (layer == null) return null;
            return string.IsNullOrEmpty(objectPath) ? layer.transform : layer.transform.Find(objectPath);
        }

        private void EnsureLayersInitialized()
        {
            if (!_layersInitialized) RefreshLayers();
        }

        public void SetLayerVisible(int index, bool visible)
        {
            if (index < 0 || index >= _layers.Length) return;
            _layers[index].Visible = visible;
        }

        public IReadOnlyList<LayerDescriptor> LayerDescriptors => LayerRegistry.Descriptors;

        /// <summary>登録済みのIDからレイヤーを生成する。未知のIDは生成せず null を返す。</summary>
        public StageLayer AddLayer(string typeId, Transform parent = null) =>
            AddLayer(LayerRegistry.GetDescriptor(typeId), parent);

        private StageLayer AddLayer(LayerDescriptor descriptor, Transform parent)
        {
            if (descriptor == null)
            {
                Debug.LogWarning("[CameraStage] 未登録のレイヤー型は生成できません。", this);
                return null;
            }

            var layerObject = new GameObject(descriptor.DefaultObjectName);
            layerObject.transform.SetParent(parent != null ? parent : transform, false);
            ApplyDeckRenderingLayer(layerObject);
            StageLayer layer = descriptor.Create(layerObject);
            if (layer == null)
            {
                Debug.LogError($"[CameraStage] レイヤー型 '{descriptor.TypeId}' を生成できませんでした。", this);
                Destroy(layerObject);
                return null;
            }

            descriptor.Initialize(layer, _audioFeatureProvider, _beatManager, _deckStateProvider);
            layer.Order = GetNextLayerOrder(layerObject.transform.parent);
            ApplyDeckRenderingLayer(layerObject);
            RefreshLayers();
            return layer;
        }

        // Existing public APIs remain as compatibility entry points for call sites and user scripts.
        public ShapeLayer AddShapeLayer(Transform parent = null) => AddLayer(LayerRegistry.Shape, parent) as ShapeLayer;
        public Primitive3DLayer AddPrimitive3DLayer(Transform parent = null) => AddLayer(LayerRegistry.Primitive3D, parent) as Primitive3DLayer;

        public GameObject ResolveModel(string key)
        {
            return AssetCatalog.ResolveModel(key);
        }

        public IReadOnlyList<string> GetModelKeys()
        {
            return AssetCatalog.GetModelKeys();
        }

        public Texture2D ResolveSpriteSheet(string key)
        {
            return AssetCatalog.ResolveTexture(key);
        }

        public Texture2D ResolveTexture(string key) => ResolveSpriteSheet(key);

        public IReadOnlyList<string> GetSpriteSheetKeys()
        {
            return AssetCatalog.GetTextureKeys();
        }

        public IReadOnlyList<string> GetTextureKeys() => GetSpriteSheetKeys();

        public Texture2D ResolveLut(string key)
        {
            return AssetCatalog.ResolveLut(key);
        }

        public IReadOnlyList<string> GetLutKeys()
        {
            return AssetCatalog.GetLutKeys();
        }

        public VisualEffectAsset ResolveVfxGraph(string key)
        {
            return AssetCatalog.ResolveVfxGraph(key);
        }

        public IReadOnlyList<string> GetVfxGraphKeys()
        {
            return AssetCatalog.GetVfxGraphKeys();
        }

        public TMP_FontAsset ResolveFontAsset(string key)
        {
            return AssetCatalog.ResolveFontAsset(key);
        }

        public IReadOnlyList<string> GetFontAssetKeys()
        {
            return AssetCatalog.GetFontAssetKeys();
        }

        public ModelLayer AddModelLayer(Transform parent = null) => AddLayer(LayerRegistry.Model, parent) as ModelLayer;
        public SpriteSheetLayer AddSpriteSheetLayer(Transform parent = null) => AddLayer(LayerRegistry.SpriteSheet, parent) as SpriteSheetLayer;
        public MovieLayer AddMovieLayer(Transform parent = null) => AddLayer(LayerRegistry.Movie, parent) as MovieLayer;
        public LightLayer AddLightLayer(Transform parent = null) => AddLayer(LayerRegistry.Light, parent) as LightLayer;
        public GpuParticleLayer AddGpuParticleLayer(Transform parent = null) => AddLayer(LayerRegistry.GpuParticle, parent) as GpuParticleLayer;
        public TextLayer AddTextLayer(Transform parent = null) => AddLayer(LayerRegistry.Text, parent) as TextLayer;
        public RuntimeShaderLayer AddRuntimeShaderLayer(Transform parent = null) => AddLayer(LayerRegistry.RuntimeShader, parent) as RuntimeShaderLayer;
        public GroupLayer AddGroupLayer(Transform parent = null) => AddLayer(LayerRegistry.Group, parent) as GroupLayer;

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
        public Camera LayerCamera => StageCamera;
        public bool LayerAllowsMidi => _deckStateProvider?.IsDeckEditable(Deck) ?? Deck == StageDeck.Next;
        public ColorPalette LayerPalette => _deckStateProvider?.GetState(Deck).Palette;
        public int ResolveLayerSortingOrder(StageLayer layer, int localOrder) => localOrder;
        public Texture2D ResolveLayerTexture(string key) => ResolveTexture(key);
        public IReadOnlyList<string> GetLayerTextureKeys() => GetTextureKeys();
        public TMP_FontAsset ResolveLayerFont(string key) => ResolveFontAsset(key);
        public IReadOnlyList<string> GetLayerFontKeys() => GetFontAssetKeys();

        public void MoveLayerToIndex(StageLayer layer, int targetIndex)
        {
            if (layer == null || layer.GetComponentInParent<CameraStage>(true) != this) return;

            Transform parent = layer.transform.parent;
            var ordered = parent.GetComponentsInChildren<StageLayer>(true)
                .Where(item => item != null && item.transform.parent == parent)
                .OrderBy(item => item.Order).ToList();
            int currentIndex = ordered.IndexOf(layer);
            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= ordered.Count || currentIndex == targetIndex) return;

            ordered.RemoveAt(currentIndex);
            ordered.Insert(targetIndex, layer);
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
            EnsureLayersInitialized();
            return _layers
                .Select(CaptureLayer)
                .ToList();
        }

        public CameraStageLayerSaveData CopyLayer(StageLayer layer)
        {
            if (layer == null || layer.GetComponentInParent<CameraStage>() != this) return null;
            CameraStageLayerSaveData copy = JsonUtility.FromJson<CameraStageLayerSaveData>(JsonUtility.ToJson(CaptureLayer(layer)));
            ClearLayerIds(copy);
            return copy;
        }

        private static void ClearLayerIds(CameraStageLayerSaveData layer)
        {
            if (layer == null) return;
            layer.LayerId = null;
            foreach (CameraStageLayerSaveData child in layer.Children ?? new List<CameraStageLayerSaveData>())
                ClearLayerIds(child);
        }

        public StageLayer PasteLayer(CameraStageLayerSaveData clipboard, Transform parent, int orderAfter)
        {
            if (clipboard == null) return null;
            parent ??= transform;
            StageLayer pasted = RestoreLayer(clipboard, parent);
            if (pasted == null) return null;
            pasted.gameObject.name += " Copy";
            foreach (StageLayer sibling in parent.GetComponentsInChildren<StageLayer>(true)
                         .Where(item => item != null && item.transform.parent == parent && item != pasted && item.Order > orderAfter))
                sibling.Order++;
            pasted.Order = orderAfter + 1;
            RefreshLayers();
            return pasted;
        }

        private static CameraStageLayerSaveData CaptureLayer(StageLayer layer) => new()
        {
            LayerId = layer.LayerId,
            Type = LayerRegistry.GetDescriptor(layer)?.TypeId ?? string.Empty,
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
            LayerDescriptor descriptor = LayerRegistry.GetDescriptor(savedLayer?.Type);
            if (descriptor == null)
            {
                Debug.LogWarning($"[CameraStage] 未対応のレイヤー型 '{savedLayer?.Type}' を読み込み時にスキップしました。", this);
                return null;
            }

            StageLayer layer = AddLayer(descriptor, parent);
            if (layer == null) return null;

            layer.SetLayerId(savedLayer.LayerId);
            layer.gameObject.name = string.IsNullOrWhiteSpace(savedLayer.Name) ? descriptor.DefaultObjectName : savedLayer.Name;
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
        public string LayerId;
        public string Type;
        public string Name;
        public string ParamsJson;
        public List<CameraStageLayerSaveData> Children = new();
    }
}
