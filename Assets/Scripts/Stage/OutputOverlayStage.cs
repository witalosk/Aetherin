using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Aetherin
{
    /// <summary>Independent transparent layer stage composited immediately before Output post effects.</summary>
    public sealed class OutputOverlayStage : StageBase, IStageLayerHost
    {
        public override IReadOnlyList<StageLayer> Layers => _layers;
        public IReadOnlyList<OutputOverlayCueRuntime> Cues => _cues;
        public int Revision { get; private set; }
        public Camera LayerCamera => _camera;
        public bool LayerAllowsMidi => Application.isPlaying;
        public ColorPalette LayerPalette => _deckStateProvider?.GetState(StageDeck.Current).Palette;
        public int ResolveLayerSortingOrder(StageLayer layer, int localOrder)
        {
            OutputOverlayCueRuntime cue = layer?.GetComponentInParent<OutputOverlayCueRuntime>();
            int cueIndex = cue != null ? _cues.IndexOf(cue) : 0;
            int nestedOrder = 0;
            Transform ancestor = layer?.transform.parent;
            while (ancestor != null && ancestor != transform)
            {
                GroupLayer group = ancestor.GetComponent<GroupLayer>();
                if (group != null && group != cue?.Root) nestedOrder = nestedOrder * 100 + group.Order;
                ancestor = ancestor.parent;
            }
            return cueIndex * 10000 + nestedOrder * 100 + localOrder;
        }

        private Camera _camera;
        private StageLayer[] _layers = Array.Empty<StageLayer>();
        private readonly List<OutputOverlayCueRuntime> _cues = new();
        private IAudioFeatureProvider _audio;
        private IBeatManager _beat;
        private IStageAssetCatalog _assets;
        private int _overlayLayer = -1;

        public void Initialize(IAudioFeatureProvider audio, IBeatManager beat, IStageAssetCatalog assets, Camera camera)
        {
            _audio = audio;
            _beat = beat;
            _assets = StageAssetCatalog.FindBestAvailable(assets);
            _camera = camera;
            _overlayLayer = LayerMask.NameToLayer("StageOverlay");
            if (_overlayLayer < 0) Debug.LogError("Required Unity layer 'StageOverlay' is missing.", this);
            ConfigureCamera();
        }

        protected override void Start()
        {
            base.Start();
            ConfigureCamera();
            if (_camera != null) _camera.targetTexture = OutputTexture;
        }

        private void Update()
        {
            ConfigureCamera();
            for (int i = 0; i < _cues.Count; i++) _cues[i]?.Tick();
        }

        private void ConfigureCamera()
        {
            if (_camera == null) return;
            _camera.orthographic = true;
            _camera.orthographicSize = 1f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.clear;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 100f;
            _camera.allowHDR = true;
            _camera.cullingMask = _overlayLayer >= 0 ? 1 << _overlayLayer : 0;
        }

        public OutputOverlayCueRuntime AddCue(string cueName = "Overlay Cue")
        {
            var data = new OutputOverlayCueData { Name = cueName };
            data.EnsureInitialized();
            var rootObject = new GameObject(cueName);
            rootObject.transform.SetParent(transform, false);
            ApplyOverlayLayer(rootObject.transform);
            var root = rootObject.AddComponent<GroupLayer>();
            root.Initialize(_audio, _beat, _deckStateProvider);
            root.Order = _cues.Count;
            var runtime = rootObject.AddComponent<OutputOverlayCueRuntime>();
            runtime.Initialize(data, root, this);
            _cues.Add(runtime);
            RefreshLayers();
            return runtime;
        }

        public void RemoveCue(OutputOverlayCueRuntime cue)
        {
            if (cue == null || !_cues.Remove(cue)) return;
            cue.Stop();
            cue.gameObject.SetActive(false);
            Destroy(cue.gameObject);
            RefreshLayers();
        }

        public OutputOverlayCueRuntime DuplicateCue(OutputOverlayCueRuntime source)
        {
            if (source == null) return null;
            OutputOverlayCueData copy = CaptureCue(source);
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = $"{copy.Name} Copy";
            ClearLayerIds(copy.RootLayer);
            OutputOverlayCueRuntime cue = AddCue(copy.Name);
            cue.Initialize(copy, cue.Root, this);
            if (copy.RootLayer != null)
            {
                if (!string.IsNullOrEmpty(copy.RootLayer.ParamsJson))
                    JsonUtility.FromJsonOverwrite(copy.RootLayer.ParamsJson, cue.Root.Params);
                cue.Root.SetLayerId(copy.RootLayer.LayerId);
                foreach (CameraStageLayerSaveData child in copy.RootLayer.Children ?? new List<CameraStageLayerSaveData>())
                    RestoreLayer(cue, child, cue.Root.transform);
            }
            cue.Stop();
            RefreshLayers();
            return cue;
        }

        public void MoveCue(OutputOverlayCueRuntime cue, int direction)
        {
            int index = _cues.IndexOf(cue);
            int destination = index + direction;
            if (index < 0 || destination < 0 || destination >= _cues.Count) return;
            _cues.RemoveAt(index);
            _cues.Insert(destination, cue);
            for (int i = 0; i < _cues.Count; i++) _cues[i].Root.Order = i;
            RefreshLayers();
        }

        internal void StopExclusivePeers(OutputOverlayCueRuntime source)
        {
            string group = source?.Data?.ExclusiveGroup;
            if (string.IsNullOrWhiteSpace(group)) return;
            foreach (OutputOverlayCueRuntime cue in _cues)
                if (cue != null && cue != source && cue.IsPlaying && cue.Data.ExclusiveGroup == group)
                    cue.Stop();
        }

        public StageLayer AddLayer(OutputOverlayCueRuntime cue, string typeId, Transform parent = null)
        {
            if (cue == null) return null;
            LayerDescriptor descriptor = LayerRegistry.GetDescriptor(typeId);
            if (descriptor == null) return null;
            parent ??= cue.Root.transform;
            var layerObject = new GameObject(descriptor.DefaultObjectName);
            layerObject.transform.SetParent(parent, false);
            ApplyOverlayLayer(layerObject.transform);
            StageLayer layer = descriptor.Create(layerObject);
            descriptor.Initialize(layer, _audio, _beat, _deckStateProvider);
            layer.Order = NextOrder(parent);
            RefreshLayers();
            return layer;
        }

        public void RemoveLayer(StageLayer layer)
        {
            if (layer == null || layer.GetComponentInParent<OutputOverlayStage>() != this ||
                layer.GetComponent<OutputOverlayCueRuntime>() != null) return;
            layer.gameObject.SetActive(false);
            Destroy(layer.gameObject);
            RefreshLayers();
        }

        public void MoveLayer(StageLayer layer, int direction)
        {
            if (layer == null) return;
            Transform parent = layer.transform.parent;
            var siblings = DirectChildren(parent);
            int index = siblings.IndexOf(layer);
            int destination = index + direction;
            if (index < 0 || destination < 0 || destination >= siblings.Count) return;
            siblings.RemoveAt(index);
            siblings.Insert(destination, layer);
            for (int i = 0; i < siblings.Count; i++) siblings[i].Order = i;
            RefreshLayers();
        }

        public void MoveLayerToIndex(StageLayer layer, int targetIndex)
        {
            if (layer == null || layer.GetComponent<OutputOverlayCueRuntime>() != null) return;
            Transform parent = layer.transform.parent;
            List<StageLayer> siblings = DirectChildren(parent);
            int currentIndex = siblings.IndexOf(layer);
            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= siblings.Count || currentIndex == targetIndex)
                return;
            siblings.RemoveAt(currentIndex);
            siblings.Insert(targetIndex, layer);
            for (int i = 0; i < siblings.Count; i++) siblings[i].Order = i;
            RefreshLayers();
        }

        public CameraStageLayerSaveData CopyLayer(StageLayer layer)
        {
            if (layer == null || layer.GetComponentInParent<OutputOverlayStage>() != this ||
                layer.GetComponent<OutputOverlayCueRuntime>() != null) return null;
            CameraStageLayerSaveData copy = JsonUtility.FromJson<CameraStageLayerSaveData>(
                JsonUtility.ToJson(CaptureLayer(layer)));
            ClearLayerIds(copy);
            return copy;
        }

        public StageLayer PasteLayer(
            OutputOverlayCueRuntime cue,
            CameraStageLayerSaveData clipboard,
            Transform parent,
            int orderAfter)
        {
            if (cue == null || clipboard == null) return null;
            parent ??= cue.Root.transform;
            StageLayer pasted = RestoreLayer(cue, clipboard, parent);
            if (pasted == null) return null;
            pasted.gameObject.name += " Copy";
            foreach (StageLayer sibling in DirectChildren(parent)
                         .Where(item => item != pasted && item.Order > orderAfter))
                sibling.Order++;
            pasted.Order = orderAfter + 1;
            RefreshLayers();
            return pasted;
        }

        public void MoveLayerToGroup(StageLayer layer, GroupLayer group)
        {
            if (layer == null || group == null || layer == group || group.transform.IsChildOf(layer.transform)) return;
            OutputOverlayCueRuntime layerCue = layer.GetComponentInParent<OutputOverlayCueRuntime>();
            OutputOverlayCueRuntime groupCue = group.GetComponentInParent<OutputOverlayCueRuntime>();
            if (layerCue == null || layerCue != groupCue || layer.GetComponent<OutputOverlayCueRuntime>() != null) return;
            layer.transform.SetParent(group.transform, true);
            layer.Order = NextOrder(group.transform);
            RefreshLayers();
        }

        public void MoveLayerOutOfGroup(StageLayer layer)
        {
            if (layer == null || layer.GetComponent<OutputOverlayCueRuntime>() != null) return;
            GroupLayer parentGroup = layer.transform.parent != null
                ? layer.transform.parent.GetComponent<GroupLayer>()
                : null;
            if (parentGroup == null || parentGroup.GetComponent<OutputOverlayCueRuntime>() != null) return;
            Transform destination = parentGroup.transform.parent;
            layer.transform.SetParent(destination, true);
            layer.Order = NextOrder(destination);
            RefreshLayers();
        }

        public CameraStageLayerSaveData CaptureLayer(StageLayer layer) => new()
        {
            LayerId = layer.LayerId,
            Type = LayerRegistry.GetDescriptor(layer)?.TypeId ?? string.Empty,
            Name = layer.gameObject.name,
            ParamsJson = JsonUtility.ToJson(layer.Params),
            Children = layer is GroupLayer group
                ? group.Children.Select(CaptureLayer).ToList()
                : new List<CameraStageLayerSaveData>(),
        };

        public OutputOverlayCueData CaptureCue(OutputOverlayCueRuntime cue)
        {
            OutputOverlayCueData copy = JsonUtility.FromJson<OutputOverlayCueData>(JsonUtility.ToJson(cue.Data));
            copy.RootLayer = CaptureLayer(cue.Root);
            return copy;
        }

        public void RestoreCues(IEnumerable<OutputOverlayCueData> savedCues)
        {
            foreach (OutputOverlayCueRuntime cue in _cues)
                if (cue != null)
                {
                    cue.gameObject.SetActive(false);
                    Destroy(cue.gameObject);
                }
            _cues.Clear();
            foreach (OutputOverlayCueData saved in savedCues ?? Array.Empty<OutputOverlayCueData>())
            {
                if (saved == null) continue;
                saved.EnsureInitialized();
                OutputOverlayCueRuntime cue = AddCue(saved.Name);
                cue.Initialize(saved, cue.Root, this);
                if (saved.RootLayer != null)
                {
                    if (!string.IsNullOrEmpty(saved.RootLayer.ParamsJson))
                        JsonUtility.FromJsonOverwrite(saved.RootLayer.ParamsJson, cue.Root.Params);
                    cue.Root.SetLayerId(saved.RootLayer.LayerId);
                    foreach (CameraStageLayerSaveData child in saved.RootLayer.Children ?? new List<CameraStageLayerSaveData>())
                        RestoreLayer(cue, child, cue.Root.transform);
                }
                cue.Stop();
            }
            RefreshLayers();
        }

        private StageLayer RestoreLayer(OutputOverlayCueRuntime cue, CameraStageLayerSaveData saved, Transform parent)
        {
            StageLayer layer = AddLayer(cue, saved?.Type, parent);
            if (layer == null) return null;
            layer.SetLayerId(saved.LayerId);
            layer.gameObject.name = string.IsNullOrWhiteSpace(saved.Name) ? layer.gameObject.name : saved.Name;
            if (!string.IsNullOrEmpty(saved.ParamsJson)) JsonUtility.FromJsonOverwrite(saved.ParamsJson, layer.Params);
            if (layer is GroupLayer)
                foreach (CameraStageLayerSaveData child in saved.Children ?? new List<CameraStageLayerSaveData>())
                    RestoreLayer(cue, child, layer.transform);
            return layer;
        }

        private static void ClearLayerIds(CameraStageLayerSaveData layer)
        {
            if (layer == null) return;
            layer.LayerId = null;
            foreach (CameraStageLayerSaveData child in layer.Children ?? new List<CameraStageLayerSaveData>())
                ClearLayerIds(child);
        }

        private void RefreshLayers()
        {
            _layers = GetComponentsInChildren<StageLayer>(true)
                .Where(layer => layer != null && layer.transform.parent == transform)
                .OrderBy(layer => layer.Order).ToArray();
            Revision++;
        }

        private int NextOrder(Transform parent)
        {
            List<StageLayer> siblings = DirectChildren(parent);
            return siblings.Count == 0 ? 0 : siblings.Max(layer => layer.Order) + 1;
        }

        private static List<StageLayer> DirectChildren(Transform parent) =>
            parent.GetComponentsInChildren<StageLayer>(true)
                .Where(layer => layer != null && layer.transform.parent == parent)
                .OrderBy(layer => layer.Order).ToList();

        private void ApplyOverlayLayer(Transform root)
        {
            if (_overlayLayer >= 0) root.gameObject.layer = _overlayLayer;
            for (int i = 0; i < root.childCount; i++) ApplyOverlayLayer(root.GetChild(i));
        }

        public Texture2D ResolveLayerTexture(string key) => _assets?.ResolveTexture(key);
        public IReadOnlyList<string> GetLayerTextureKeys() => _assets?.GetTextureKeys() ?? Array.Empty<string>();
        public TMP_FontAsset ResolveLayerFont(string key) => _assets?.ResolveFontAsset(key);
        public IReadOnlyList<string> GetLayerFontKeys() => _assets?.GetFontAssetKeys() ?? Array.Empty<string>();

        protected override void OnDestroy()
        {
            if (_camera != null) _camera.targetTexture = null;
            base.OnDestroy();
        }
    }
}
