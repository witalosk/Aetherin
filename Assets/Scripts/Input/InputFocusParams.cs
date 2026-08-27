using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public class InputFocusParams : IParams
    {
        [Tooltip("Game Viewにフォーカスが無くてもキー入力をゲーム側で受け取る (Editorでのみ効果あり)")]
        public bool AlwaysSendInputToGame = true;

        [Tooltip("ウィンドウが非アクティブでも入力を受け取る (ビルド後にも効果あり)")]
        public bool IgnoreApplicationFocus = true;
    }
}
