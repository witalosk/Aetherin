using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public sealed class WasapiLoopbackInputParams : IParams
    {
        [HideInInspector]
        [Tooltip("空文字の場合はWindowsのデフォルト出力デバイス")]
        public string DeviceId = string.Empty;

        [Tooltip("2の累乗")]
        public int FftSize = 1024;

        [Tooltip("UIや利用側へ公開する波形のサンプル数")]
        public int WaveformSamples = 1024;

        [Tooltip("スペクトラムへ加えるゲイン (dB)")]
        [Range(-60f, 60f)]
        public float Gain;

        [Tooltip("スペクトラム表示のダイナミックレンジ (dB)")]
        [Range(20f, 120f)]
        public float DynamicRange = 80f;
    }
}
