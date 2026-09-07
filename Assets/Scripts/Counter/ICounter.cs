namespace Aetherin
{
    /// <summary>パッド操作で増減する、FXモジュレーション用のカウンター。</summary>
    public interface ICounter
    {
        int Value { get; }
        float AnimatedValue { get; }
        double LastIncrementTime { get; }
        void Increment();
        void Reset();
    }
}
