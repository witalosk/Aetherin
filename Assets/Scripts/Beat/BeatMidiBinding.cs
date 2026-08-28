using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    /// <summary>
    /// MIDIコントローラのボタンを拍のタップに割り当て、そのボタンを拍に合わせて光らせる
    /// <see cref="BeatManager"/>をMIDIから独立させておくため、繋ぎ込みはこのコンポーネントが担う
    ///
    /// ボタンの割り当て・Learnは<see cref="MidiBinding"/>が持つため、ここではその読み書きだけを行う
    /// </summary>
    public class BeatMidiBinding : MonoBehaviour, ISaveAndUiTarget
    {
        public IParams Params => _params;

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
