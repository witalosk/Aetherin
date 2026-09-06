using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using RosettaUI;
using RtMidi;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public class MidiOutputParams : IParams
    {
        [Tooltip("部分一致、大文字小文字は無視")]
        public string PortNameFilter = "APC mini mk2";

        [Tooltip("秒")]
        public float ReconnectInterval = 2f;

        public bool ClearLedsOnDisable = true;
    }
    
    /// <summary>
    /// RtMidiを直接使ってMIDI出力を行う<see cref="IMidiOutput"/>の実装
    /// MinisはMIDI入力専用のため、出力はこちらで独自にポートを開いて扱う
    /// </summary>
    public class MidiOutput : MonoBehaviour, IMidiOutput, ISaveAndUiTarget
    {
        public bool IsConnected => _isOpen;
        public IParams Params => _params;
        public string Category => UiCategory.Settings;

        [SerializeField]
        private MidiOutputParams _params = new();

        // RtMidiのOpen/Send/DisposeはすべてWorkerだけで実行する。
        // Windows MIDIドライバがブロックしてもUnityメインスレッドを止めないため。
        private MidiOut _midiOut;
        private volatile bool _isOpen;
        private string _openedPortName;
        private string _lastError;

        private readonly List<string> _portNames = new();
        private readonly object _stateLock = new();
        private readonly ConcurrentQueue<byte[]> _sendQueue = new();
        private readonly AutoResetEvent _workerWakeSignal = new(false);
        private Thread _worker;
        private volatile bool _stopWorker;
        private string _portNameFilter;
        private int _reconnectIntervalMilliseconds;
        private long _nextReconnectTicks;

        // モニタ表示用
        private string _lastSentMessage;
        private int _sentMessageCount;

        private void Awake() => MidiDiagnostics.Initialize(Application.persistentDataPath);

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
            if (message.IsEmpty || _worker == null) return;

            _sendQueue.Enqueue(message.ToArray());
            while (_sendQueue.Count > 512) _sendQueue.TryDequeue(out _);
            _workerWakeSignal.Set();
        }

        #endregion

        #region Connection

        private void OnEnable()
        {
            _portNameFilter = _params.PortNameFilter ?? string.Empty;
            _reconnectIntervalMilliseconds = Mathf.RoundToInt(Mathf.Max(0.5f, _params.ReconnectInterval) * 1000f);
            _stopWorker = false;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Aetherin MIDI Output" };
            _worker.Start();
        }

        private void OnDisable()
        {
            _stopWorker = true;
            _workerWakeSignal.Set();
            // ネイティブ送信が詰まっていてもJoinでUnityを待たせない。
            _worker?.Join(100);
            _worker = null;
            while (_sendQueue.TryDequeue(out _)) { }
        }

        private void WorkerLoop()
        {
            while (!_stopWorker)
            {
                if (!_isOpen) TryConnectWorker();
                if (_isOpen) SendQueuedMessagesWorker();
                _workerWakeSignal.WaitOne(50);
            }
            DisconnectWorker();
        }

        private void TryConnectWorker()
        {
            long now = DateTime.UtcNow.Ticks;
            if (now < _nextReconnectTicks) return;
            _nextReconnectTicks = now + _reconnectIntervalMilliseconds * TimeSpan.TicksPerMillisecond;

            // ポート一覧を取り直すためハンドルごと作り直す
            DisconnectWorker();

            try
            {
                _midiOut = MidiOut.Create(Api.Unspecified, "Aetherin");
                if (_midiOut.IsInvalid)
                {
                    _lastError = "MIDI出力の初期化に失敗しました";
                    DisconnectWorker();
                    return;
                }

                RefreshPortNamesWorker();

                int index = FindPortIndexWorker();
                if (index < 0)
                {
                    _lastError = $"出力ポートが見つかりません ({_portNameFilter})";
                    return;
                }

                _midiOut.OpenPort(index, "Aetherin Output");
                if (!_midiOut.IsOk)
                {
                    _lastError = _midiOut.Error;
                    return;
                }

                lock (_stateLock) _openedPortName = _portNames[index];
                _isOpen = true;
                _lastError = null;
            }
            catch (Exception e)
            {
                _lastError = e.Message;
                DisconnectWorker();
            }
        }

        private void SendQueuedMessagesWorker()
        {
            while (_sendQueue.TryDequeue(out byte[] message))
            {
                MidiDiagnostics.RecordCritical($"MIDI output begin ({message.Length} bytes)");
                int result = _midiOut.SendMessage(message);
                MidiDiagnostics.Record($"MIDI output complete ({message.Length} bytes, result={result})");
                if (result < 0)
                {
                    _lastError = _midiOut.IsOk ? "SendMessage failed" : _midiOut.Error;
                    DisconnectWorker();
                    return;
                }

                Interlocked.Increment(ref _sentMessageCount);
                _lastSentMessage = ToHexString(message);
            }
        }

        private void DisconnectWorker()
        {
            _isOpen = false;
            lock (_stateLock) _openedPortName = null;

            if (_midiOut == null) return;

            _midiOut.Dispose();
            _midiOut = null;
        }

        private void RefreshPortNamesWorker()
        {
            lock (_stateLock) _portNames.Clear();
            if (_midiOut == null) return;

            for (int i = 0; i < _midiOut.PortCount; i++)
            {
                lock (_stateLock) _portNames.Add(_midiOut.GetPortName(i));
            }
        }

        private int FindPortIndexWorker()
        {
            lock (_stateLock)
            {
                if (string.IsNullOrEmpty(_portNameFilter)) return _portNames.Count > 0 ? 0 : -1;

                for (int i = 0; i < _portNames.Count; i++)
                {
                    if (_portNames[i] != null &&
                        _portNames[i].IndexOf(_portNameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return i;
                    }
                }

                return -1;
            }
        }

        private static string ToHexString(ReadOnlySpan<byte> message)
        {
            const int maxLength = 8;
            var builder = new System.Text.StringBuilder();

            int count = Math.Min(message.Length, maxLength);
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
                        readStatus: () => GetPortNamesSnapshot().Length,
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
            string[] portNames = GetPortNamesSnapshot();
            if (portNames.Length == 0)
            {
                yield return UI.Label("(no output port)");
                yield break;
            }

            for (int i = 0; i < portNames.Length; i++)
            {
                int index = i;
                yield return UI.Label(() => $"{index} : {portNames[index]}");
            }
        }

        private string[] GetPortNamesSnapshot()
        {
            lock (_stateLock) return _portNames.ToArray();
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
