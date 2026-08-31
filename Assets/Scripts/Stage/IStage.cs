using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// ステージが属する系統 (Current: 本番出力側 / Next: MIDIコンで操作する側)
    /// </summary>
    public enum StageDeck
    {
        Current,
        Next,
    }

    public interface IStage
    {
        string StageName { get; }
        RenderTexture OutputTexture { get; }
    }
}
