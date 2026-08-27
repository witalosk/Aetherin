using System;

namespace Aetherin
{
    /// <summary>
    /// MIDI出力 (パッドのLED制御など) を行うインターフェース
    /// 機材固有の解釈は持たず、素のMIDIメッセージ送信のみを担う
    /// APC mini mk2向けのヘルパーは<see cref="ApcMiniMk2"/>を参照
    /// </summary>
    public interface IMidiOutput
    {
        /// <summary> 出力ポートが開いているか </summary>
        bool IsConnected { get; }

        /// <summary>
        /// ノートオンを送信する
        /// </summary>
        /// <param name="noteNumber">ノート番号 (0..127)</param>
        /// <param name="velocity">ベロシティ (0..127) APC mini mk2では色番号として解釈される</param>
        /// <param name="channel">MIDIチャンネル (0..15) APC mini mk2では点灯の挙動として解釈される</param>
        void SendNoteOn(int noteNumber, int velocity, int channel = 0);

        /// <summary>
        /// ノートオフを送信する
        /// </summary>
        void SendNoteOff(int noteNumber, int channel = 0);

        /// <summary>
        /// コントロールチェンジを送信する
        /// </summary>
        void SendCc(int number, int value, int channel = 0);

        /// <summary>
        /// 任意のMIDIメッセージを送信する (SysExなど)
        /// </summary>
        void SendRaw(ReadOnlySpan<byte> message);
    }
}
