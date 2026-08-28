using UnityEngine;

namespace Aetherin
{
    /// <summary>オーディオ解析結果を演出やシェーダーへ提供する。</summary>
    public interface IAudioFeatureProvider
    {
        /// <summary>バスドラムの立ち上がり。0～1。</summary>
        float Kick { get; }

        /// <summary>スネアまたはハンドクラップの立ち上がり。0～1。</summary>
        float SnareClap { get; }

        /// <summary>このフレームでバスドラムを検出した。</summary>
        bool WasKick { get; }

        /// <summary>このフレームでスネアまたはハンドクラップを検出した。</summary>
        bool WasSnareClap { get; }

        /// <summary>キャプチャ開始から数えた直近Kickのサンプル位置。取得不能時は-1。</summary>
        long LastKickSampleIndex { get; }

        /// <summary>キャプチャ開始から数えた直近Snare/Clapのサンプル位置。取得不能時は-1。</summary>
        long LastSnareClapSampleIndex { get; }

        /// <summary>Rチャンネルに-1～1の波形を格納した1行のTexture。</summary>
        Texture WaveformTexture { get; }

        /// <summary>Rチャンネルに0～1の線形周波数スペクトラムを格納した1行のTexture。</summary>
        Texture SpectrumTexture { get; }
    }
}
