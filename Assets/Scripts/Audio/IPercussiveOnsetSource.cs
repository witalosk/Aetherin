namespace Aetherin
{
    public enum PercussiveBand
    {
        KickLow,
        KickPunch,
        SnareBody,
        SnareLowNoise,
        SnareHighNoise,
        Count
    }

    /// <summary>オーディオスレッドで検出されたサンプル精度の打楽器オンセット。</summary>
    public interface IPercussiveOnsetSource
    {
        bool IsHardRealtimeOnsetAvailable { get; }
        int KickOnsetSequence { get; }
        int SnareClapOnsetSequence { get; }
        float LatestKickStrength { get; }
        float LatestSnareClapStrength { get; }
        long LatestKickSampleIndex { get; }
        long LatestSnareClapSampleIndex { get; }
        float GetLongTermMean(PercussiveBand band);
        float GetLongTermStandardDeviation(PercussiveBand band);
        float GetLongTermDeviation(PercussiveBand band);
    }
}
