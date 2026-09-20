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

        bool IsPreparingNext { get; }

        StageDeck EditingDeck { get; }

        bool IsDeckEditable(StageDeck deck);

        DeckState GetState(StageDeck deck);
        int GetCameraWorkChangeCount(StageDeck deck);

        DeckState EditingState { get; }

        /// <summary> 通常モードのNext状態。デッキを明示して扱う処理向け。 </summary>
        DeckState NextState { get; }
    }
}
