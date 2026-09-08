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
        [Tooltip("Time.timeScale を操作するMIDI CC。未割り当て時は 1 のままです")]
        public MidiCcBinding TimeScaleCc = new();
        [Min(0f)] public float TimeScaleMin = 0f;
        [Min(0f)] public float TimeScaleMax = 2f;
    }
    
    public class ApplicationManager : MonoBehaviour, IApplicationManager, ISaveAndUiTarget
    {
        public Vector2Int Resolution => _params.Resolution;
        public IParams Params => _params;
        public string Category => UiCategory.Settings;
        
        [SerializeField] private ApplicaitonManagerParams _params = new();

        private void Awake()
        {
            _params ??= new ApplicaitonManagerParams();
            _params.TimeScaleCc ??= new MidiCcBinding();
            ApplyFps();
        }

        private void Update()
        {
            _params ??= new ApplicaitonManagerParams();
            _params.TimeScaleCc ??= new MidiCcBinding();
            if (!_params.TimeScaleCc.IsAssigned)
            {
                Time.timeScale = 1f;
                return;
            }

            float min = Mathf.Min(_params.TimeScaleMin, _params.TimeScaleMax);
            float max = Mathf.Max(_params.TimeScaleMin, _params.TimeScaleMax);
            Time.timeScale = Mathf.Lerp(min, max, _params.TimeScaleCc.GetValue());
        }

        private void OnDestroy()
        {
            // このマネージャが破棄されたあとに、停止状態だけが残らないよう戻す。
            Time.timeScale = 1f;
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
