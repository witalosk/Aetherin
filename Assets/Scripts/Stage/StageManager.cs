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

        [Tooltip("選択中のNext CameraStageにあるレイヤーを、リストのインデックス順でON/OFFするPad")]
        public List<MidiBinding> LayerToggleButtons = new();

        [Tooltip("選択中のNext CameraStageにあるカメラワークデッキを選ぶPad")]
        public List<MidiBinding> CameraWorkDeckButtons = new();

        [Tooltip("カメラワークの切り替え周期を Beat / Bar / 2 Bars / 4 Bars / Manual の順で選ぶPad")]
        public List<MidiBinding> CameraWorkTimingButtons = new();

        [Tooltip("Manual時に次のカメラワークへ進めるPad")]
        public MidiBinding CameraWorkManualButton = new();
        
        public int CurrentStageIndex;
        public int NextStageIndex;

        [Tooltip("CameraStageで複製元が映り込まないようにするためのオフセット")]
        public Vector3 NextStageOffset = new(0f, 1000f, 0f);

        [Tooltip("フェーダーがこの値まで振り切ったらCurrent / Nextを入れ替える")]
        [Range(0.9f, 1f)]
        public float SwapThreshold = 0.99f;

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
        public static CameraWorkSwitchTiming CurrentCameraWorkTiming { get; private set; } = CameraWorkSwitchTiming.Manual;
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
        private StageLayer _inspectedLayer;
        private CameraStage _inspectedLayerStage;
        private int _inspectedLayerColorIndex;
        private CameraWorkRecipe _inspectedCameraWork;
        private CameraStage _inspectedCameraWorkStage;
        private DynamicElement _inspectorElement;
        private WindowElement _inspectorWindow;
        private int _selectedStageUiIndex;

        private readonly DeckState _currentState = new();

        private IContainer _container;
        private IApplicationManager _applicationManager;
        private Material _crossFadeMaterial;
        private RenderTexture _crossFadeTexture;
        private IPostEffectManager _postEffectManager;
        private Texture _currentPostTexture;
        private Texture _nextPostTexture;
        private CameraStageSaveData _pendingCameraStageData;
        private readonly HashSet<string> _runtimeStageIds = new();

        [Inject]
        public void Construct(
            IContainer container,
            IApplicationManager applicationManager,
            IPostEffectManager postEffectManager)
        {
            _container = container;
            _applicationManager = applicationManager;
            _postEffectManager = postEffectManager;
        }

        public DeckState GetState(StageDeck deck) => deck == StageDeck.Current ? _currentState : NextState;

        private void Start()
        {
            OutputTexture = new RenderTexture(_applicationManager.Resolution.x, _applicationManager.Resolution.y, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _crossFadeTexture = new RenderTexture(_applicationManager.Resolution.x, _applicationManager.Resolution.y, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _crossFadeMaterial = new Material(_crossFadeShader);
            BuildDecks();
            ApplyPendingCameraStageData();

            if (_outputRenderer != null) _outputRenderer.material.SetTexture(MainTexId, OutputTexture);
        }

        /// <summary>
        /// シーン上のステージを非アクティブのテンプレートにして、Current / Nextともクローンで構成する
        /// </summary>
        private void BuildDecks()
        {
            _stages ??= new List<StageBase>();
            _currentSlotOffset = Vector3.zero;
            _nextSlotOffset = _params.NextStageOffset;

            _currentStages = new List<StageBase>();
            _nextStages = new List<StageBase>();

            for (int i = 0; i < _stages.Count; i++)
            {
                StageBase template = _stages[i];
                if (template == null)
                {
                    _currentStages.Add(null);
                    _nextStages.Add(null);
                    continue;
                }
                template.EnsureStageId();
                StageBase current = CloneStage(template, StageDeck.Current, _currentSlotOffset, template.name);
                StageBase next = CloneStage(template, StageDeck.Next, _nextSlotOffset, template.name);
                if (current is CameraStage currentCamera) currentCamera.ConfigureCinemachineChannel(i * 2);
                if (next is CameraStage nextCamera) nextCamera.ConfigureCinemachineChannel(i * 2 + 1);
                _currentStages.Add(current);
                _nextStages.Add(next);
                template.gameObject.SetActive(false);
            }

            _deckRevision++;
        }

        public CameraStage AddCameraStage(string stageName = "Camera Stage", string stageId = null)
        {
            _stages ??= new List<StageBase>();
            string resolvedName = string.IsNullOrWhiteSpace(stageName) ? "Camera Stage" : stageName.Trim();
            var templateObject = new GameObject(resolvedName);
            templateObject.transform.SetParent(transform, false);
            var template = templateObject.AddComponent<CameraStage>();
            template.SetIdentity(stageId, resolvedName);

            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(templateObject.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            cameraObject.AddComponent<Camera>();

            _stages.Add(template);
            _runtimeStageIds.Add(template.StageId);
            templateObject.SetActive(false);
            if (_currentStages == null || _nextStages == null) return template;

            int index = _stages.Count - 1;
            var current = CloneStage(template, StageDeck.Current, _currentSlotOffset, resolvedName) as CameraStage;
            var next = CloneStage(template, StageDeck.Next, _nextSlotOffset, resolvedName) as CameraStage;
            _currentStages.Add(current);
            _nextStages.Add(next);
            ConfigureStageChannels();
            _params.CurrentStageIndex = Mathf.Clamp(_params.CurrentStageIndex, 0, _stages.Count - 1);
            _params.NextStageIndex = index;
            _deckRevision++;
            return next;
        }

        public CameraStage DuplicateCameraStage(string stageId)
        {
            int index = FindStageIndex(stageId);
            if (index < 0 || _nextStages == null || _nextStages[index] is not CameraStage source) return null;

            string sourceName = GetStageDisplayName(_stages[index], index);
            string duplicateName = $"{sourceName} Copy";
            List<CameraStageLayerSaveData> layers = source.CaptureLayers();
            List<CameraWorkDeck> cameraWorkDecks = source.CaptureCameraWorkDecks();
            CameraStage next = AddCameraStage(duplicateName);
            int newIndex = _stages.Count - 1;
            next?.RestoreLayers(layers);
            next?.RestoreCameraWorkDecks(cameraWorkDecks);
            if (_currentStages[newIndex] is CameraStage current)
            {
                current.RestoreLayers(layers);
                current.RestoreCameraWorkDecks(cameraWorkDecks);
            }
            return next;
        }

        public bool RemoveCameraStage(string stageId)
        {
            int index = FindStageIndex(stageId);
            if (index < 0 || _stages[index] is not CameraStage) return false;

            DestroyStageAt(_currentStages, index);
            DestroyStageAt(_nextStages, index);
            StageBase template = _stages[index];
            _stages.RemoveAt(index);
            if (template != null)
            {
                _runtimeStageIds.Remove(template.StageId);
                Destroy(template.gameObject);
            }
            ConfigureStageChannels();
            int maxIndex = Mathf.Max(0, _stages.Count - 1);
            _params.CurrentStageIndex = Mathf.Clamp(_params.CurrentStageIndex, 0, maxIndex);
            _params.NextStageIndex = Mathf.Clamp(_params.NextStageIndex, 0, maxIndex);
            _deckRevision++;
            return true;
        }

        public bool RenameCameraStage(string stageId, string stageName)
        {
            int index = FindStageIndex(stageId);
            if (index < 0 || _stages[index] is not CameraStage || string.IsNullOrWhiteSpace(stageName)) return false;
            string resolvedName = stageName.Trim();
            SetStageName(_stages[index], resolvedName, null);
            SetStageName(_currentStages, index, resolvedName, StageDeck.Current);
            SetStageName(_nextStages, index, resolvedName, StageDeck.Next);
            _deckRevision++;
            return true;
        }

        private int FindStageIndex(string stageId) =>
            string.IsNullOrEmpty(stageId) || _stages == null
                ? -1
                : _stages.FindIndex(stage => stage != null && stage.StageId == stageId);

        private static void DestroyStageAt(List<StageBase> stages, int index)
        {
            if (stages == null || index < 0 || index >= stages.Count) return;
            StageBase stage = stages[index];
            stages.RemoveAt(index);
            if (stage == null) return;
            stage.gameObject.SetActive(false);
            Destroy(stage.gameObject);
        }

        private static void SetStageName(List<StageBase> stages, int index, string stageName, StageDeck deck)
        {
            if (stages == null || index < 0 || index >= stages.Count) return;
            SetStageName(stages[index], stageName, deck);
        }

        private static void SetStageName(StageBase stage, string stageName, StageDeck? deck)
        {
            if (stage == null) return;
            stage.SetIdentity(stage.StageId, stageName);
            stage.gameObject.name = deck.HasValue ? $"{stageName} ({deck.Value})" : stageName;
        }

        private void ConfigureStageChannels()
        {
            for (int i = 0; i < _stages.Count; i++)
            {
                if (_currentStages != null && i < _currentStages.Count &&
                    _currentStages[i] is CameraStage currentCamera)
                    currentCamera.ConfigureCinemachineChannel(i * 2);
                if (_nextStages != null && i < _nextStages.Count &&
                    _nextStages[i] is CameraStage nextCamera)
                    nextCamera.ConfigureCinemachineChannel(i * 2 + 1);
            }
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
            UpdateStageActivity();
            UpdateLayerToggleButtons();
            UpdateCameraWorkButtons();

            if (!IsImmediateMode && CrossFade >= _params.SwapThreshold) SwapDecks();

            var currentTexture = GetStageTexture(_currentStages, _params.CurrentStageIndex);
            var nextTexture = GetStageTexture(_nextStages, _params.NextStageIndex);

            _currentPostTexture = _postEffectManager.ProcessCurrent(currentTexture);
            _nextPostTexture = _postEffectManager.ProcessNext(nextTexture);

            _crossFadeMaterial.SetTexture(TexAId, _currentPostTexture);
            _crossFadeMaterial.SetTexture(TexBId, _nextPostTexture);
            _crossFadeMaterial.SetFloat(FadeId, IsImmediateMode ? 1f : CrossFade);
            Graphics.Blit(null, _crossFadeTexture, _crossFadeMaterial);
            Texture outputTexture = _postEffectManager.ProcessOutput(_crossFadeTexture);
            Graphics.Blit(outputTexture, OutputTexture);

            if (_currentPreviewRenderer != null) _currentPreviewRenderer.material.SetTexture(MainTexId, _currentPostTexture);
            if (_nextPreviewRenderer != null) _nextPreviewRenderer.material.SetTexture(MainTexId, _nextPostTexture);
        }

        /// <summary>
        /// 同じデッキ内の別ステージが選択中ステージのカメラへ映り込まないよう、
        /// Current / Next それぞれで選択中のステージだけを有効にする。
        /// 特にランタイム追加ステージは同じスロット位置から生成されるため、この分離が必要。
        /// </summary>
        private void UpdateStageActivity()
        {
            SetOnlySelectedStageActive(_currentStages, _params.CurrentStageIndex);
            SetOnlySelectedStageActive(_nextStages, _params.NextStageIndex);
        }

        private static void SetOnlySelectedStageActive(IReadOnlyList<StageBase> stages, int selectedIndex)
        {
            if (stages == null || stages.Count == 0) return;
            selectedIndex = Mathf.Clamp(selectedIndex, 0, stages.Count - 1);

            for (int i = 0; i < stages.Count; i++)
            {
                StageBase stage = stages[i];
                if (stage != null && stage.gameObject.activeSelf != (i == selectedIndex))
                    stage.gameObject.SetActive(i == selectedIndex);
            }
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
        /// Binding自体をレイヤーへ持たせず、選択中Nextステージのレイヤーリスト位置へ対応させる。
        /// レイヤーを並び替えた場合、Padの対象も新しいインデックスへ追従する。
        /// </summary>
        private void UpdateLayerToggleButtons()
        {
            _params.LayerToggleButtons ??= new List<MidiBinding>();

            IReadOnlyList<StageLayer> layers = null;
            if (_nextStages != null && _nextStages.Count > 0)
            {
                int stageIndex = Mathf.Clamp(_params.NextStageIndex, 0, _nextStages.Count - 1);
                if (_nextStages[stageIndex] is CameraStage cameraStage) layers = cameraStage.Layers;
            }

            for (int index = 0; index < _params.LayerToggleButtons.Count; index++)
            {
                MidiBinding button = _params.LayerToggleButtons[index];
                if (button == null) continue;

                StageLayer layer = layers != null && index < layers.Count ? layers[index] : null;
                if (layer == null)
                {
                    button.ClearLed();
                    continue;
                }

                if (button.WasNoteOn) layer.Visible = !layer.Visible;
                Color layerColor = GetLayerColor(index);
                button.SetLed(layer.Visible ? layerColor : layerColor * 0.25f);
            }
        }

        /// <summary>
        /// Layer indexから再現可能な色を作る。UIとPadで同じ関数を使うため、
        /// Layerの並び順とPadの色が常に対応する。
        /// </summary>
        private static Color GetLayerColor(int index)
        {
            unchecked
            {
                uint hash = (uint)index;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;

                float hue = (hash & 0x00FFFFFFu) / 16777216f;
                float saturation = Mathf.Lerp(0.72f, 0.9f, ((hash >> 24) & 0xFFu) / 255f);
                float value = Mathf.Lerp(0.82f, 1f, ((hash >> 8) & 0xFFu) / 255f);
                return Color.HSVToRGB(hue, saturation, value);
            }
        }

        private void UpdateCameraWorkButtons()
        {
            CameraStage nextStage = GetCameraStage(_nextStages, _params.NextStageIndex);
            CameraStage currentStage = GetCameraStage(_currentStages, _params.CurrentStageIndex);

            _params.CameraWorkDeckButtons ??= new List<MidiBinding>();
            for (int i = 0; i < _params.CameraWorkDeckButtons.Count; i++)
            {
                MidiBinding button = _params.CameraWorkDeckButtons[i];
                if (button == null) continue;
                bool available = nextStage != null && i < nextStage.CameraWorkDecks.Count;
                if (!available) { button.ClearLed(); continue; }
                if (button.WasNoteOn)
                {
                    nextStage.SelectCameraWorkDeck(i);
                    if (currentStage != null && i < currentStage.CameraWorkDecks.Count)
                        currentStage.SelectCameraWorkDeck(i);
                }
                button.SetLed(i == nextStage.SelectedCameraWorkDeck ? StageLedColor * 0.5f : StageLedColor * 0.25f);
            }

            _params.CameraWorkTimingButtons ??= new List<MidiBinding>();
            int timingCount = Enum.GetValues(typeof(CameraWorkSwitchTiming)).Length;
            for (int i = 0; i < _params.CameraWorkTimingButtons.Count; i++)
            {
                MidiBinding button = _params.CameraWorkTimingButtons[i];
                if (button == null) continue;
                if (i >= timingCount) { button.ClearLed(); continue; }
                if (button.WasNoteOn)
                {
                    CurrentCameraWorkTiming = (CameraWorkSwitchTiming)i;
                }
                button.SetLed(i == (int)CurrentCameraWorkTiming ? Color.yellow * 0.5f : Color.yellow * 0.25f);
            }

            _params.CameraWorkManualButton ??= new MidiBinding();
            if (_params.CameraWorkManualButton.WasNoteOn)
            {
                nextStage?.AdvanceCameraWork();
                currentStage?.AdvanceCameraWork();
            }
            _params.CameraWorkManualButton.SetLed(_params.CameraWorkManualButton.WasNoteOn ? Color.yellow * 0.5f : Color.yellow * 0.25f);
        }

        private static CameraStage GetCameraStage(IReadOnlyList<StageBase> stages, int selectedIndex)
        {
            if (stages == null || stages.Count == 0) return null;
            return stages[Mathf.Clamp(selectedIndex, 0, stages.Count - 1)] as CameraStage;
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
                if (stage == null) continue;

                // Destroyはフレーム末まで遅延するため、先に無効化して
                // 同じ位置へ作る新Nextと一時的に二重描画されるのを防ぐ。
                stage.gameObject.SetActive(false);
                Destroy(stage.gameObject);
            }

            _nextStages = new List<StageBase>(_currentStages.Count);
            for (int i = 0; i < _currentStages.Count; i++)
            {
                var source = _currentStages[i];
                StageBase next = source == null
                    ? null
                    : CloneStage(source, StageDeck.Next, _nextSlotOffset - _currentSlotOffset, _stages[i].name);
                if (next is CameraStage nextCamera)
                {
                    int channel = source is CameraStage sourceCamera && sourceCamera.CinemachineChannelIndex >= 0
                        ? sourceCamera.CinemachineChannelIndex ^ 1
                        : i * 2;
                    nextCamera.ConfigureCinemachineChannel(channel);
                }
                _nextStages.Add(next);
            }

            // 見えていたNextの状態をCurrentに引き継ぎ、スワップで見た目が変わらないようにする
            _currentState.CopyFrom(NextState);
            _postEffectManager.PromoteNextToCurrent();
            UpdateStageActivity();

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
                data.Stages.Add(new CameraStageLayersSaveData
                {
                    StageId = stage.StageId,
                    StageName = GetStageDisplayName(_stages[i], i),
                    RuntimeCreated = _runtimeStageIds.Contains(stage.StageId),
                    StageIndex = i,
                    Layers = stage.CaptureLayers(),
                    CameraWorkDecks = stage.CaptureCameraWorkDecks(),
                });
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
            int currentStageIndex = _params.CurrentStageIndex;
            int nextStageIndex = _params.NextStageIndex;

            foreach (var savedStage in _pendingCameraStageData.Stages)
            {
                if (savedStage == null) continue;
                int stageIndex = FindStageIndex(savedStage.StageId);
                if (stageIndex < 0 && savedStage.RuntimeCreated && !string.IsNullOrEmpty(savedStage.StageId))
                {
                    AddCameraStage(savedStage.StageName, savedStage.StageId);
                    stageIndex = FindStageIndex(savedStage.StageId);
                }
                if (stageIndex < 0) stageIndex = savedStage.StageIndex;
                if (stageIndex < 0 || stageIndex >= _nextStages.Count) continue;

                if (!string.IsNullOrEmpty(savedStage.StageId))
                {
                    string stageName = string.IsNullOrEmpty(savedStage.StageName)
                        ? GetStageDisplayName(_stages[stageIndex], stageIndex)
                        : savedStage.StageName;
                    _stages[stageIndex]?.SetIdentity(savedStage.StageId, stageName);
                    _currentStages[stageIndex]?.SetIdentity(savedStage.StageId, stageName);
                    _nextStages[stageIndex]?.SetIdentity(savedStage.StageId, stageName);
                }

                if (_nextStages[stageIndex] is CameraStage nextStage)
                {
                    nextStage.RestoreLayers(savedStage.Layers);
                    nextStage.RestoreCameraWorkDecks(savedStage.CameraWorkDecks);
                }
                if (_currentStages[stageIndex] is CameraStage currentStage)
                {
                    currentStage.RestoreLayers(savedStage.Layers);
                    currentStage.RestoreCameraWorkDecks(savedStage.CameraWorkDecks);
                }
            }

            int maxIndex = Mathf.Max(0, _stages.Count - 1);
            _params.CurrentStageIndex = Mathf.Clamp(currentStageIndex, 0, maxIndex);
            _params.NextStageIndex = Mathf.Clamp(nextStageIndex, 0, maxIndex);
            _deckRevision++;
            _pendingCameraStageData = null;
        }

        private void OnDestroy()
        {
            if (OutputTexture != null) OutputTexture.Release();
            if (_crossFadeTexture != null) _crossFadeTexture.Release();
            if (_crossFadeMaterial != null) Destroy(_crossFadeMaterial);
        }

        /// <summary>
        /// 左でステージを選択し、右に選択中Nextステージのレイヤー一覧を表示する。
        /// デッキはスワップで作り直されるため、_deckRevisionの変化でも右カラムを再構築する。
        /// </summary>
        private Element CreateStageListElement(IReadOnlyList<string> stageNames)
        {
            _selectedStageUiIndex = Mathf.Clamp(_selectedStageUiIndex, 0, stageNames.Count - 1);

            Element stageColumn = UI.Column(
                UI.Column(Enumerable.Range(0, stageNames.Count).Select(index =>
                    UI.DynamicElementOnStatusChanged(() => _selectedStageUiIndex, _ =>
                        UI.Button(
                            UI.Label(() => $"{(_selectedStageUiIndex == index ? "▶ " : "  ")}{stageNames[index]}"),
                            () => _selectedStageUiIndex = index).SetMinWidth(150f).SetHeight(26f).SetFlexGrow(1f).SetBackgroundColor(_selectedStageUiIndex == index ? Color.yellowNice * 0.5f : null)
                        )
                    )
                ));

            Element layerColumn = UI.Box(
                UI.DynamicElementOnStatusChanged(
                    readStatus: () => (_selectedStageUiIndex, _deckRevision),
                    build: _ => CreateSelectedStageHeader(stageNames)),
                UI.Row(
                    UI.Box(UI.DynamicElementOnStatusChanged(
                        () => ReadSelectedStageUiStatus(stageNames.Count),
                        status => CreateLayerListElement(status.index))
                    ),
                    UI.Box(UI.DynamicElementOnStatusChanged(
                        () => ReadSelectedStageUiStatus(stageNames.Count),
                        status => CreateCameraWorkListElement(status.index))
                    )
                )
            );
            return UI.Row(stageColumn.SetWidth(180f).SetFlexShrink(0f), layerColumn.SetMinWidth(420f).SetFlexGrow(1f));
            
            Element CreateSelectedStageHeader(IReadOnlyList<string> stageNames)
            {
                int stageIndex = Mathf.Clamp(_selectedStageUiIndex, 0, stageNames.Count - 1);

                return UI.Row(
                    UI.Field(null, () => GetStageDisplayName(_stages[stageIndex], stageIndex), value => RenameCameraStage(_stages[stageIndex].StageId, value)).SetFlexGrow(1f),
                    UI.Button("Duplicate", () => DuplicateCameraStage(_stages[stageIndex].StageId)),
                    UI.Button("Delete", () => RemoveCameraStage(_stages[stageIndex].StageId))
                ).SetBackgroundColor(Color.black * 0.5f);
            }
        }


        private (int index, int deckRevision, CameraStage cameraStage, int layerRevision, int cameraWorkRevision) ReadSelectedStageUiStatus(int stageCount) 
        {
            if (stageCount <= 0)
                return (0, _deckRevision, null, 0, 0);

            int index = Mathf.Clamp(_selectedStageUiIndex, 0, stageCount - 1);
            var cameraStage = _nextStages != null && index < _nextStages.Count
                ? _nextStages[index] as CameraStage
                : null;
            return (index, _deckRevision, cameraStage, cameraStage?.LayerRevision ?? 0,
                cameraStage?.CameraWorkRevision ?? 0);
        }

        private Element CreateStageManagementElement()
        {
            var stageNames = _stages
                .Select(GetStageDisplayName)
                .ToList();

            return UI.Column(
                UI.Row(
                    UI.Button("Add New Stage", () => AddCameraStage()),
                    stageNames.Count == 0
                        ? UI.Label("No Stage")
                        : UI.Column(
                            UI.Dropdown("Current",
                                () => Mathf.Clamp(_params.CurrentStageIndex, 0, stageNames.Count - 1),
                                value => _params.CurrentStageIndex = value,
                                stageNames),
                            UI.Dropdown("Next",
                                () => Mathf.Clamp(_params.NextStageIndex, 0, stageNames.Count - 1),
                                value => _params.NextStageIndex = value,
                                stageNames)
                        )
                ),
                CreateStageListElement(stageNames)
            );
        }

        private static string GetStageDisplayName(StageBase stage, int index) =>
            stage == null ? $"Stage {index}" :
            string.IsNullOrEmpty(stage.StageName) ? stage.name : stage.StageName;

        private Element CreateCameraWorkListElement(int stageIndex)
        {
            var stage = _nextStages != null && stageIndex < _nextStages.Count ? _nextStages[stageIndex] as CameraStage : null;
            if (stage == null) return UI.Label("CameraStageではありません");

            var decks = stage.CameraWorkDecks.Select((deck, deckIndex) =>
               UI.Box(
                    UI.Row(
                        UI.Field(null, () => deck.Name, value => deck.Name = value).SetFlexGrow(1f),
                        UI.Button("Select", () => stage.SelectCameraWorkDeck(deckIndex)),
                        UI.Button("Add Work", () => stage.AddCameraWork(deckIndex)),
                        UI.Button("▲", () => stage.MoveCameraWorkDeck(deckIndex, -1)).SetWidth(32f),
                        UI.Button("▼", () => stage.MoveCameraWorkDeck(deckIndex, 1)).SetWidth(32f),
                        UI.Button("Delete Deck", () => stage.RemoveCameraWorkDeck(deckIndex))
                    ),
                    deck.Recipes == null || deck.Recipes.Count == 0
                        ? UI.Label("No Recipe")
                        : UI.Column(deck.Recipes.Select((recipe, recipeIndex) => CreateCameraWorkElement(stage, deckIndex, recipeIndex, recipe)).ToArray())
                ).SetBackgroundColor(deckIndex % 2 == 0 ? Color.black * 0.4f : null)
            ).ToArray();
            

            return UI.Column(
                UI.Row(
                    UI.Button("Add Deck", () => stage.AddCameraWorkDeck()),
                    UI.Button("Restart", stage.ResetCameraWork),
                    UI.Label(() => $"Timing: {CurrentCameraWorkTiming}"),
                    UI.Label(() => GetCameraWorkProgressText(stage)).SetFlexGrow(1f),
                    UI.SliderReadOnly(null, () => GetCameraWorkProgress(stage), 0f, 1f).SetWidth(60f)),
                decks.Length == 0 ? UI.Label("カメラワークデッキがありません") : UI.Column(decks));
        }

        private static string GetCameraWorkProgressText(CameraStage stage)
        {
            if (stage == null || stage.CameraWorkDecks == null || stage.CameraWorkDecks.Count == 0)
                return "Work: - / -";

            int deckIndex = Mathf.Clamp(stage.SelectedCameraWorkDeck, 0, stage.CameraWorkDecks.Count - 1);
            CameraWorkDeck deck = stage.CameraWorkDecks[deckIndex];
            int recipeCount = deck?.Recipes?.Count ?? 0;
            if (recipeCount == 0) return "Work: - / 0";

            int recipeIndex = Mathf.Clamp(stage.CurrentCameraWork, 0, recipeCount - 1);
            return $"Work: {recipeIndex + 1} / {recipeCount}";
        }

        private static float GetCameraWorkProgress(CameraStage stage)
        {
            if (stage == null || stage.CameraWorkDecks == null || stage.CameraWorkDecks.Count == 0)
                return 0f;

            int deckIndex = Mathf.Clamp(stage.SelectedCameraWorkDeck, 0, stage.CameraWorkDecks.Count - 1);
            int recipeCount = stage.CameraWorkDecks[deckIndex]?.Recipes?.Count ?? 0;
            if (recipeCount <= 1) return recipeCount == 1 ? 1f : 0f;

            return Mathf.Clamp01((float)stage.CurrentCameraWork / (recipeCount - 1));
        }

        private Element CreateCameraWorkElement(CameraStage stage, int deckIndex, int recipeIndex, CameraWorkRecipe recipe)
        {
            recipe.EnsureInitialized();
            return UI.Row(
                UI.Button(UI.Label(() => $"{(_inspectedCameraWork == recipe ? "▶ " : "  ")}{recipe.Name}"),
                    () => InspectCameraWork(stage, recipe))
                    .SetFlexGrow(1f)
                    .RegisterUpdateCallback(element =>
                    {
                        bool isCurrent = deckIndex == stage.SelectedCameraWorkDeck &&
                        recipeIndex == stage.CurrentCameraWork;
                        element.SetBackgroundColor(isCurrent ? StageLedColor * 0.5f : null);
                    }),
                UI.Button("Select", () => stage.SelectCameraWork(deckIndex, recipeIndex)),
                UI.Button("▲", () => stage.MoveCameraWork(deckIndex, recipeIndex, -1)).SetWidth(32f),
                UI.Button("▼", () => stage.MoveCameraWork(deckIndex, recipeIndex, 1)).SetWidth(32f),
                UI.Button("Delete", () => RemoveCameraWork(stage, deckIndex, recipeIndex, recipe)));
        }

        private static IEnumerable<Element> CreateCameraWorkParameterFields(CameraWorkRecipe recipe)
        {
            yield return UI.Field("Position", Binder.Create(recipe.Position, typeof(Vector3Parameter)));
            yield return UI.Field("Look At", Binder.Create(recipe.LookAt, typeof(Vector3Parameter)));
            yield return UI.Field("Aim Rotation", Binder.Create(recipe.AimRotation, typeof(Vector3Parameter)));
            if (recipe.Type == CameraWorkType.Orbit)
                yield return UI.Field("Orbit Rotation", Binder.Create(recipe.OrbitRotation, typeof(Vector3Parameter)));
            yield return UI.Field("Field Of View", Binder.Create(recipe.FieldOfView, typeof(FloatParameter)));
            if (recipe.Type is CameraWorkType.Follow or CameraWorkType.Handheld)
                yield return UI.Field("Speed", Binder.Create(recipe.Speed, typeof(FloatParameter)));
            if (recipe.Type == CameraWorkType.Orbit)
                yield return UI.Field("Radius", Binder.Create(recipe.Radius, typeof(FloatParameter)));
            if (recipe.Type == CameraWorkType.Handheld)
                yield return UI.Field("Noise Amount", Binder.Create(recipe.NoiseAmount, typeof(FloatParameter)));
        }

        private Element CreateLayerListElement(int stageIndex)
        {
            var stage = _nextStages != null && stageIndex < _nextStages.Count ? _nextStages[stageIndex] : null;
            if (stage == null) return UI.Label("ステージが構築されていません");

            if (stage is not CameraStage cameraStage) return UI.Label("このステージはレイヤー編集に未対応です");

            var layers = cameraStage.Layers;
            var layerElements = new List<Element>();
            for (int index = 0; index < layers.Count; index++)
            {
                if (layers[index] != null) layerElements.Add(CreateLayerElement(cameraStage, layers[index], index));
            }

            return UI.Column(
                UI.Row(
                    UI.Button("+ Shape", () => cameraStage.AddShapeLayer()),
                    UI.Button("+ 3D", () => cameraStage.AddPrimitive3DLayer()),
                    UI.Button("+ Model", () => cameraStage.AddModelLayer()),
                    UI.Button("+ Group", () => cameraStage.AddGroupLayer()),
                    UI.Button("+ Particles", () => cameraStage.AddGpuParticleLayer()),
                    UI.Button("+ Text", () => cameraStage.AddTextLayer()),
                    UI.Button("+ Shader", () => cameraStage.AddRuntimeShaderLayer())
                ),
                layers.Count == 0 ? UI.Label("No Layers") : UI.Column(layerElements)
            );
        }

        private Element CreateLayerElement(CameraStage stage, StageLayer layer, int layerIndex)
        {
            if (layer is GroupLayer group)
            {
                var children = new List<Element>();
                var groupChildren = group.Children;
                for (int index = 0; index < groupChildren.Length; index++)
                {
                    StageLayer child = groupChildren[index];
                    if (child != null) children.Add(CreateLayerElement(stage, child, index));
                }
                return UI.Fold(
                    CreateLayerHeader(stage, layer, layerIndex),
                    new Element[] { UI.Column(
                        UI.Row(
                            UI.Button("+ Shape", () => stage.AddShapeLayer(group.transform)),
                            UI.Button("+ 3D", () => stage.AddPrimitive3DLayer(group.transform)),
                            UI.Button("+ Model", () => stage.AddModelLayer(group.transform)),
                            UI.Button("+ GPU", () => stage.AddGpuParticleLayer(group.transform)),
                            UI.Button("+ Text", () => stage.AddTextLayer(group.transform)),
                            UI.Button("+ Group", () => stage.AddGroupLayer(group.transform))),
                        UI.Button("Move Selected Here", () =>
                        {
                            if (_inspectedLayer != null) stage.MoveLayerToGroup(_inspectedLayer, group);
                        }),
                        children.Count == 0 ? UI.Label("グループ内にレイヤーがありません") : UI.Column(children)) });
            }

            return CreateLayerHeader(stage, layer, layerIndex);
        }

        private Element CreateLayerHeader(CameraStage stage, StageLayer layer, int layerColorIndex)
        {
            bool insideGroup = layer.transform.parent != null && layer.transform.parent.GetComponent<GroupLayer>() != null;
            return UI.Row(
                UI.Space().SetWidth(layer is GroupLayer ? 0f : 18f),
                UI.Label(() => _inspectedLayer == layer ? "▶" : " ").SetWidth(18f),
                UI.Toggle(null, () => layer.Visible, value => layer.Visible = value).SetWidth(28f),
                UI.Button(UI.Label(() => layer.gameObject.name), () => InspectLayer(stage, layer, layerColorIndex)).SetMinWidth(250f).SetFlexGrow(1f).SetHeight(30f)
                    .RegisterUpdateCallback(element =>
                    {
                        Color color = GetLayerColor(layerColorIndex);
                        element.SetBackgroundColor( _inspectedLayer == layer ? color * 0.8f : layer.Visible ? color * 0.5f : color * 0.25f);
                    }),
                UI.Button("▲", () => stage.MoveLayer(layer, -1)).SetWidth(32f),
                UI.Button("▼", () => stage.MoveLayer(layer, 1)).SetWidth(32f),
                insideGroup ? UI.Button("Out", () => stage.MoveLayerOutOfGroup(layer)).SetWidth(38f) : null,
                UI.Button("Delete", () => RemoveLayer(stage, layer)));
        }

        private void InspectLayer(CameraStage stage, StageLayer layer, int layerColorIndex)
        {
            _inspectedCameraWork = null;
            _inspectedCameraWorkStage = null;
            if (_inspectedLayerStage == stage && _inspectedLayer == layer)
            {
                _inspectedLayerColorIndex = layerColorIndex;
                _inspectorElement?.CheckAndRebuild();
                if (_inspectorWindow != null) _inspectorWindow.IsOpen = true;
                return;
            }

            _inspectedLayerStage = stage;
            _inspectedLayer = layer;
            _inspectedLayerColorIndex = layerColorIndex;
            _inspectorElement?.CheckAndRebuild();
            if (_inspectorWindow != null) _inspectorWindow.IsOpen = true;
        }

        private void InspectCameraWork(CameraStage stage, CameraWorkRecipe recipe)
        {
            _inspectedLayer = null;
            _inspectedLayerStage = null;
            _inspectedCameraWorkStage = stage;
            _inspectedCameraWork = recipe;
            _inspectorElement?.CheckAndRebuild();
            if (_inspectorWindow != null) _inspectorWindow.IsOpen = true;
        }

        private void RemoveCameraWork(CameraStage stage, int deckIndex, int recipeIndex, CameraWorkRecipe recipe)
        {
            if (_inspectedCameraWork == recipe)
            {
                _inspectedCameraWork = null;
                _inspectedCameraWorkStage = null;
                _inspectorElement?.CheckAndRebuild();
            }

            stage.RemoveCameraWork(deckIndex, recipeIndex);
        }

        private void RemoveLayer(CameraStage stage, StageLayer layer)
        {
            if (_inspectedLayer == layer)
            {
                _inspectedLayer = null;
                _inspectedLayerStage = null;
                _inspectorElement?.CheckAndRebuild();
            }
            stage.RemoveLayer(layer);
        }

        private Element CreateInspectorElement()
        {
            if (_inspectedLayer != null && _inspectedLayerStage != null)
                return CreateLayerInspectorElement();
            if (_inspectedCameraWork != null && _inspectedCameraWorkStage != null)
                return CreateCameraWorkInspectorElement();
            return UI.Label("Select Layer or Camera Work");
        }

        private Element CreateLayerInspectorElement()
        {
            if (_inspectedLayer == null || _inspectedLayerStage == null) return UI.Label("Select Layer");

            StageLayer layer = _inspectedLayer;
            return UI.Column(
                UI.Row(
                    UI.Toggle(null, () => layer.Visible, value => layer.Visible = value),
                    UI.Field(null, () => layer.gameObject.name, value => layer.gameObject.name = value).SetFlexGrow(1f)
                ).SetBackgroundColor(GetLayerColor(_inspectedLayerColorIndex) * 0.5f),
                UI.Field("Order", () => layer.Order, value => layer.Order = value),
                UI.Field(null, Binder.Create(layer.Params, layer.Params.GetType()))
            );
        }

        private Element CreateCameraWorkInspectorElement()
        {
            CameraWorkRecipe recipe = _inspectedCameraWork;
            recipe.EnsureInitialized();
            return UI.Column(
                UI.Field("Name", () => recipe.Name, value => recipe.Name = value),
                UI.Field("Type", () => recipe.Type, value => recipe.Type = value),
                UI.DynamicElementOnStatusChanged(
                    () => recipe.Type,
                    _ => UI.Column(CreateCameraWorkParameterFields(recipe)))
            );
        }

        public Element AdditiveUi()
        {
            _stages ??= new List<StageBase>();
            _inspectorElement = UI.DynamicElementOnStatusChanged(
                () => (_deckRevision, _inspectedLayerStage, _inspectedLayer, _inspectedLayerColorIndex,
                    _inspectedCameraWorkStage, _inspectedCameraWork),
                _ => CreateInspectorElement());
            _inspectorWindow = UI.Window("Inspector", _inspectorElement).SetWidth(460f);
            return UI.Column(
                UI.Row(
                    UI.Slider("Main Fader", () => CrossFade, 0f, 1f),
                    UI.Toggle("Immediate Mode", () => IsImmediateMode, SetImmediateMode),
                    UI.WindowLauncher("Inspector", _inspectorWindow)
                ),
                UI.DynamicElementOnStatusChanged(() => _deckRevision, _ => CreateStageManagementElement()),
                UI.Label(() => IsImmediateMode ? "<b>IMMEDIATE MODE</b>" : _isFaderFlipped ? "FADER: Down to next" : "FADER: Up to next")
            );
        }
    }

    [Serializable]
    public sealed class CameraStageSaveData
    {
        public int Version = 2;
        public List<CameraStageLayersSaveData> Stages = new();
    }

    [Serializable]
    public sealed class CameraStageLayersSaveData
    {
        public string StageId;
        public string StageName;
        public bool RuntimeCreated;
        public int StageIndex;
        public List<CameraStageLayerSaveData> Layers = new();
        public List<CameraWorkDeck> CameraWorkDecks = new();
    }
}
