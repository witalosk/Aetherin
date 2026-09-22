namespace Aetherin
{
    /// <summary>パッド操作で増減する、FXモジュレーション用のカウンター。</summary>
    public interface ICounter
    {
        int Value { get; }
        float AnimatedValue { get; }
        double LastIncrementTime { get; }
        long IncrementEventId { get; }
        void Increment();
        void Reset();
    }

    /// <summary>複数の独立したカウンターを提供する。</summary>
    public interface ICounterBank
    {
        int CounterCount { get; }
        ICounter GetCounter(int index);
    }
}
