using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public sealed class AudioFeatureProviderParams : IParams
    {
        [Header("Kick")]
        [Min(10f)] public float KickMinFrequency = 35f;
        [Tooltip("サブベース成分とアタック成分を分ける周波数")]
        [Min(20f)] public float KickSplitFrequency = 80f;
        [Min(20f)] public float KickMaxFrequency = 160f;
        [Range(0.1f, 8f)] public float KickSensitivity = 1.35f;

        [Header("Snare / Clap")]
        [Min(20f)] public float SnareMinFrequency = 180f;
        [Tooltip("スネアの胴鳴りとして扱う上限周波数")]
        [Min(100f)] public float SnareBodyMaxFrequency = 520f;
        [Tooltip("スネア／クラップのノイズ成分として扱う下限周波数")]
        [Min(100f)] public float SnareNoiseMinFrequency = 700f;
        [Tooltip("ノイズ成分の広がりを確認するための分割周波数")]
        [Min(200f)] public float SnareNoiseSplitFrequency = 2800f;
        [Min(100f)] public float SnareMaxFrequency = 9000f;
        [Range(0.1f, 8f)] public float SnareSensitivity = 1.55f;

        [Header("Onset")]
        [Tooltip("通常時のばらつきから何標準偏差を超えた変化をオンセットとして扱うか")]
        [Range(0.5f, 5f)] public float FluxThreshold = 1.8f;

        [Tooltip("周囲の音量へ追従する速さ")]
        [Range(0.05f, 3f)] public float AdaptationTime = 0.9f;

        [Tooltip("これより小さいRMS入力では検出しない")]
        [Range(0f, 0.1f)] public float NoiseGate = 0.004f;

        [Tooltip("Kickが強い瞬間にSnare判定を抑える量")]
        [Range(0f, 1f)] public float KickToSnareRejection = 0.4f;

        [Tooltip("この値を超えたときにパルスを発生させる")]
        [Range(0f, 1f)] public float TriggerThreshold = 0.12f;

        [Tooltip("パルスが0へ戻るまでの速さ")]
        [Range(0.01f, 1f)] public float ReleaseTime = 0.14f;

        [Tooltip("同じ音を連続検出しない最短時間")]
        [Range(0f, 0.5f)] public float Cooldown = 0.09f;
    }
}
