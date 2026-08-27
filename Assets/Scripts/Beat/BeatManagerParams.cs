using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aetherin
{
    [Serializable]
    public class BeatManagerParams : IParams
    {
        [Tooltip("主拍 (小節頭) をタップするキー")]
        public Key MainTapKey = Key.Z;

        [Tooltip("サブ拍 (小節頭以外の拍) をタップするキー")]
        public Key SubTapKey = Key.X;

        [Tooltip("主拍から次の主拍までのタップ数から拍子を求める")]
        public bool EstimateBeatsPerBar = true;

        [Tooltip("拍子 (推定を切ったときはこの値が使われる)")]
        [Range(1, 16)]
        public int BeatsPerBar = 4;

        [Tooltip("拍子として採用する上限。これを超えるタップ数は採用しない")]
        [Range(2, 16)]
        public int MaxBeatsPerBar = 8;

        [Tooltip("この秒数タップが途切れたらタップ列をリセットする")]
        public float TapTimeout = 2f;

        [Tooltip("BPMの平均に使う直近のタップ間隔の数")]
        [Range(2, 16)]
        public int TapHistoryCount = 6;

        public float MinBpm = 40f;
        public float MaxBpm = 300f;

        [Tooltip("UIの拍インジケータ1つのサイズ (px)")]
        [Range(8f, 48f)]
        public float CellSize = 22f;
    }
}
