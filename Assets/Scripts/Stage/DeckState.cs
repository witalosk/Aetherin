namespace Aetherin
{
    /// <summary>
    /// デッキ (Current / Next) ごとに切り替わる、見た目を決める状態の集約
    /// デッキ依存の要素を増やすときはここにフィールドを追加する
    /// (各マネージャはNext側に書き込むだけでよく、Current / Nextの二重管理はStageManagerに閉じる)
    /// </summary>
    public class DeckState
    {
        public ColorPalette Palette = new();

        public void CopyFrom(DeckState other)
        {
            Palette = other.Palette;
        }
    }

    public interface IDeckStateProvider
    {
        DeckState GetState(StageDeck deck);

        /// <summary> MIDIコンやUIからの変更はこちらに書き込む </summary>
        DeckState NextState { get; }
    }
}
