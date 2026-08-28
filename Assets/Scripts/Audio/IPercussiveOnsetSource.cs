namespace Aetherin
{
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
    }
}
