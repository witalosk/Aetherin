using System;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// MIDIコントローラ (APC mini mk2) を1つの操作面として扱うインターフェース
    /// 入力の読み出しとLEDの状態を集約して保持し、実機が未接続のときはUI上の操作を入力として扱う
    ///
    /// アプリ側のロジックは<see cref="IMidiInput"/>/<see cref="IMidiOutput"/>ではなく
    /// こちらを参照することで、実機の有無にかかわらず同じコードで動作する
    /// </summary>
    public interface IMidiSurface
    {
        /// <summary> 実機が接続されているか </summary>
        bool IsHardwareConnected { get; }

        /// <summary> UI操作によるエミュレートが有効か (実機未接続時) </summary>
        bool IsEmulating { get; }

        #region Input

        bool IsNoteOn(int noteNumber);
        bool WasNoteOn(int noteNumber);
        bool WasNoteOff(int noteNumber);
        float GetVelocity(int noteNumber);
        float GetCc(int number, float defaultValue = 0f);

        event Action<int, float> OnNoteOn;
        event Action<int> OnNoteOff;
        event Action<int, float> OnCcChanged;

        #endregion

        #region LED

        /// <summary>
        /// パッドの色を設定する (SysExで任意のRGBを送る)
        /// </summary>
        void SetPad(int noteNumber, Color color);

        /// <summary>
        /// パッドの色を設定する
        /// </summary>
        /// <param name="x">左からの位置 (0..7)</param>
        /// <param name="y">下からの位置 (0..7)</param>
        void SetPad(int x, int y, Color color);

        /// <summary>
        /// グリッド下のボタン (Track Button 1-8) のLEDを設定する
        /// </summary>
        /// <param name="index">左からの位置 (0..7)</param>
        void SetTrackLed(int index, ApcMiniMk2.ButtonLedState state);

        /// <summary>
        /// 右端のボタン (Scene Launch 1-8) のLEDを設定する
        /// </summary>
        /// <param name="index">上からの位置 (0..7)</param>
        void SetSceneLed(int index, ApcMiniMk2.ButtonLedState state);

        /// <summary>
        /// 全てのLEDを消灯する
        /// </summary>
        void ClearLeds();

        /// <summary>
        /// 現在設定されているパッドの色を取得する
        /// </summary>
        Color GetPadColor(int noteNumber);

        #endregion
    }
}
