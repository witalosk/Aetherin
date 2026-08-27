using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public class MidiSurfaceParams : IParams
    {
        [Tooltip("パッド1つのUIサイズ (px)")]
        [Range(12f, 64f)]
        public float PadSize = 26f;

        [Tooltip("フェーダーUIの幅 (px)")]
        public float FaderWidth = 260f;

        [Tooltip("実機が接続されていてもUIから操作できるようにする")]
        public bool AlwaysAllowEmulation = false;
    }
}
