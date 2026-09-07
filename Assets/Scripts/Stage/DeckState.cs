using System;

namespace Aetherin
{
    /// <summary>
    /// デッキ (Current / Next) ごとに切り替わる、見た目を決める状態の集約
    /// デッキ依存の要素を増やすときはここにフィールドを追加する
    /// (各マネージャはNext側への書き込みと、NextPromoted時のCurrentへの昇格を担当する)
    /// </summary>
    public class DeckState
    {
        public ColorPalette Palette = new();
    }

    public interface IDeckStateProvider
    {
        event Action NextPromoted;

        DeckState GetState(StageDeck deck);

        /// <summary> MIDIコンやUIからの変更はこちらに書き込む </summary>
        DeckState NextState { get; }
    }
}
