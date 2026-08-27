using System;
using System.Collections.Generic;
using System.Text;
using Minis;
using RosettaUI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Aetherin
{
    /// <summary>
    /// Minisを介してMIDI入力を受け取る<see cref="IMidiInput"/>の実装
    /// 認識された全てのMidiDevice(=チャンネル)の入力を合成して保持する
    ///
    /// Minisのコールバックはフレームの途中 (EditorではEditorApplication.updateからも) 呼ばれるため、
    /// 受信内容は一度キューに溜めて、他のコンポーネントより先に走るUpdateでフレーム単位に確定させる
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class MidiInput : MonoBehaviour, IMidiInput, IUiTarget
    {
        private const int ValueCount = 128;

        public bool IsConnected => _devices.Count > 0;
        public float PitchBend { get; private set; }
        public float ChannelPressure { get; private set; }
        public IParams Params => _params;

        public event Action<int, float> OnCcChanged;
        public event Action<int, float> OnNoteOn;
        public event Action<int> OnNoteOff;

        [SerializeField]
        private MidiInputParams _params = new();

        private readonly List<MidiDevice> _devices = new();

        /// <summary> CCの生値。-1は未受信 </summary>
        private readonly int[] _ccRawValues = new int[ValueCount];

        private readonly bool[] _noteStates = new bool[ValueCount];
        private readonly float[] _velocities = new float[ValueCount];

        /// <summary> このフレームでノートオン/オフがあったか (Updateで確定させる) </summary>
        private readonly bool[] _noteOnThisFrame = new bool[ValueCount];
        private readonly bool[] _noteOffThisFrame = new bool[ValueCount];

        private enum MidiEventType { NoteOn, NoteOff, ControlChange }

        private readonly struct PendingEvent
        {
            public readonly MidiEventType Type;
            public readonly int Number;
            public readonly float Value;

            public PendingEvent(MidiEventType type, int number, float value)
            {
                Type = type;
                Number = number;
                Value = value;
            }
        }

        /// <summary> コールバックが溜めた受信イベント </summary>
        private readonly List<PendingEvent> _pendingEvents = new();

        /// <summary> Update中に処理するイベント (処理中の再入で崩れないよう入れ替えて使う) </summary>
        private readonly List<PendingEvent> _dispatchingEvents = new();

        /// <summary> 一度でもCCを受信した番号 (昇順、モニタ表示用) </summary>
        private readonly List<int> _receivedCcNumbers = new();

        // モニタ表示用の直近受信メッセージ
        private string _lastMessageType;
        private int _lastMessageNumber = -1;
        private float _lastMessageValue;

        private readonly StringBuilder _stringBuilder = new();

        public float GetCc(int number, float defaultValue = 0f)
        {
            int raw = _ccRawValues[number];
            return raw < 0 ? defaultValue : raw / 127f;
        }

        public bool TryGetCcRaw(int number, out int rawValue)
        {
            rawValue = _ccRawValues[number];
            return rawValue >= 0;
        }

        public bool IsNoteOn(int noteNumber) => _noteStates[noteNumber];
        public bool WasNoteOn(int noteNumber) => _noteOnThisFrame[noteNumber];
        public bool WasNoteOff(int noteNumber) => _noteOffThisFrame[noteNumber];
        public float GetVelocity(int noteNumber) => _velocities[noteNumber];

        private void Awake()
        {
            ResetStates();
        }

        /// <summary>
        /// 溜まった受信イベントをこのフレームのものとして確定させ、イベントを発火する
        /// </summary>
        private void Update()
        {
            Array.Clear(_noteOnThisFrame, 0, ValueCount);
            Array.Clear(_noteOffThisFrame, 0, ValueCount);

            if (_pendingEvents.Count == 0) return;

            _dispatchingEvents.Clear();
            _dispatchingEvents.AddRange(_pendingEvents);
            _pendingEvents.Clear();

            foreach (var pendingEvent in _dispatchingEvents)
            {
                switch (pendingEvent.Type)
                {
                    case MidiEventType.NoteOn:
                        _noteOnThisFrame[pendingEvent.Number] = true;
                        OnNoteOn?.Invoke(pendingEvent.Number, pendingEvent.Value);
                        break;

                    case MidiEventType.NoteOff:
                        _noteOffThisFrame[pendingEvent.Number] = true;
                        OnNoteOff?.Invoke(pendingEvent.Number);
                        break;

                    case MidiEventType.ControlChange:
                        OnCcChanged?.Invoke(pendingEvent.Number, pendingEvent.Value);
                        break;
                }
            }
        }

        private void OnEnable()
        {
            foreach (var device in InputSystem.devices)
            {
                if (device is MidiDevice midiDevice) AddDevice(midiDevice);
            }

            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;

            foreach (var device in _devices) Unsubscribe(device);
            _devices.Clear();

            ResetStates();
        }

        #region UI

        public Element AdditiveUi()
        {
            return UI.Fold("MIDI Monitor",
                UI.Label(() => IsConnected
                    ? $"Connected : {_devices.Count} device(s) / channel(s)"
                    : "Not connected (デバイスは最初のMIDIメッセージ受信時に認識されます)"),
                UI.Label(() => _lastMessageNumber < 0
                    ? "Last : -"
                    : $"Last : {_lastMessageType} {_lastMessageNumber} ({_lastMessageValue:F3})"),
                UI.Label(GetActiveNoteText),
                UI.SliderReadOnly("Pitch Bend", () => PitchBend, -1f, 1f),
                UI.SliderReadOnly("Ch. Pressure", () => ChannelPressure, 0f, 1f),
                UI.DynamicElementOnStatusChanged(
                    // 表示対象のCCが増えたときだけスライダーを作り直す
                    readStatus: () => _params.ShowAllCc ? -1 : _receivedCcNumbers.Count,
                    build: _ => UI.Column(CreateCcElements()))
            ).SetWidth(400f);
        }

        private IEnumerable<Element> CreateCcElements()
        {
            if (_params.ShowAllCc)
            {
                for (int i = 0; i < ValueCount; i++)
                {
                    yield return CreateCcElement(i);
                }
                yield break;
            }

            if (_receivedCcNumbers.Count == 0)
            {
                yield return UI.Label("CC : 受信待ち");
                yield break;
            }

            foreach (int number in _receivedCcNumbers)
            {
                yield return CreateCcElement(number);
            }
        }

        private Element CreateCcElement(int number)
        {
            return UI.SliderReadOnly($"CC {number}", () => GetCc(number), 0f, 1f).SetWidth(300f);
        }

        private string GetActiveNoteText()
        {
            _stringBuilder.Clear();
            _stringBuilder.Append("Notes : ");

            int count = 0;
            for (int i = 0; i < ValueCount; i++)
            {
                if (!_noteStates[i]) continue;

                if (count++ > 0) _stringBuilder.Append(", ");
                _stringBuilder.Append(GetNoteName(i)).Append("(").Append(i).Append(") ")
                    .Append(_velocities[i].ToString("F2"));
            }

            if (count == 0) _stringBuilder.Append("-");

            return _stringBuilder.ToString();
        }

        private static readonly string[] NoteNames =
            { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        /// <summary> ノート番号を音名に変換する (Minisの表記に合わせて60をC4とする) </summary>
        private static string GetNoteName(int noteNumber)
            => NoteNames[noteNumber % 12] + (noteNumber / 12 - 1);

        #endregion

        #region Device handling

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is not MidiDevice midiDevice) return;

            switch (change)
            {
                case InputDeviceChange.Added:
                    AddDevice(midiDevice);
                    break;

                case InputDeviceChange.Removed:
                    RemoveDevice(midiDevice);
                    break;
            }
        }

        private void AddDevice(MidiDevice device)
        {
            if (_devices.Contains(device)) return;

            _devices.Add(device);
            device.onWillNoteOn += HandleNoteOn;
            device.onWillNoteOff += HandleNoteOff;
            device.onWillAftertouch += HandleAftertouch;
            device.onWillControlChange += HandleControlChange;
            device.onWillChannelPressure += HandleChannelPressure;
            device.onWillPitchBend += HandlePitchBend;
        }

        private void RemoveDevice(MidiDevice device)
        {
            if (!_devices.Remove(device)) return;

            Unsubscribe(device);

            // デバイスが切断されたときに押されたままのノートが残らないようにする
            for (int i = 0; i < ValueCount; i++)
            {
                if (_noteStates[i]) HandleNoteOffCore(i);
            }
        }

        private void Unsubscribe(MidiDevice device)
        {
            device.onWillNoteOn -= HandleNoteOn;
            device.onWillNoteOff -= HandleNoteOff;
            device.onWillAftertouch -= HandleAftertouch;
            device.onWillControlChange -= HandleControlChange;
            device.onWillChannelPressure -= HandleChannelPressure;
            device.onWillPitchBend -= HandlePitchBend;
        }

        #endregion

        #region MIDI message handling

        private void HandleNoteOn(MidiNoteControl note, float velocity)
        {
            int number = note.noteNumber;
            _noteStates[number] = true;
            _velocities[number] = velocity;

            SetLastMessage("Note On", number, velocity);
            _pendingEvents.Add(new PendingEvent(MidiEventType.NoteOn, number, velocity));
        }

        private void HandleNoteOff(MidiNoteControl note)
        {
            HandleNoteOffCore(note.noteNumber);
        }

        private void HandleNoteOffCore(int number)
        {
            _noteStates[number] = false;
            _velocities[number] = 0f;

            SetLastMessage("Note Off", number, 0f);
            _pendingEvents.Add(new PendingEvent(MidiEventType.NoteOff, number, 0f));
        }

        private void HandleAftertouch(MidiNoteControl note, float pressure)
        {
            _velocities[note.noteNumber] = pressure;
            SetLastMessage("Aftertouch", note.noteNumber, pressure);
        }

        private void HandleControlChange(MidiValueControl control, float value)
        {
            int number = control.controlNumber;
            if (_ccRawValues[number] < 0) AddReceivedCcNumber(number);
            _ccRawValues[number] = Mathf.RoundToInt(value * 127f);

            SetLastMessage("CC", number, value);
            _pendingEvents.Add(new PendingEvent(MidiEventType.ControlChange, number, value));
        }

        private void HandleChannelPressure(AxisControl control, float value)
        {
            ChannelPressure = value;
            SetLastMessage("Ch. Pressure", 0, value);
        }

        private void HandlePitchBend(AxisControl control, float value)
        {
            PitchBend = value;
            SetLastMessage("Pitch Bend", 0, value);
        }

        private void SetLastMessage(string type, int number, float value)
        {
            _lastMessageType = type;
            _lastMessageNumber = number;
            _lastMessageValue = value;
        }

        private void AddReceivedCcNumber(int number)
        {
            int index = _receivedCcNumbers.BinarySearch(number);
            if (index < 0) _receivedCcNumbers.Insert(~index, number);
        }

        #endregion

        private void ResetStates()
        {
            PitchBend = 0f;
            ChannelPressure = 0f;
            _lastMessageType = null;
            _lastMessageNumber = -1;
            _lastMessageValue = 0f;
            _receivedCcNumbers.Clear();
            _pendingEvents.Clear();

            for (int i = 0; i < ValueCount; i++)
            {
                _ccRawValues[i] = -1;
                _noteStates[i] = false;
                _velocities[i] = 0f;
                _noteOnThisFrame[i] = false;
                _noteOffThisFrame[i] = false;
            }
        }
    }
}
