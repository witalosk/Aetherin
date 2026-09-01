using System;
using System.Collections.Generic;
using System.Linq;
using RosettaUI;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    [Serializable]
    public class StageManagerParams : IParams
    {
        public MidiCcBinding CrossFader = new(ApcMiniMk2.MasterFaderCc);

        [Tooltip("即時モード (フェーダーを介さずNextへの操作を最終出力へ即時反映) を切り替えるボタン")]
        public MidiBinding ImmediateModeButton = new();

        [Tooltip("Nextに出すステージを選ぶボタン (登録ステージと同じ並び)")]
        public List<MidiBinding> StageSelectButtons = new();

        public int CurrentStageIndex;
        public int NextStageIndex;

        [Tooltip("CameraStageで複製元が映り込まないようにするためのオフセット")]
        public Vector3 NextStageOffset = new(0f, 1000f, 0f);

        [Tooltip("フェーダーがこの値まで振り切ったらCurrent / Nextを入れ替える")]
        [Range(0.9f, 1f)]
        public float SwapThreshold = 0.99f;

        [Tooltip("Current / Next / 合成後Outputへ即時適用するポストエフェクト。メインCrossFaderからは独立しています")]
        public PostEffectManagerParams PostEffects = new();
    }

    /// <summary>
    /// シーンに置かれたステージを非アクティブのテンプレートとして扱い、
    /// そのクローンでCurrent / Nextの2系統を作ってクロスフェーダーで最終出力をブレンドするマネージャ
    /// MIDIコンから操作するステージは基本的にNext側にして、フェーダーで本番出力に送る運用を想定
    ///
    /// スワップ時はNext側を昇格したCurrentのコピーとして作り直すため、
    /// どんな子オブジェクト構成のステージでも、いま出ている絵から続きを操作できる
    /// </summary>
    public class StageManager : MonoBehaviour, IDeckStateProvider, ISaveAndUiTarget, ICustomSaveTarget
    {
        public IParams Params => _params;
        public bool FoldParams => true;
        public string Category => UiCategory.Main;
        public string SaveId => "CameraStageLayers";

        /// <summary> MIDIコンやUIからの変更はこちらに書き込まれる </summary>
        public DeckState NextState { get; private set; } = new();

        /// <summary> クロスフェード済みの最終出力 </summary>
        public RenderTexture OutputTexture { get; private set; }

        /// <summary> フェーダーを介さず、Nextへの操作が即座に最終出力へ反映されるモード </summary>
        public bool IsImmediateMode { get; private set; }

        /// <summary>
        /// クロスフェーダーの現在値 (0: Current / 1: Next)
        /// スワップ後は物理フェーダーの向きを内部で反転して扱う
        /// </summary>
        public float CrossFade
        {
            get
            {
                float raw = _params.CrossFader.GetValue();
                return _isFaderFlipped ? 1f - raw : raw;
            }
        }

        [SerializeField] private Renderer _outputRenderer;
        [SerializeField] private Renderer _currentPreviewRenderer;
        [SerializeField] private Renderer _nextPreviewRenderer;
        [Space]
        [SerializeField] private Shader _crossFadeShader;
        [SerializeField] private Shader _postEffectShader;
        [Space]
        [SerializeField] private List<StageBase> _stages;
        [SerializeField] private StageManagerParams _params = new();

        private static readonly Color StageLedColor = new(0.2f, 0.9f, 1f);

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int TexAId = Shader.PropertyToID("_TexA");
        private static readonly int TexBId = Shader.PropertyToID("_TexB");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private List<StageBase> _currentStages;
        private List<StageBase> _nextStages;
        private Vector3 _currentSlotOffset;
        private Vector3 _nextSlotOffset;
        private bool _isFaderFlipped;

        /// <summary> デッキを作り直すたびに増える。UIが参照先の作り直しを検知するために使う </summary>
        private int _deckRevision;

        private readonly DeckState _currentState = new();

        private IContainer _container;
        private IApplicationManager _applicationManager;
        private IAudioFeatureProvider _audioFeatureProvider;
        private IBeatManager _beatManager;
        private Material _crossFadeMaterial;
        private RenderTexture _crossFadeTexture;
        private PostEffectManager _postEffectManager;
        private Texture _currentPostTexture;
        private Texture _nextPostTexture;
        private CameraStageSaveData _pendingCameraStageData;

        [Inject]
        public void Construct(
            IContainer container,
            IApplicationManager applicationManager,
            IAudioFeatureProvider audioFeatureProvider,
            IBeatManager beatManager)
        {
            _container = container;
            _applicationManager = applicationManager;
            _audioFeatureProvider = audioFeatureProvider;
            _beatManager = beatManager;
        }

        public DeckState GetState(StageDeck deck) => deck == StageDeck.Current ? _currentState : NextState;

        private void Start()
        {
            OutputTexture = new RenderTexture(_applicationManager.Resolution.x, _applicationManager.Resolution.y, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _crossFadeTexture = new RenderTexture(_applicationManager.Resolution.x, _applicationManager.Resolution.y, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _crossFadeMaterial = new Material(_crossFadeShader);
            _postEffectManager = new PostEffectManager(
                _postEffectShader != null ? _postEffectShader : Shader.Find("Hidden/Aetherin/PostEffectStack"));

            BuildDecks();
            ApplyPendingCameraStageData();

            if (_outputRenderer != null) _outputRenderer.material.SetTexture(MainTexId, OutputTexture);
        }

        /// <summary>
        /// シーン上のステージを非アクティブのテンプレートにして、Current / Nextともクローンで構成する
        /// </summary>
        private void BuildDecks()
        {
            _currentSlotOffset = Vector3.zero;
            _nextSlotOffset = _params.NextStageOffset;

            _currentStages = new List<StageBase>();
            _nextStages = new List<StageBase>();

            foreach (var template in _stages)
            {
                _currentStages.Add(CloneStage(template, StageDeck.Current, _currentSlotOffset, template.name));
                _nextStages.Add(CloneStage(template, StageDeck.Next, _nextSlotOffset, template.name));
                template.gameObject.SetActive(false);
            }

            _deckRevision++;
        }

        /// <summary>
        /// クローンはシーン起動時のInjectに含まれないため、コンテナ経由でInstantiateして注入する
        /// positionDeltaはCameraStageなどシーン上に実体を持つステージが互いに映り込まないためのずらし
        /// </summary>
        private StageBase CloneStage(StageBase source, StageDeck deck, Vector3 positionDelta, string baseName)
        {
            var clone = _container.Instantiate(source.gameObject, source.transform.parent, true);
            clone.name = $"{baseName} ({deck})";
            clone.transform.position = source.transform.position + positionDelta;
            clone.SetActive(true);

            var stage = clone.GetComponent<StageBase>();
            stage.Deck = deck;
            return stage;
        }

        // 各ステージがUpdateで描画した後にブレンドする
        private void LateUpdate()
        {
            if (_crossFadeMaterial == null || OutputTexture == null || _currentStages == null) return;

            if (_params.ImmediateModeButton.WasNoteOn) SetImmediateMode(!IsImmediateMode);
            _params.ImmediateModeButton.SetLed(IsImmediateMode ? Color.red : Color.red * 0.15f);

            UpdateStageSelect();

            if (!IsImmediateMode && CrossFade >= _params.SwapThreshold) SwapDecks();

            var currentTexture = GetStageTexture(_currentStages, _params.CurrentStageIndex);
            var nextTexture = GetStageTexture(_nextStages, _params.NextStageIndex);

            var modulationContext = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, true);
            _params.PostEffects ??= new PostEffectManagerParams();
            _currentPostTexture = _postEffectManager.ProcessCurrent(
                currentTexture, _params.PostEffects.Current, modulationContext);
            _nextPostTexture = _postEffectManager.ProcessNext(
                nextTexture, _params.PostEffects.Next, modulationContext);

            _crossFadeMaterial.SetTexture(TexAId, _currentPostTexture);
            _crossFadeMaterial.SetTexture(TexBId, _nextPostTexture);
            _crossFadeMaterial.SetFloat(FadeId, IsImmediateMode ? 1f : CrossFade);
            Graphics.Blit(null, _crossFadeTexture, _crossFadeMaterial);
            var outputPostTexture = _postEffectManager.ProcessOutput(
                _crossFadeTexture, _params.PostEffects.Output, modulationContext);
            Graphics.Blit(outputPostTexture, OutputTexture);

            if (_currentPreviewRenderer != null) _currentPreviewRenderer.material.SetTexture(MainTexId, _currentPostTexture);
            if (_nextPreviewRenderer != null) _nextPreviewRenderer.material.SetTexture(MainTexId, _nextPostTexture);
        }

        /// <summary>
        /// Nextに出すステージをボタンで選択し、LEDに選択状態を表示する
        /// (点滅: Nextに選択中 / 点灯: Currentに表示中 / 暗: それ以外)
        /// </summary>
        private void UpdateStageSelect()
        {
            int count = Mathf.Min(_stages.Count, _params.StageSelectButtons.Count);

            for (int i = 0; i < count; i++)
            {
                var button = _params.StageSelectButtons[i];
                if (button.WasNoteOn) _params.NextStageIndex = i;

                var ledColor = StageLedColor * 0.15f;
                if (i == _params.NextStageIndex) ledColor = StageLedColor * (Mathf.Sin(Time.time * 20f) * 0.5f + 0.5f);
                else if (i == _params.CurrentStageIndex) ledColor = StageLedColor;
                button.SetLed(ledColor);
            }
        }

        /// <summary>
        /// 解除時は表示中のNextをCurrentへ昇格させ、フェーダー操作へ自然に戻す
        /// </summary>
        public void SetImmediateMode(bool enabled)
        {
            if (IsImmediateMode == enabled) return;

            IsImmediateMode = enabled;
            if (!enabled) SwapDecks();
        }

        /// <summary>
        /// 出力される絵は変えずに、以降のMIDI操作対象 (Next) を裏側のデッキに切り替える
        /// </summary>
        private void SwapDecks()
        {
            // 実効フェードが0側 (=今まで見えていたNextをCurrentとして見続ける側) になる向きを選ぶ
            _isFaderFlipped = _params.CrossFader.GetValue() > 0.5f;

            // 見えていたNextをCurrentへ昇格させる
            var retiredStages = _currentStages;
            _currentStages = _nextStages;
            (_currentSlotOffset, _nextSlotOffset) = (_nextSlotOffset, _currentSlotOffset);

            // NextはCurrentの複製として続きを操作するため、選択も昇格したステージと同じものを指し続ける
            _params.CurrentStageIndex = _params.NextStageIndex;

            for (int i = 0; i < _currentStages.Count; i++)
            {
                var stage = _currentStages[i];
                if (stage == null) continue;

                stage.Deck = StageDeck.Current;
                stage.gameObject.name = $"{_stages[i].name} ({StageDeck.Current})";
            }

            // 引退した旧Currentは破棄し、Nextは昇格したCurrentのコピーとして作り直す
            // (いま出ている絵から続きを操作できるようにする)
            foreach (var stage in retiredStages)
            {
                if (stage != null) Destroy(stage.gameObject);
            }

            _nextStages = new List<StageBase>(_currentStages.Count);
            for (int i = 0; i < _currentStages.Count; i++)
            {
                var source = _currentStages[i];
                _nextStages.Add(source == null
                    ? null
                    : CloneStage(source, StageDeck.Next, _nextSlotOffset - _currentSlotOffset, _stages[i].name));
            }

            // 見えていたNextの状態をCurrentに引き継ぎ、スワップで見た目が変わらないようにする
            _currentState.CopyFrom(NextState);

            _deckRevision++;
        }

        private static Texture GetStageTexture(List<StageBase> stages, int index)
        {
            if (stages.Count == 0) return Texture2D.blackTexture;

            var stage = stages[Mathf.Clamp(index, 0, stages.Count - 1)];
            return stage != null && stage.OutputTexture != null ? stage.OutputTexture : Texture2D.blackTexture;
        }

        public string CaptureSaveData()
        {
            var data = new CameraStageSaveData();
            if (_nextStages == null) return JsonUtility.ToJson(data);

            for (int i = 0; i < _nextStages.Count; i++)
            {
                if (_nextStages[i] is not CameraStage stage) continue;
                data.Stages.Add(new CameraStageLayersSaveData { StageIndex = i, Layers = stage.CaptureLayers() });
            }

            return JsonUtility.ToJson(data);
        }

        public void RestoreSaveData(string json)
        {
            _pendingCameraStageData = JsonUtility.FromJson<CameraStageSaveData>(json);
            if (_nextStages != null) ApplyPendingCameraStageData();
        }

        private void ApplyPendingCameraStageData()
        {
            if (_pendingCameraStageData?.Stages == null) return;

            foreach (var savedStage in _pendingCameraStageData.Stages)
            {
                if (savedStage == null) continue;
                if (savedStage.StageIndex < 0 || savedStage.StageIndex >= _nextStages.Count) continue;

                if (_nextStages[savedStage.StageIndex] is CameraStage nextStage)
                    nextStage.RestoreLayers(savedStage.Layers);
                if (_currentStages[savedStage.StageIndex] is CameraStage currentStage)
                    currentStage.RestoreLayers(savedStage.Layers);
            }

            _deckRevision++;
            _pendingCameraStageData = null;
        }

        private void OnDestroy()
        {
            if (OutputTexture != null) OutputTexture.Release();
            if (_crossFadeTexture != null) _crossFadeTexture.Release();
            if (_crossFadeMaterial != null) Destroy(_crossFadeMaterial);
            _postEffectManager?.Dispose();
        }

        /// <summary>
        /// ウィンドウのリサイズにImageが追従するプレビューウィンドウを作る
        /// Imageはテクスチャの実サイズを本来のサイズとして持つため、
        /// ウィンドウ側に初期サイズを与えて、Imageは固定サイズを持たせずflexで追従させる
        /// </summary>
        private static Element CreatePreviewWindowLauncher(string title, Func<Texture> readTexture)
        {
            return UI.WindowLauncher(title,
                UI.Window(title,
                    UI.Image(readTexture)
                        .SetMinWidth(160f)
                        .SetMinHeight(90f)
                        .SetFlexGrow(1f)
                        .SetFlexShrink(1f)
                        .SetMaxWidth(800f).SetMaxHeight(450f)
                ));
        }

        /// <summary>
        /// 各ステージのレイヤーを編集するウィンドウを開くランチャーの一覧
        /// 操作対象はMIDIコンと同じNextデッキのインスタンス
        /// (デッキはスワップで作り直されるため、_deckRevisionの変化で中身を作り直す)
        /// </summary>
        private Element CreateStageListElement(IReadOnlyList<string> stageNames)
        {
            return UI.Fold("Stages",
                Enumerable.Range(0, stageNames.Count).Select(index => UI.Row(
                    UI.Label(stageNames[index]).SetWidth(120f),
                    UI.WindowLauncher("Layers",
                        UI.Window($"{stageNames[index]} Layers",
                            UI.DynamicElementOnStatusChanged(
                                readStatus: () => _deckRevision * 100000 +
                                    ((_nextStages != null && index < _nextStages.Count && _nextStages[index] is CameraStage cameraStage)
                                        ? cameraStage.LayerRevision
                                        : 0),
                                build: _ => CreateLayerListElement(index))
                        ).SetWidth(400f))
                )));
        }

        private Element CreateLayerListElement(int stageIndex)
        {
            var stage = _nextStages != null && stageIndex < _nextStages.Count ? _nextStages[stageIndex] : null;
            if (stage == null) return UI.Label("ステージが構築されていません");

            if (stage is not CameraStage cameraStage) return UI.Label("このステージはレイヤー編集に未対応です");

            var layers = stage.Layers;

            return UI.Column(
                UI.Row(
                    UI.Button("Add 2D Shape", () => cameraStage.AddShapeLayer()),
                    UI.Button("Add 3D Primitive", () => cameraStage.AddPrimitive3DLayer())),
                layers.Count == 0
                    ? UI.Label("レイヤーがありません")
                    : UI.Column(layers.Select(layer => CreateLayerElement(cameraStage, layer))));
        }

        private static Element CreateLayerElement(CameraStage stage, StageLayer layer)
        {
            return UI.Fold(
                UI.Row(
                    UI.Label(() => layer.Visible ? "●" : "○"),
                    UI.Field(null,
                            () => layer.gameObject.name,
                            value => layer.gameObject.name = value)
                        .SetWidth(180f)),
                UI.Row(
                    UI.Toggle("Visible", () => layer.Visible, value => layer.Visible = value),
                    UI.Button("↑", () => stage.MoveLayer(layer, -1)),
                    UI.Button("↓", () => stage.MoveLayer(layer, 1)),
                    UI.Button("Delete", () => stage.RemoveLayer(layer))),
                new[]
                {
                    UI.Field("Order", () => layer.Order, value => layer.Order = value),
                    UI.Field(null, Binder.Create(layer.Params, layer.Params.GetType()))
                }
            );
        }

        public Element AdditiveUi()
        {
            var stageNames = _stages
                .Select((s, i) => s == null ? $"Stage {i}" : (string.IsNullOrEmpty(s.StageName) ? s.name : s.StageName))
                .ToList();

            if (stageNames.Count == 0) return UI.Label("ステージが登録されていません");

            return UI.Column(
                UI.Row(
                    UI.WindowLauncher("Previews", UI.Window(
                        UI.Column(
                            UI.Label("Next"),
                            UI.Image(() => GetStageTexture(_nextStages, _params.NextStageIndex))
                                .SetMinWidth(160f).SetMinHeight(90f)
                                .SetFlexGrow(1f).SetFlexShrink(1f)
                                .SetMaxWidth(600f).SetMaxHeight(310f),
                            UI.Label("Output"),
                            UI.Image(() => OutputTexture)
                                .SetMinWidth(160f).SetMinHeight(90f)
                                .SetFlexGrow(1f).SetFlexShrink(1f)
                                .SetMaxWidth(600f).SetMaxHeight(310f)
                        )
                    )),
                    CreatePreviewWindowLauncher("Output Preview", () => OutputTexture),
                    CreatePreviewWindowLauncher("Current Preview", () => _currentPostTexture),
                    CreatePreviewWindowLauncher("Next Preview", () => _nextPostTexture)
                ),
                UI.Dropdown("Current",
                    () => Mathf.Clamp(_params.CurrentStageIndex, 0, stageNames.Count - 1),
                    value => _params.CurrentStageIndex = value,
                    stageNames),
                UI.Dropdown("Next",
                    () => Mathf.Clamp(_params.NextStageIndex, 0, stageNames.Count - 1),
                    value => _params.NextStageIndex = value,
                    stageNames),
                CreateStageListElement(stageNames),
                UI.SliderReadOnly("CrossFade (Current ← → Next)", () => CrossFade, 0f, 1f),
                UI.Toggle("Immediate Mode", () => IsImmediateMode, SetImmediateMode),
                UI.Label(() => IsImmediateMode
                    ? "即時モード中: Nextが直接出力されています (フェーダー無効)"
                    : _isFaderFlipped ? "フェーダー: 下に倒すとNextが出ます" : "フェーダー: 上に倒すとNextが出ます")
            );
        }
    }

    [Serializable]
    public sealed class CameraStageSaveData
    {
        public int Version = 1;
        public List<CameraStageLayersSaveData> Stages = new();
    }

    [Serializable]
    public sealed class CameraStageLayersSaveData
    {
        public int StageIndex;
        public List<CameraStageLayerSaveData> Layers = new();
    }
}
