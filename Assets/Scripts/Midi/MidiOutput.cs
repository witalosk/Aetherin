using System;
using System.Collections.Generic;
using RosettaUI;
using RtMidi;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// RtMidiを直接使ってMIDI出力を行う<see cref="IMidiOutput"/>の実装
    /// MinisはMIDI入力専用のため、出力はこちらで独自にポートを開いて扱う
    /// </summary>
    public class MidiOutput : MonoBehaviour, IMidiOutput, IUiTarget
    {
        public bool IsConnected => _isOpen;
        public IParams Params => _params;

        [SerializeField]
        private MidiOutputParams _params = new();

        private MidiOut _midiOut;
        private bool _isOpen;
        private string _openedPortName;
        private float _nextReconnectTime;
        private string _lastError;

        private readonly List<string> _portNames = new();

        // モニタ表示用
        private string _lastSentMessage;
        private int _sentMessageCount;

        #region IMidiOutput

        public void SendNoteOn(int noteNumber, int velocity, int channel = 0)
        {
            Span<byte> message = stackalloc byte[3];
            message[0] = (byte)(0x90 | (channel & 0x0F));
            message[1] = (byte)(noteNumber & 0x7F);
            message[2] = (byte)(velocity & 0x7F);
            SendRaw(message);
        }

        public void SendNoteOff(int noteNumber, int channel = 0)
        {
            Span<byte> message = stackalloc byte[3];
            message[0] = (byte)(0x80 | (channel & 0x0F));
            message[1] = (byte)(noteNumber & 0x7F);
            message[2] = 0;
            SendRaw(message);
        }

        public void SendCc(int number, int value, int channel = 0)
        {
            Span<byte> message = stackalloc byte[3];
            message[0] = (byte)(0xB0 | (channel & 0x0F));
            message[1] = (byte)(number & 0x7F);
            message[2] = (byte)(value & 0x7F);
            SendRaw(message);
        }

        public void SendRaw(ReadOnlySpan<byte> message)
        {
            if (!_isOpen || message.IsEmpty) return;

            if (_midiOut.SendMessage(message) < 0)
            {
                // 送信に失敗した場合はデバイスが外れたものとして再接続待ちに戻す
                _lastError = _midiOut.IsOk ? "SendMessage failed" : _midiOut.Error;
                Disconnect();
                return;
            }

            _sentMessageCount++;
            _lastSentMessage = ToHexString(message);
        }

        #endregion

        #region Connection

        private void OnEnable()
        {
            TryConnect();
        }

        private void Update()
        {
            if (_isOpen || Time.unscaledTime < _nextReconnectTime) return;

            TryConnect();
        }

        private void OnDisable()
        {
            if (_isOpen && _params.ClearLedsOnDisable) this.ClearAllLeds();

            Disconnect();
        }

        private void TryConnect()
        {
            _nextReconnectTime = Time.unscaledTime + Mathf.Max(0.5f, _params.ReconnectInterval);

            // ポート一覧を取り直すためハンドルごと作り直す
            Disconnect();

            try
            {
                _midiOut = MidiOut.Create(Api.Unspecified, "Aetherin");
                if (_midiOut.IsInvalid)
                {
                    _lastError = "MIDI出力の初期化に失敗しました";
                    Disconnect();
                    return;
                }

                RefreshPortNames();

                int index = FindPortIndex();
                if (index < 0)
                {
                    _lastError = $"出力ポートが見つかりません ({_params.PortNameFilter})";
                    return;
                }

                _midiOut.OpenPort(index, "Aetherin Output");
                if (!_midiOut.IsOk)
                {
                    _lastError = _midiOut.Error;
                    return;
                }

                _openedPortName = _portNames[index];
                _isOpen = true;
                _lastError = null;
            }
            catch (Exception e)
            {
                _lastError = e.Message;
                Disconnect();
            }
        }

        private void Disconnect()
        {
            _isOpen = false;
            _openedPortName = null;

            if (_midiOut == null) return;

            _midiOut.Dispose();
            _midiOut = null;
        }

        private void RefreshPortNames()
        {
            _portNames.Clear();
            if (_midiOut == null) return;

            for (int i = 0; i < _midiOut.PortCount; i++)
            {
                _portNames.Add(_midiOut.GetPortName(i));
            }
        }

        private int FindPortIndex()
        {
            if (string.IsNullOrEmpty(_params.PortNameFilter)) return _portNames.Count > 0 ? 0 : -1;

            for (int i = 0; i < _portNames.Count; i++)
            {
                if (_portNames[i] != null &&
                    _portNames[i].IndexOf(_params.PortNameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string ToHexString(ReadOnlySpan<byte> message)
        {
            const int maxLength = 8;
            var builder = new System.Text.StringBuilder();

            int count = Mathf.Min(message.Length, maxLength);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) builder.Append(' ');
                builder.Append(message[i].ToString("X2"));
            }

            if (message.Length > maxLength) builder.Append(" ...");

            return builder.ToString();
        }

        #endregion

        #region UI

        public Element AdditiveUi()
        {
            return UI.Fold("MIDI Output",
                UI.Label(() => _isOpen
                    ? $"Connected : {_openedPortName}"
                    : $"Not connected : {_lastError}"),
                UI.Label(() => $"Sent : {_sentMessageCount} ({_lastSentMessage})"),
                UI.Fold("Ports",
                    UI.DynamicElementOnStatusChanged(
                        readStatus: () => _portNames.Count,
                        build: _ => UI.Column(CreatePortNameElements()))
                ),
                UI.Row(
                    UI.Button("Test", SendTestPattern),
                    UI.Button("Clear", () => this.ClearAllLeds())
                )
            ).SetWidth(400f);
        }

        private IEnumerable<Element> CreatePortNameElements()
        {
            if (_portNames.Count == 0)
            {
                yield return UI.Label("(no output port)");
                yield break;
            }

            for (int i = 0; i < _portNames.Count; i++)
            {
                int index = i;
                yield return UI.Label(() => $"{index} : {_portNames[index]}");
            }
        }

        /// <summary>
        /// 8x8パッドを列ごとに色分けして点灯させる (配線確認用)
        /// </summary>
        private void SendTestPattern()
        {
            for (int y = 0; y < ApcMiniMk2.GridSize; y++)
            {
                for (int x = 0; x < ApcMiniMk2.GridSize; x++)
                {
                    // 横方向に色相、縦方向に明度が変わるパターン
                    var color = UnityEngine.Color.HSVToRGB(x / (float)ApcMiniMk2.GridSize, 1f, (y + 1) / (float)ApcMiniMk2.GridSize);
                    this.SetPadRgb(ApcMiniMk2.GetPadNote(x, y), color);
                }
            }

            for (int i = 0; i < ApcMiniMk2.GridSize; i++)
            {
                this.SetButtonLed(ApcMiniMk2.TrackButtonFirst + i, ApcMiniMk2.ButtonLedState.On);
                this.SetButtonLed(ApcMiniMk2.SceneButtonFirst + i, ApcMiniMk2.ButtonLedState.On);
            }
        }

        #endregion
    }
}
