using System;

namespace Aetherin
{
    /// <summary>
    /// 単一のテンポを管理するインターフェース
    ///
    /// 主拍 (小節頭) とサブ拍 (それ以外の拍) を1つの拍の列として扱う
    /// 3拍子なら Z X X Z X X ... 、4拍子なら Z X X X Z X X X ... とタップする想定で、
    /// テンポはタップ間隔から、拍子 (<see cref="BeatsPerBar"/>) は主拍から次の主拍までのタップ数から求める
    /// </summary>
    public interface IBeatManager
    {
        /// <summary> 現在のBPM (サブ拍=拍の速さ) </summary>
        float Bpm { get; }

        /// <summary> テンポが動いているか (一度もタップされていない/Stop後はfalse) </summary>
        bool IsRunning { get; }

        /// <summary> 拍子 (1小節あたりの拍数)。主拍から次の主拍までのタップ数から求まる </summary>
        int BeatsPerBar { get; }

        /// <summary> 直近の主拍から次の主拍までに数えたタップ数 (0は未計測)。UI表示・確認用 </summary>
        int LastCountedBeatsPerBar { get; }

        /// <summary> 現在の小節で主拍から数えているタップ数 </summary>
        int CountingBeats { get; }

        /// <summary> タップから拍子が確定しているか </summary>
        bool IsBeatsPerBarEstimated { get; }

        #region 拍 (サブ拍)

        /// <summary>
        /// 有効な拍の拍内位置 (0..1)。無効な拍では常に1となり、Beatモジュレーションは発火しない。
        /// </summary>
        float BeatPhase { get; }

        /// <summary>指定した小節内の拍を Beat モジュレーションの対象にするか。</summary>
        bool IsBeatEnabled(int beatInBar);

        /// <summary>指定した小節内の拍の Beat モジュレーション対象を切り替える。</summary>
        void SetBeatEnabled(int beatInBar, bool enabled);

        /// <summary>指定した小節内の拍の Beat モジュレーション対象を反転する。</summary>
        void ToggleBeatEnabled(int beatInBar);

        /// <summary> 小節頭からの通算の拍数 </summary>
        int BeatCount { get; }

        /// <summary>BeatManagerの生存中、拍ごとに単調増加するイベントID</summary>
        long BeatEventId { get; }

        /// <summary> 小節内の拍位置 (0..BeatsPerBar-1) </summary>
        int BeatInBar { get; }

        /// <summary> このフレームで拍が来たか </summary>
        bool WasBeat { get; }

        /// <summary> 拍のタイミングで発火する (引数は<see cref="BeatCount"/>) </summary>
        event Action<int> OnBeat;

        #endregion

        #region 小節 (主拍)

        /// <summary> 小節内の位置 (0..1)。小節頭が0 </summary>
        float BarPhase { get; }

        int BarCount { get; }

        /// <summary>BeatManagerの生存中、小節頭ごとに単調増加するイベントID</summary>
        long BarEventId { get; }

        /// <summary> このフレームで小節頭が来たか </summary>
        bool WasBar { get; }

        /// <summary> 小節頭のタイミングで発火する (引数は<see cref="BarCount"/>) </summary>
        event Action<int> OnBar;

        #endregion

        /// <summary>
        /// 主拍 (小節頭) をタップする
        /// 小節の頭をこの瞬間に合わせ、前回の主拍からのタップ数を拍子として採用する
        /// </summary>
        void TapMain();

        /// <summary>
        /// サブ拍 (小節頭以外の拍) をタップする
        /// 拍の頭をこの瞬間に合わせ、拍子のカウントを1つ進める
        /// </summary>
        void TapSub();

        /// <summary>
        /// 再度タップするまで位相は進まない
        /// </summary>
        void Stop();

        void SetBpm(float bpm);

        /// <summary>現在のBPMを2倍にする。拍内位相は維持する。</summary>
        void DoubleBpm();

        /// <summary>現在のBPMを半分にする。拍内位相は維持する。</summary>
        void HalfBpm();

        /// <summary>
        /// 推定を上書きする
        /// </summary>
        void SetBeatsPerBar(int beatsPerBar);
    }
}
