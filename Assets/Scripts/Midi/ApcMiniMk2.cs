using System;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// AKAI APC mini mk2 固有のMIDI仕様 (LED制御・ノート配置)
    /// 出典: APC mini mk2 Communications Protocol v1.0
    /// </summary>
    public static class ApcMiniMk2
    {
        public const int GridSize = 8;

        /// <summary> 8x8パッド (Clip Launch) のノート番号。0が左下、右上が63 </summary>
        public const int PadFirst = 0x00;
        public const int PadLast = 0x3F;

        /// <summary> グリッド下の横一列のボタン (Track Button 1-8)。LEDは赤単色 </summary>
        public const int TrackButtonFirst = 0x64;
        public const int TrackButtonLast = 0x6B;

        /// <summary> 右端の縦一列のボタン (Scene Launch 1-8)。LEDは緑単色 </summary>
        public const int SceneButtonFirst = 0x70;
        public const int SceneButtonLast = 0x77;

        /// <summary> Shiftボタン。LEDは無し </summary>
        public const int ShiftButton = 0x7A;

        /// <summary> フェーダー1-8のCC番号 (0x30-0x37) </summary>
        public const int FaderCcFirst = 0x30;

        public const int MasterFaderCc = 0x38;

        /// <summary>
        /// パッドLEDの点灯挙動。MIDIチャンネルとして送信する
        /// </summary>
        public enum PadBehaviour
        {
            Brightness10 = 0,
            Brightness25 = 1,
            Brightness50 = 2,
            Brightness65 = 3,
            Brightness75 = 4,
            Brightness90 = 5,

            /// <summary> 100%で点灯 </summary>
            Solid = 6,

            Pulse1Per16 = 7,
            Pulse1Per8 = 8,
            Pulse1Per4 = 9,
            Pulse1Per2 = 10,

            Blink1Per24 = 11,
            Blink1Per16 = 12,
            Blink1Per8 = 13,
            Blink1Per4 = 14,
            Blink1Per2 = 15,
        }

        /// <summary>
        /// Track / Scene ボタンの単色LEDの状態。ベロシティとして送信する
        /// </summary>
        public enum ButtonLedState
        {
            Off = 0,
            On = 1,
            Blink = 2,
        }

        /// <summary>
        /// 内蔵カラーパレットの主要な色番号 (ベロシティとして送信する)
        /// </summary>
        public static class ColorIndex
        {
            public const int Black = 0;
            public const int White = 3;
            public const int Red = 5;
            public const int Yellow = 13;
            public const int Green = 21;
            public const int Blue = 45;
            public const int Pink = 53;
            public const int Cyan = 90;
        }

        /// <summary>
        /// グリッド座標からパッドのノート番号を得る
        /// </summary>
        /// <param name="x">左からの位置 (0..7)</param>
        /// <param name="y">下からの位置 (0..7)</param>
        public static int GetPadNote(int x, int y) => PadFirst + y * GridSize + x;

        /// <summary>
        /// パッドのノート番号からグリッド座標を得る
        /// </summary>
        public static (int x, int y) GetPadPosition(int noteNumber)
        {
            int index = noteNumber - PadFirst;
            return (index % GridSize, index / GridSize);
        }

        /// <summary>
        /// 内蔵カラーパレットの色番号でパッドを点灯させる
        /// </summary>
        public static void SetPad(this IMidiOutput output, int noteNumber, int colorIndex,
            PadBehaviour behaviour = PadBehaviour.Solid)
        {
            output.SendNoteOn(noteNumber, colorIndex, (int)behaviour);
        }

        /// <summary>
        /// 内蔵カラーパレットの色番号でパッドを点灯させる
        /// </summary>
        public static void SetPad(this IMidiOutput output, int x, int y, int colorIndex,
            PadBehaviour behaviour = PadBehaviour.Solid)
        {
            output.SetPad(GetPadNote(x, y), colorIndex, behaviour);
        }

        /// <summary>
        /// SysExで任意のRGBカラーをパッドに設定する
        /// (パレット外の色を出せるが、点滅などの挙動指定はできない)
        /// </summary>
        public static void SetPadRgb(this IMidiOutput output, int noteNumber, Color color)
        {
            output.SetPadRgb(noteNumber, noteNumber, color);
        }

        /// <summary>
        /// SysExで連続する範囲のパッドに同じRGBカラーを設定する
        /// </summary>
        public static void SetPadRgb(this IMidiOutput output, int firstNote, int lastNote, Color color)
        {
            // 各色は0-255を7bit x 2に分割して送る
            int r = ToByteValue(color.r);
            int g = ToByteValue(color.g);
            int b = ToByteValue(color.b);

            Span<byte> message = stackalloc byte[16];
            message[0] = 0xF0;                          // SysEx開始
            message[1] = 0x47;                          // AKAI
            message[2] = 0x7F;                          // Device ID
            message[3] = 0x4F;                          // APC mini mk2
            message[4] = 0x24;                          // RGB LED制御
            message[5] = 0x00;                          // データ長 MSB
            message[6] = 0x08;                          // データ長 LSB (1グループ = 8バイト)
            message[7] = (byte)(firstNote & 0x7F);
            message[8] = (byte)(lastNote & 0x7F);
            message[9] = (byte)(r >> 7);
            message[10] = (byte)(r & 0x7F);
            message[11] = (byte)(g >> 7);
            message[12] = (byte)(g & 0x7F);
            message[13] = (byte)(b >> 7);
            message[14] = (byte)(b & 0x7F);
            message[15] = 0xF7;                         // SysEx終了

            output.SendRaw(message);
        }

        /// <summary>
        /// Track / Scene ボタンの単色LEDを制御する
        /// </summary>
        public static void SetButtonLed(this IMidiOutput output, int noteNumber, ButtonLedState state)
        {
            output.SendNoteOn(noteNumber, (int)state);
        }

        public static void ClearAllLeds(this IMidiOutput output)
        {
            output.SetPadRgb(PadFirst, PadLast, Color.black);

            for (int note = TrackButtonFirst; note <= TrackButtonLast; note++)
            {
                output.SetButtonLed(note, ButtonLedState.Off);
            }

            for (int note = SceneButtonFirst; note <= SceneButtonLast; note++)
            {
                output.SetButtonLed(note, ButtonLedState.Off);
            }
        }

        private static int ToByteValue(float value) => Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
    }
}
