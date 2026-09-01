using System;
using System.Collections.Generic;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    [Serializable]
    public class BeatMidiBindingParams : IParams
    {
        [Tooltip("主拍 = 小節の頭")]
        public MidiBinding MainTapButton = new(0);

        public MidiBinding SubTapButton = new(1);

        [Tooltip("小節内の各拍を Beat モジュレーション対象へ切り替えるPad。1番目がBeat 1です")]
        public List<MidiBinding> BeatToggleButtons = new()
        {
            new(2), new(3), new(4), new(5)
        };

        [Tooltip("BPMを半分にするPad")]
        public MidiBinding HalfBpmButton = new(6);

        [Tooltip("BPMを2倍にするPad")]
        public MidiBinding DoubleBpmButton = new(7);

        public Color MainColor = new(0.35f, 0.85f, 1f);
        public Color SubColor = new(1f, 0.65f, 0.25f);
        public Color BeatToggleColor = new(0.75f, 0.3f, 1f);
        public Color BpmButtonColor = new(0.25f, 1f, 0.55f);

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
        public string Category => UiCategory.Settings;

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

            UpdateBpmButtons();

            UpdateBeatToggleButtons();

            // 主拍のボタンは小節の位相、サブ拍のボタンは拍の位相に合わせて光らせる
            _params.MainTapButton.SetLed(_params.MainColor * GetBrightness(_beat.BarPhase));
            _params.SubTapButton.SetLed(_params.SubColor * GetBrightness(_beat.BeatPhase));
        }

        private void UpdateBpmButtons()
        {
            bool wasHalfPressed = _params.HalfBpmButton.WasNoteOn;
            bool wasDoublePressed = _params.DoubleBpmButton.WasNoteOn;
            if (wasHalfPressed) _beat.HalfBpm();
            if (wasDoublePressed) _beat.DoubleBpm();

            _params.HalfBpmButton.SetLed(_params.BpmButtonColor * (wasHalfPressed ? 1f : 0.2f));
            _params.DoubleBpmButton.SetLed(_params.BpmButtonColor * (wasDoublePressed ? 1f : 0.2f));
        }

        private void UpdateBeatToggleButtons()
        {
            _params.BeatToggleButtons ??= new List<MidiBinding>();
            int beatCount = _beat.BeatsPerBar;

            for (int i = 0; i < _params.BeatToggleButtons.Count; i++)
            {
                var button = _params.BeatToggleButtons[i];
                if (i >= beatCount)
                {
                    button.ClearLed();
                    continue;
                }

                if (button.WasNoteOn) _beat.ToggleBeatEnabled(i);

                bool enabled = _beat.IsBeatEnabled(i);
                bool isCurrent = _beat.IsRunning && _beat.BeatInBar == i;
                float brightness = enabled ? (isCurrent ? 1f : 0.45f) : 0.08f;
                button.SetLed(_params.BeatToggleColor * brightness);
            }
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
