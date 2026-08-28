using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    public abstract class StageBase : MonoBehaviour, IStage
    {
        public string StageName => _stageName;
        public RenderTexture OutputTexture { get; private set; }

        [SerializeField] private string _stageName;
        
        private IApplicationManager _applicationManager;
        
        [Inject]
        public void Construct(IApplicationManager applicationManager)
        {
            _applicationManager = applicationManager;
        }

        private void Start()
        {
            OutputTexture = new RenderTexture(_applicationManager.Resolution.x, _applicationManager.Resolution.y, 0, RenderTextureFormat.ARGB32);
        }

        private void OnDestroy()
        {
            OutputTexture.Release();
        }
    }
}