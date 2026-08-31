using System;

namespace Aetherin
{
    /// <summary>
    /// MIDI入力を読み出すインターフェース
    /// 単一機材の使用を前提としており、接続されている全てのMIDIデバイス/チャンネルの入力を
    /// 1つの入力空間として合成して扱う
    /// </summary>
    public interface IMidiInput
    {
        /// <summary>
        /// MIDIデバイスが1つ以上認識されているか
        /// (InputSystemの仕様上、最初のMIDIメッセージを受信するまでデバイスは認識されない)
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// ピッチベンドの現在値 (-1..1、未受信時は0)
        /// </summary>
        float PitchBend { get; }

        /// <summary>
        /// チャンネルプレッシャー(チャンネルアフタータッチ)の現在値 (0..1、未受信時は0)
        /// </summary>
        float ChannelPressure { get; }

        /// <summary>
        /// コントロールチェンジの現在値 (0..1)
        /// </summary>
        /// <param name="number">CC番号 (0..127)</param>
        /// <param name="defaultValue">一度も受信していない場合に返す値</param>
        float GetCc(int number, float defaultValue = 0f);

        /// <summary>
        /// コントロールチェンジの生値 (0..127) を取得する
        /// 相対値エンコーダなど、正規化前の値が必要な場合に使う
        /// </summary>
        /// <returns>一度も受信していない場合はfalse</returns>
        bool TryGetCcRaw(int number, out int rawValue);

        bool IsNoteOn(int noteNumber);

        /// <summary>
        /// このフレームでノートが押されたか
        /// </summary>
        bool WasNoteOn(int noteNumber);

        /// <summary>
        /// このフレームでノートが離されたか
        /// </summary>
        bool WasNoteOff(int noteNumber);

        /// <summary>
        /// ノートのベロシティ (0..1、ノートオフ時は0)
        /// ポリフォニックアフタータッチを受信した場合はその値で更新される
        /// </summary>
        float GetVelocity(int noteNumber);

        /// <summary>
        /// コントロールチェンジ受信時に発火する (CC番号, 値 0..1)
        /// </summary>
        event Action<int, float> OnCcChanged;

        /// <summary>
        /// ノートオン受信時に発火する (ノート番号, ベロシティ 0..1)
        /// 1フレーム内の複数回の入力を取りこぼしたくない場合に使う
        /// </summary>
        event Action<int, float> OnNoteOn;

        event Action<int> OnNoteOff;
    }
}
