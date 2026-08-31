using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    public abstract class StageBase : MonoBehaviour, IStage
    {
        public string StageName => _stageName;
        public RenderTexture OutputTexture { get; private set; }

        [SerializeField] private string _stageName;
        [SerializeField] private RenderTexture _tex;
        private IApplicationManager _applicationManager;
        
        [Inject]
        public void Construct(IApplicationManager applicationManager)
        {
            _applicationManager = applicationManager;
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
