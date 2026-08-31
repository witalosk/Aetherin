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
        public int CurrentStageIndex;
        public int NextStageIndex;

        [Tooltip("CameraStageで複製元が映り込まないようにするためのオフセット")]
        public Vector3 NextStageOffset = new(0f, 1000f, 0f);

        [Tooltip("フェーダーがこの値まで振り切ったらCurrent / Nextを入れ替える")]
        [Range(0.9f, 1f)]
        public float SwapThreshold = 0.99f;
    }

    /// <summary>
    /// 登録されたステージを起動時に複製してCurrent (オリジナル) / Next (複製) の2系統を持ち、
    /// クロスフェーダーで最終出力をブレンドするマネージャ
    /// MIDIコンから操作するステージは基本的にNext側にして、フェーダーで本番出力に送る運用を想定
    /// </summary>
    public class StageManager : MonoBehaviour, IDeckStateProvider, ISaveAndUiTarget
    {
        public IParams Params => _params;
        public string Category => UiCategory.Main;

        /// <summary> MIDIコンやUIからの変更はこちらに書き込まれる </summary>
        public DeckState NextState { get; private set; } = new();

        /// <summary> クロスフェード済みの最終出力 </summary>
        public RenderTexture OutputTexture { get; private set; }

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

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int TexAId = Shader.PropertyToID("_TexA");
        private static readonly int TexBId = Shader.PropertyToID("_TexB");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private List<StageBase> _currentStages;
        private List<StageBase> _nextStages = new();
        private bool _isFaderFlipped;
        private readonly DeckState _currentState = new();

        private IContainer _container;
        private IApplicationManager _applicationManager;
        private Material _crossFadeMaterial;

        [Inject]
        public void Construct(IContainer container, IApplicationManager applicationManager)
        {
            _container = container;
            _applicationManager = applicationManager;
        }

        public DeckState GetState(StageDeck deck) => deck == StageDeck.Current ? _currentState : NextState;

        private void Start()
        {
            OutputTexture = new RenderTexture(_applicationManager.Resolution.x, _applicationManager.Resolution.y, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _crossFadeMaterial = new Material(_crossFadeShader);

            DuplicateStagesForNext();

            if (_outputRenderer != null) _outputRenderer.material.SetTexture(MainTexId, OutputTexture);
        }

        /// <summary>
        /// 複製はシーン起動時のInjectに含まれないため、コンテナ経由でInstantiateして注入する
        /// </summary>
        private void DuplicateStagesForNext()
        {
            _currentStages = new List<StageBase>(_stages);

            foreach (var stage in _stages)
            {
                var clone = _container.Instantiate(stage.gameObject, stage.transform.parent, true);
                clone.name = $"{stage.name} (Next)";

                // CameraStageなどシーン上に実体を持つステージが複製元と互いに映り込まないようにずらす
                clone.transform.position += _params.NextStageOffset;

                var nextStage = clone.GetComponent<StageBase>();
                nextStage.Deck = StageDeck.Next;
                _nextStages.Add(nextStage);
            }
        }

        // 各ステージがUpdateで描画した後にブレンドする
        private void LateUpdate()
        {
            if (_crossFadeMaterial == null || OutputTexture == null || _currentStages == null) return;

            if (CrossFade >= _params.SwapThreshold) SwapDecks();

            var currentTexture = GetStageTexture(_currentStages, _params.CurrentStageIndex);
            var nextTexture = GetStageTexture(_nextStages, _params.NextStageIndex);

            _crossFadeMaterial.SetTexture(TexAId, currentTexture);
            _crossFadeMaterial.SetTexture(TexBId, nextTexture);
            _crossFadeMaterial.SetFloat(FadeId, CrossFade);
            Graphics.Blit(null, OutputTexture, _crossFadeMaterial);

            if (_currentPreviewRenderer != null) _currentPreviewRenderer.material.SetTexture(MainTexId, currentTexture);
            if (_nextPreviewRenderer != null) _nextPreviewRenderer.material.SetTexture(MainTexId, nextTexture);
        }

        /// <summary>
        /// 出力される絵は変えずに、以降のMIDI操作対象 (Next) を裏側のデッキに切り替える
        /// </summary>
        private void SwapDecks()
        {
            // フェーダーの向きを反転することで、スワップ直後のブレンド結果 (=今まで見えていたNext) を維持する
            _isFaderFlipped = !_isFaderFlipped;

            (_currentStages, _nextStages) = (_nextStages, _currentStages);
            (_params.CurrentStageIndex, _params.NextStageIndex) = (_params.NextStageIndex, _params.CurrentStageIndex);

            foreach (var stage in _currentStages)
            {
                if (stage != null) stage.Deck = StageDeck.Current;
            }

            foreach (var stage in _nextStages)
            {
                if (stage != null) stage.Deck = StageDeck.Next;
            }

            // 見えていたNextの状態をCurrentに引き継ぎ、スワップで見た目が変わらないようにする
            _currentState.CopyFrom(NextState);
        }

        private static Texture GetStageTexture(List<StageBase> stages, int index)
        {
            if (stages.Count == 0) return Texture2D.blackTexture;

            var stage = stages[Mathf.Clamp(index, 0, stages.Count - 1)];
            return stage != null && stage.OutputTexture != null ? stage.OutputTexture : Texture2D.blackTexture;
        }

        private void OnDestroy()
        {
            if (OutputTexture != null) OutputTexture.Release();
            if (_crossFadeMaterial != null) Destroy(_crossFadeMaterial);
        }

        public Element AdditiveUi()
        {
            var stageNames = _stages
                .Select((s, i) => s == null ? $"Stage {i}" : (string.IsNullOrEmpty(s.StageName) ? s.name : s.StageName))
                .ToList();

            if (stageNames.Count == 0) return UI.Label("ステージが登録されていません");

            return UI.Column(
                UI.Dropdown("Current",
                    () => Mathf.Clamp(_params.CurrentStageIndex, 0, stageNames.Count - 1),
                    value => _params.CurrentStageIndex = value,
                    stageNames),
                UI.Dropdown("Next",
                    () => Mathf.Clamp(_params.NextStageIndex, 0, stageNames.Count - 1),
                    value => _params.NextStageIndex = value,
                    stageNames),
                UI.SliderReadOnly("CrossFade (Current ← → Next)", () => CrossFade, 0f, 1f)
            );
        }
    }
}
