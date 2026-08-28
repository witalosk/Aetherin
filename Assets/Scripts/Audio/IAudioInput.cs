using System;

namespace Aetherin
{
    /// <summary>
    /// オーディオ入力の時間領域データと周波数領域データを提供する。
    /// 公開されるSpanは次のUpdateまで有効。
    /// </summary>
    public interface IAudioInput
    {
        bool IsConnected { get; }
        int SampleRate { get; }
        int Channel { get; }
        float RmsLevel { get; }
        float PeakLevel { get; }

        ReadOnlySpan<float> Waveform { get; }
        ReadOnlySpan<float> Spectrum { get; }
        ReadOnlySpan<float> LogSpectrum { get; }

        /// <summary>線形FFTスペクトルの指定ビンに対応する周波数を返す。</summary>
        float GetFrequency(int spectrumIndex);
    }
}
