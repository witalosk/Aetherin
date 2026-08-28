using System;
using RosettaUI;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public class ApplicaitonManagerParams : IParams
    {
        public int TargetFps = 60;
        public int VSyncCOUnt = 0;
        public Vector2Int Resolution = new(3840, 2160);
    }
    
    public class ApplicationManager : MonoBehaviour, IApplicationManager, ISaveAndUiTarget
    {
        public Vector2Int Resolution => _params.Resolution;
        public IParams Params => _params;
        public string Category => UiCategory.System;
        
        [SerializeField] private ApplicaitonManagerParams _params = new();

        private void Awake()
        {
            ApplyFps();
        }

        private void ApplyFps()
        {
            QualitySettings.vSyncCount = _params.VSyncCOUnt;
            Application.targetFrameRate = _params.TargetFps;
        }

        public Element AdditiveUi()
        {
            return UI.Button("Update Fps", ApplyFps);
        }
    }
}
