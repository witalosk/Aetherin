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

        /// <summary> レイヤーを持たないステージでは空 </summary>
        public virtual IReadOnlyList<StageLayer> Layers => Array.Empty<StageLayer>();

        /// <summary> Nextの複製を作るときにStageManagerが設定する </summary>
        public StageDeck Deck { get; set; } = StageDeck.Current;

        [SerializeField] private string _stageId;
        [SerializeField] private string _stageName;
        [SerializeField] private RenderTexture _tex;
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

#if UNITY_EDITOR
        private void OnValidate() => EnsureStageId();
#endif
        
        [Inject]
        public void Construct(IApplicationManager applicationManager, IDeckStateProvider deckStateProvider)
        {
            _applicationManager = applicationManager;
            _deckStateProvider = deckStateProvider;
        }

        protected virtual void Start()
        {
            OutputTexture = new RenderTexture(_applicationManager.Resolution.x, _applicationManager.Resolution.y, 1, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _tex = OutputTexture;
        }

        protected virtual void OnDestroy()
        {
            OutputTexture?.Release();
        }
    }
}
