using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public class AudioInputParams : IParams
    {
        [Tooltip("OSのデフォルト入力デバイスを使用する")]
        [HideInInspector]
        public bool UseDefaultDevice = true;

        [Tooltip("Use Default Deviceが無効な場合に使うLASPデバイスID")]
        [HideInInspector]
        public string DeviceId = string.Empty;

        [Tooltip("入力チャンネル番号（0始まり）")]
        [Range(0, 15)]
        public int Channel;

        [Tooltip("FFTの出力ビン数。2の累乗を指定する")]
        public int SpectrumResolution = 512;

        [Tooltip("FFT表示の入力ゲインを自動調整する")]
        public bool AutoGain = true;

        [Tooltip("Auto Gainが無効な場合の入力ゲイン (dB)")]
        [Range(-10f, 120f)]
        public float Gain;

        [Tooltip("FFT表示のダイナミックレンジ (dB)")]
        [Range(1f, 120f)]
        public float DynamicRange = 80f;
    }
}
