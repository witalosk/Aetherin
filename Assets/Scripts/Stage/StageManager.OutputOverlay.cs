using System;
using System.Collections.Generic;
using System.Linq;
using RosettaUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Aetherin
{
    public enum StageManagerEditMode
    {
        Stage,
        Overlay,
    }

    [Serializable]
    public sealed class OutputOverlaySaveData
    {
        public int Version = 1;
        public List<OutputOverlayCueData> Cues = new();
    }

    public partial class StageManager
    {
        private static readonly int OverlayTexId = Shader.PropertyToID("_OverlayTex");
        private OutputOverlayStage _outputOverlayStage;
        private Material _outputOverlayCompositeMaterial;
        private RenderTexture _outputOverlayCompositeTexture;
        private int _selectedOverlayCue;
        private StageManagerEditMode _mainEditMode;

        private void InitializeOutputOverlay()
        {
            Shader shader = Shader.Find("Hidden/Aetherin/OutputOverlayComposite");
            if (shader == null)
            {
                Debug.LogError("Output Overlay composite shader was not found.", this);
                return;
            }

            _outputOverlayCompositeMaterial = new Material(shader)
            {
                name = "Output Overlay Composite",
                hideFlags = HideFlags.DontSave,
            };
            _outputOverlayCompositeTexture = new RenderTexture(
                _applicationManager.Resolution.x, _applicationManager.Resolution.y, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Output Overlay Composite",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _outputOverlayCompositeTexture.Create();

            var stageObject = new GameObject("Output Overlay Stage");
            stageObject.transform.SetParent(transform, false);
            stageObject.transform.localPosition = new Vector3(0f, 2000f, 0f);
            int overlayLayer = LayerMask.NameToLayer("StageOverlay");
            if (overlayLayer >= 0) stageObject.layer = overlayLayer;

            var cameraObject = new GameObject("Output Overlay Camera");
            cameraObject.transform.SetParent(stageObject.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            if (overlayLayer >= 0) cameraObject.layer = overlayLayer;
            Camera camera = cameraObject.AddComponent<Camera>();

            _outputOverlayStage = stageObject.AddComponent<OutputOverlayStage>();
            _outputOverlayStage.Deck = StageDeck.Current;
            _outputOverlayStage.SetIdentity("output-overlay", "Output Overlay");
            _outputOverlayStage.Construct(_applicationManager, this);
            _outputOverlayStage.Initialize(_audioFeatureProvider, _beatManager, _stageAssetCatalog, camera);
        }

        private Texture CompositeOutputOverlay(Texture source)
        {
            Texture overlay = _outputOverlayStage != null ? _outputOverlayStage.OutputTexture : null;
            if (overlay == null || _outputOverlayCompositeMaterial == null || _outputOverlayCompositeTexture == null)
                return source;
            _outputOverlayCompositeMaterial.SetTexture(OverlayTexId, overlay);
            Graphics.Blit(source, _outputOverlayCompositeTexture, _outputOverlayCompositeMaterial);
            return _outputOverlayCompositeTexture;
        }

        private void TriggerStageChangeOverlayCues()
        {
            if (_outputOverlayStage == null) return;
            foreach (OutputOverlayCueRuntime cue in _outputOverlayStage.Cues)
                if (cue?.Data?.TriggerOnStageChange == true) cue.Trigger();
        }

        private OutputOverlaySaveData CaptureOutputOverlaySaveData()
        {
            var data = new OutputOverlaySaveData();
            if (_outputOverlayStage == null) return data;
            data.Cues.AddRange(_outputOverlayStage.Cues
                .Where(cue => cue != null)
                .Select(_outputOverlayStage.CaptureCue));
            return data;
        }

        private void RestoreOutputOverlaySaveData(OutputOverlaySaveData data)
        {
            if (_outputOverlayStage == null || data == null) return;
            _outputOverlayStage.RestoreCues(data.Cues);
            _selectedOverlayCue = Mathf.Clamp(_selectedOverlayCue, 0,
                Mathf.Max(0, _outputOverlayStage.Cues.Count - 1));
        }

        private void DisposeOutputOverlay()
        {
            if (_outputOverlayCompositeTexture != null)
            {
                _outputOverlayCompositeTexture.Release();
                Destroy(_outputOverlayCompositeTexture);
            }
            if (_outputOverlayCompositeMaterial != null) Destroy(_outputOverlayCompositeMaterial);
            if (_outputOverlayStage != null) Destroy(_outputOverlayStage.gameObject);
        }

        private Element CreateOutputOverlayElement()
        {
            return UI.DynamicElementOnStatusChanged(
                () => (_outputOverlayStage?.Revision ?? 0, _selectedOverlayCue),
                _ => BuildOutputOverlayContents());
        }

        private Element CreateMainEditModeElement()
        {
            return UI.Row(
                UI.Button("Stage", () => SetMainEditMode(StageManagerEditMode.Stage))
                    .SetWidth(120f)
                    .RegisterUpdateCallback(element => element.SetBackgroundColor(
                        _mainEditMode == StageManagerEditMode.Stage ? Color.deepSkyBlue * 0.55f : null)),
                UI.Button("Overlay", () => SetMainEditMode(StageManagerEditMode.Overlay))
                    .SetWidth(120f)
                    .RegisterUpdateCallback(element => element.SetBackgroundColor(
                        _mainEditMode == StageManagerEditMode.Overlay ? Color.deepSkyBlue * 0.55f : null)));
        }

        private void SetMainEditMode(StageManagerEditMode mode)
        {
            if (_mainEditMode == mode) return;
            _mainEditMode = mode;
            if (mode == StageManagerEditMode.Stage)
            {
                _inspectedOverlayCue = null;
                _inspectedOverlayLayer = null;
            }
            else
            {
                _inspectedLayer = null;
                _inspectedLayerStage = null;
                _inspectedCameraWork = null;
                _inspectedCameraWorkStage = null;
            }
            _inspectorElement?.CheckAndRebuild();
        }

        private Element BuildOutputOverlayContents()
        {
            if (_outputOverlayStage == null) return UI.Label("Output Overlay is not initialized.");
            IReadOnlyList<OutputOverlayCueRuntime> cues = _outputOverlayStage.Cues;
            _selectedOverlayCue = Mathf.Clamp(_selectedOverlayCue, 0, Mathf.Max(0, cues.Count - 1));

            var cueButtons = cues.Select((cue, index) =>
            {
                int captured = index;
                return UI.Button(UI.Label(() => cue != null && cue.IsPlaying
                        ? $"▶ {cue.Data.Name}"
                        : cue?.Data?.Name ?? "Missing Cue"),
                    () => _selectedOverlayCue = captured)
                    .SetBackgroundColor(captured == _selectedOverlayCue ? Color.deepSkyBlue * 0.45f : null);
            }).ToArray();

            Element left = UI.Column(
                UI.Button("+ Cue", () =>
                {
                    _outputOverlayStage.AddCue($"Overlay Cue {_outputOverlayStage.Cues.Count + 1}");
                    _selectedOverlayCue = _outputOverlayStage.Cues.Count - 1;
                }),
                cues.Count == 0 ? UI.Label("No Cues") : UI.Column(cueButtons)).SetWidth(180f);

            if (cues.Count == 0) return UI.Row(left, UI.Label("Add a Cue to begin."));
            OutputOverlayCueRuntime selected = cues[_selectedOverlayCue];
            return UI.Row(left, BuildOverlayCueEditor(selected).SetFlexGrow(1f));
        }

        private Element BuildOverlayCueEditor(OutputOverlayCueRuntime cue)
        {
            OutputOverlayCueData data = cue.Data;
            var childLayers = cue.Root.Children;
            return UI.Column(
                UI.Row(
                    UI.Field(null, () => data.Name, value =>
                    {
                        data.Name = value;
                        cue.gameObject.name = value;
                    }).SetFlexGrow(1f),
                    UI.Button("Play", cue.Trigger),
                    UI.Button("Stop", cue.Stop),
                    UI.Button("Duplicate", () =>
                    {
                        OutputOverlayCueRuntime duplicate = _outputOverlayStage.DuplicateCue(cue);
                        if (duplicate != null) _selectedOverlayCue = _outputOverlayStage.Cues.Count - 1;
                    }),
                    UI.Button("▲", () => _outputOverlayStage.MoveCue(cue, -1)).SetWidth(32f),
                    UI.Button("▼", () => _outputOverlayStage.MoveCue(cue, 1)).SetWidth(32f),
                    UI.Button("Delete", () => RemoveOverlayCue(cue))),
                UI.Row(
                    UI.Label(() => cue.IsWaiting ? "WAITING" : cue.IsPlaying ? $"PLAYING  {cue.ElapsedTime:0.00}s" : "STOPPED")
                        .SetWidth(130f),
                    UI.Field("Mode", () => data.TriggerMode, value => data.TriggerMode = value),
                    UI.Field("Duration", () => data.Duration, value => data.Duration = Mathf.Max(0f, value))),
                UI.Fold("Cue Settings", UI.Column(
                    UI.Row(
                        UI.Field("Delay", () => data.StartDelay, value => data.StartDelay = Mathf.Max(0f, value)),
                        UI.Field("Speed", () => data.PlaybackSpeed, value => data.PlaybackSpeed = Mathf.Max(0f, value)),
                        UI.Toggle("Restart", () => data.RestartOnTrigger, value => data.RestartOnTrigger = value),
                        UI.Toggle("Loop", () => data.Loop, value => data.Loop = value)),
                    UI.Toggle("On Stage Change", () => data.TriggerOnStageChange,
                        value => data.TriggerOnStageChange = value),
                    UI.Field("Exclusive Group", () => data.ExclusiveGroup, value => data.ExclusiveGroup = value),
                    UI.Field("Trigger MIDI", Binder.Create(data.TriggerButton, typeof(MidiBinding))),
                    UI.Fold("Cue Transform", UI.Field(null, Binder.Create(cue.Root.Params, cue.Root.Params.GetType()))))),
                UI.Box(UI.Row(OverlayDescriptors().Select(descriptor =>
                    UI.Button($"+ {descriptor.DisplayName}", () =>
                        _outputOverlayStage.AddLayer(cue, descriptor.TypeId))).ToArray())),
                childLayers.Length == 0
                    ? UI.Label("No Layers — add Text, Shape, or Group above.")
                    : UI.Column(childLayers.Select(layer => BuildOverlayLayerElement(cue, layer)).ToArray()));
        }

        private Element BuildOverlayLayerElement(OutputOverlayCueRuntime cue, StageLayer layer)
        {
            bool insideGroup = layer.transform.parent != null &&
                               layer.transform.parent.GetComponent<GroupLayer>() is GroupLayer parentGroup &&
                               parentGroup != cue.Root;
            Element selectButton = UI.Button(UI.Label(() => layer.gameObject.name),
                    () => InspectOutputOverlayLayer(cue, layer))
                .SetMinWidth(150f).SetFlexGrow(1f).SetHeight(30f)
                .RegisterUpdateCallback(element =>
                {
                    Color color = GetLayerColor(layer);
                    element.SetBackgroundColor(_inspectedOverlayLayer == layer
                        ? color
                        : layer.Visible ? color * 0.8f : color * 0.5f);
                })
                .RegisterVisualElementAttachedCallback(
                    element => AttachOutputOverlayLayerDragCallbacks(element, cue, layer),
                    DetachOutputOverlayLayerDragCallbacks);
            Element header = UI.Popup(
                UI.Row(
                    UI.Space().SetWidth(layer is GroupLayer ? 0f : 18f),
                    UI.Label(() => _inspectedOverlayLayer == layer ? "▶" : " ").SetWidth(8f),
                    UI.Toggle(null, () => layer.Visible, value => layer.Visible = value).SetWidth(28f),
                    selectButton,
                    UI.Button("▲", () => _outputOverlayStage.MoveLayer(layer, -1)).SetWidth(24f),
                    UI.Button("▼", () => _outputOverlayStage.MoveLayer(layer, 1)).SetWidth(24f)),
                () =>
                {
                    var items = new List<IMenuItem>
                    {
                        new MenuItem("Copy", () => _layerClipboard = _outputOverlayStage.CopyLayer(layer)),
                        new MenuItem("Paste", () => PasteOutputOverlayLayer(cue, layer))
                            { isEnable = _layerClipboard != null },
                        new MenuItem("Delete", () => RemoveOutputOverlayLayer(layer)),
                    };
                    if (insideGroup)
                        items.Add(new MenuItem("Out", () => _outputOverlayStage.MoveLayerOutOfGroup(layer)));
                    return items;
                });

            if (layer is GroupLayer group)
            {
                var children = group.Children.Select(child => BuildOverlayLayerElement(cue, child)).ToList();
                return UI.Fold(header, new Element[]
                {
                    UI.Column(
                        UI.Row(OverlayDescriptors().Select(descriptor =>
                            UI.Button($"+ {descriptor.DisplayName}", () =>
                                _outputOverlayStage.AddLayer(cue, descriptor.TypeId, group.transform))).ToArray()),
                        UI.Button("Move Selected Here", () =>
                        {
                            if (_inspectedOverlayLayer != null)
                                _outputOverlayStage.MoveLayerToGroup(_inspectedOverlayLayer, group);
                        }),
                        children.Count == 0 ? UI.Label("No Layers in Group") : UI.Column(children))
                });
            }
            return header;
        }

        private void InspectOutputOverlayLayer(OutputOverlayCueRuntime cue, StageLayer layer)
        {
            _inspectedLayer = null;
            _inspectedLayerStage = null;
            _inspectedCameraWork = null;
            _inspectedCameraWorkStage = null;
            _inspectedOverlayCue = cue;
            _inspectedOverlayLayer = layer;
            _inspectorElement?.CheckAndRebuild();
            if (_inspectorWindow != null) _inspectorWindow.IsOpen = true;
        }

        private Element CreateOutputOverlayLayerInspectorElement()
        {
            StageLayer layer = _inspectedOverlayLayer;
            if (layer == null) return UI.Label("Select Overlay Layer");
            return UI.Column(
                UI.Label($"Output Overlay / {_inspectedOverlayCue?.Data?.Name}"),
                UI.Row(
                    UI.Toggle(null, () => layer.Visible, value => layer.Visible = value),
                    UI.Field(null, () => layer.gameObject.name, value => layer.gameObject.name = value)
                        .SetFlexGrow(1f)).SetBackgroundColor(GetLayerColor(layer) * 0.5f),
                UI.Field("Order", () => layer.Order, value => layer.Order = value),
                UI.Field(null, Binder.Create(layer.Params, layer.Params.GetType())));
        }

        private void PasteOutputOverlayLayer(OutputOverlayCueRuntime cue, StageLayer reference)
        {
            if (_layerClipboard == null || cue == null || reference == null) return;
            StageLayer pasted = _outputOverlayStage.PasteLayer(
                cue, _layerClipboard, reference.transform.parent, reference.Order);
            if (pasted != null) InspectOutputOverlayLayer(cue, pasted);
        }

        private void RemoveOutputOverlayLayer(StageLayer layer)
        {
            if (_inspectedOverlayLayer == layer)
            {
                _inspectedOverlayCue = null;
                _inspectedOverlayLayer = null;
                _inspectorElement?.CheckAndRebuild();
            }
            _outputOverlayStage.RemoveLayer(layer);
        }

        private void RemoveOverlayCue(OutputOverlayCueRuntime cue)
        {
            if (_inspectedOverlayCue == cue)
            {
                _inspectedOverlayCue = null;
                _inspectedOverlayLayer = null;
                _inspectorElement?.CheckAndRebuild();
            }
            _outputOverlayStage.RemoveCue(cue);
            _selectedOverlayCue = Mathf.Clamp(_selectedOverlayCue, 0,
                Mathf.Max(0, _outputOverlayStage.Cues.Count - 1));
        }

        private void AttachOutputOverlayLayerDragCallbacks(
            VisualElement layerButton,
            OutputOverlayCueRuntime cue,
            StageLayer layer)
        {
            Vector2 pointerStart = default;
            int pointerId = -1;
            bool moved = false;
            VisualElement dropTarget = null;

            layerButton.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            layerButton.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            layerButton.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            layerButton.RegisterCallback<PointerCancelEvent>(OnCancel, TrickleDown.TrickleDown);
            layerButton.userData = new OverlayLayerDragTarget(cue, layer, Unregister);

            void OnDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || pointerId >= 0) return;
                pointerId = evt.pointerId;
                pointerStart = evt.position;
                moved = false;
                InspectOutputOverlayLayer(cue, layer);
                layerButton.CapturePointer(pointerId);
                evt.StopImmediatePropagation();
            }

            void OnMove(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointerId || !layerButton.HasPointerCapture(pointerId)) return;
                if (!moved && ((Vector2)evt.position - pointerStart).sqrMagnitude < 16f) return;
                moved = true;
                layerButton.style.opacity = 0.65f;
                VisualElement next = FindOutputOverlayDropTarget(layerButton, cue, layer, evt.position);
                if (next == dropTarget) return;
                if (dropTarget != null) dropTarget.style.opacity = StyleKeyword.Null;
                dropTarget = next;
                if (dropTarget != null) dropTarget.style.opacity = 0.65f;
            }

            void OnUp(PointerUpEvent evt)
            {
                if (evt.pointerId != pointerId) return;
                Finish(evt, true);
            }

            void OnCancel(PointerCancelEvent evt)
            {
                if (evt.pointerId != pointerId) return;
                Finish(evt, false);
            }

            void Finish(EventBase evt, bool commit)
            {
                int capturedId = pointerId;
                pointerId = -1;
                if (commit && moved && dropTarget?.userData is OverlayLayerDragTarget target &&
                    target.Layer != layer)
                {
                    var siblings = layer.transform.parent.GetComponentsInChildren<StageLayer>(true)
                        .Where(item => item != null && item.transform.parent == layer.transform.parent)
                        .OrderBy(item => item.Order).ToList();
                    _outputOverlayStage.MoveLayerToIndex(layer, siblings.IndexOf(target.Layer));
                }
                ResetVisuals();
                if (capturedId >= 0 && layerButton.HasPointerCapture(capturedId))
                    layerButton.ReleasePointer(capturedId);
                evt.StopImmediatePropagation();
            }

            void ResetVisuals()
            {
                layerButton.style.opacity = StyleKeyword.Null;
                if (dropTarget != null) dropTarget.style.opacity = StyleKeyword.Null;
                dropTarget = null;
            }

            void Unregister()
            {
                ResetVisuals();
                layerButton.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
                layerButton.UnregisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
                layerButton.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
                layerButton.UnregisterCallback<PointerCancelEvent>(OnCancel, TrickleDown.TrickleDown);
            }
        }

        private static VisualElement FindOutputOverlayDropTarget(
            VisualElement source,
            OutputOverlayCueRuntime cue,
            StageLayer layer,
            Vector2 pointerPosition)
        {
            if (source.panel == null) return null;
            var targets = source.panel.visualTree.Query<VisualElement>().ToList()
                .Where(element => element.userData is OverlayLayerDragTarget binding &&
                                  binding.Cue == cue && binding.Layer != layer &&
                                  binding.Layer.transform.parent == layer.transform.parent)
                .ToList();
            if (targets.Count == 0) return null;
            return targets
                .Where(element => element.worldBound.Contains(pointerPosition))
                .OrderBy(element => (element.worldBound.center - pointerPosition).sqrMagnitude)
                .FirstOrDefault();
        }

        private static void DetachOutputOverlayLayerDragCallbacks(VisualElement element)
        {
            if (element.userData is not OverlayLayerDragTarget target) return;
            target.Unregister();
            element.userData = null;
        }

        private sealed class OverlayLayerDragTarget
        {
            public readonly OutputOverlayCueRuntime Cue;
            public readonly StageLayer Layer;
            private readonly Action _unregister;

            public OverlayLayerDragTarget(OutputOverlayCueRuntime cue, StageLayer layer, Action unregister)
            {
                Cue = cue;
                Layer = layer;
                _unregister = unregister;
            }

            public void Unregister() => _unregister?.Invoke();
        }

        private static IEnumerable<LayerDescriptor> OverlayDescriptors() =>
            new[] { LayerRegistry.Text, LayerRegistry.Shape, LayerRegistry.Group };
    }
}
