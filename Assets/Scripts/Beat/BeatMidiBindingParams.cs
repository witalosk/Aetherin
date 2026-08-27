using System;
using UnityEngine;

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
}
