using System;
using System.Collections.Generic;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    public abstract class StageBase : MonoBehaviour, IStage
    {
        public string StageId => _stageId;
        public string StageName => _stageName;
        public RenderTexture OutputTexture { get; private set; }
        public double StageTime { get { SampleStageTime(); return _stageTime; } }
        public float StageDeltaTime { get { SampleStageTime(); return _stageDeltaTime; } }
        public float StageTimeSpeed => _stageTimeSpeed;
        public int StageTimeRevision { get; private set; }

        /// <summary> レイヤーを持たないステージでは空 </summary>
        public virtual IReadOnlyList<StageLayer> Layers => Array.Empty<StageLayer>();

        /// <summary> Nextの複製を作るときにStageManagerが設定する </summary>
        public StageDeck Deck { get; set; } = StageDeck.Current;

        [SerializeField] private string _stageId;
        [SerializeField] private string _stageName;
        [SerializeField] private RenderTexture _tex;
        [SerializeField, Min(0f)] private float _stageTimeSpeed = 1f;
        [NonSerialized] private double _stageTime;
        [NonSerialized] private double _lastStageClockTime;
        [NonSerialized] private float _stageDeltaTime;
        [NonSerialized] private int _lastStageClockFrame = -1;
        protected IApplicationManager _applicationManager;
        protected IDeckStateProvider _deckStateProvider;

        public void EnsureStageId()
        {
            if (string.IsNullOrEmpty(_stageId)) _stageId = Guid.NewGuid().ToString("N");
        }

        public void SetIdentity(string stageId, string stageName)
        {
            _stageId = string.IsNullOrEmpty(stageId) ? Guid.NewGuid().ToString("N") : stageId;
            _stageName = stageName;
        }

        public void SetStageTimeSpeed(float speed)
        {
            SampleStageTime();
            _stageTimeSpeed = Mathf.Max(0f, speed);
        }

        public void ResetStageTime()
        {
            _stageTime = 0d;
            _stageDeltaTime = 0f;
            _lastStageClockTime = UnityEngine.Time.unscaledTimeAsDouble;
            _lastStageClockFrame = UnityEngine.Time.frameCount;
            StageTimeRevision++;
        }

        private void SampleStageTime()
        {
            if (!Application.isPlaying) return;
            int frame = UnityEngine.Time.frameCount;
            if (_lastStageClockFrame == frame) return;

            double now = UnityEngine.Time.unscaledTimeAsDouble;
            // 非アクティブ期間は時間を進めない。再選択時の大きなdeltaも防ぐ。
            double rawDelta = _lastStageClockFrame == frame - 1
                ? Math.Max(0d, now - _lastStageClockTime)
                : 0d;
            _stageDeltaTime = (float)(rawDelta * _stageTimeSpeed);
            _stageTime += _stageDeltaTime;
            _lastStageClockTime = now;
            _lastStageClockFrame = frame;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureStageId();
            _stageTimeSpeed = Mathf.Max(0f, _stageTimeSpeed);
        }
#endif
        
        [Inject]
        public void Construct(IApplicationManager applicationManager, IDeckStateProvider deckStateProvider)
        {
            _applicationManager = applicationManager;
            _deckStateProvider = deckStateProvider;
        }

        protected virtual void Start()
        {
            ResetStageTime();
            OutputTexture = new RenderTexture(_applicationManager.Resolution.x, _applicationManager.Resolution.y, 1, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _tex = OutputTexture;
        }

        protected virtual void OnDestroy()
        {
            OutputTexture?.Release();
        }
    }
}
