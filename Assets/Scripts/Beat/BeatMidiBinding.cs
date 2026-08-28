using System;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    [Serializable]
    public class BeatMidiBindingParams : IParams
    {
        [Tooltip("主拍 (小節頭) を叩くMIDIボタン")]
        public MidiBinding MainTapButton = new(0);

        [Tooltip("サブ拍を叩くMIDIボタン")]
        public MidiBinding SubTapButton = new(1);

        public Color MainColor = new(0.35f, 0.85f, 1f);
        public Color SubColor = new(1f, 0.65f, 0.25f);

        [Tooltip("光の減衰の鋭さ。大きいほど拍の頭で短く光る")]
        [Range(0.5f, 8f)]
        public float FlashSharpness = 3f;

        [Tooltip("消灯時の明るさ。ボタンの位置が分かるように少しだけ光らせる")]
        [Range(0f, 0.5f)]
        public float IdleBrightness = 0.08f;

        [Tooltip("明るさの段階数。細かくするとMIDIの送信量が増える")]
        [Range(2, 32)]
        public int BrightnessSteps = 12;
    }
    
    /// <summary>
    /// MIDIコントローラのボタンを拍のタップに割り当て、そのボタンを拍に合わせて光らせる
    /// <see cref="BeatManager"/>をMIDIから独立させておくため、繋ぎ込みはこのコンポーネントが担う
    ///
    /// ボタンの割り当て・Learnは<see cref="MidiBinding"/>が持つため、ここではその読み書きだけを行う
    /// </summary>
    public class BeatMidiBinding : MonoBehaviour, ISaveAndUiTarget
    {
        public IParams Params => _params;
        public string Category => UiCategory.Beat;

        [SerializeField]
        private BeatMidiBindingParams _params = new();

        private IBeatManager _beat;

        [Inject]
        public void Construct(IBeatManager beat)
        {
            _beat = beat;
        }

        private void Update()
        {
            if (_beat == null) return;

            if (_params.MainTapButton.WasNoteOn) _beat.TapMain();
            if (_params.SubTapButton.WasNoteOn) _beat.TapSub();

            // 主拍のボタンは小節の位相、サブ拍のボタンは拍の位相に合わせて光らせる
            _params.MainTapButton.SetLed(_params.MainColor * GetBrightness(_beat.BarPhase));
            _params.SubTapButton.SetLed(_params.SubColor * GetBrightness(_beat.BeatPhase));
        }

        private float GetBrightness(float phase)
        {
            if (!_beat.IsRunning) return _params.IdleBrightness;

            float brightness = Mathf.Pow(1f - Mathf.Clamp01(phase), _params.FlashSharpness);
            brightness = Mathf.Max(_params.IdleBrightness, brightness);

            // 段階に丸めて、明るさが変わったときだけMIDIが送られるようにする
            int steps = Mathf.Max(2, _params.BrightnessSteps);
            return Mathf.Round(brightness * steps) / steps;
        }
    }
}
