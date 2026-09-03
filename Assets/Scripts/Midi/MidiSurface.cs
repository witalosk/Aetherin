using System;
using System.Collections.Generic;
using RosettaUI;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    [Serializable]
    public class MidiSurfaceParams : IParams
    {
        [Tooltip("px")]
        [Range(12f, 64f)]
        public float PadSize = 26f;

        [Tooltip("px")]
        public float FaderWidth = 260f;

        [Tooltip("実機が接続されていてもUIから操作できるようにする")]
        public bool AlwaysAllowEmulation = false;
    }
    
    /// <summary>
    /// APC mini mk2 の操作面を集約する<see cref="IMidiSurface"/>の実装
    /// LEDの状態を保持して画面に表示し、実機が未接続のときはUIのクリック/スライダーを入力として扱う
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class MidiSurface : MonoBehaviour, IMidiSurface, ISaveAndUiTarget
    {
        private const int ValueCount = 128;
        private const int PadCount = 64;
        private const int ButtonCount = 8;
        private const int FaderCount = 9;

        public bool IsHardwareConnected => _input != null && _input.IsConnected;
        public bool IsEmulating => _params.AlwaysAllowEmulation || !IsHardwareConnected;
        public IParams Params => _params;
        public string Category => UiCategory.Settings;

        public event Action<int, float> OnNoteOn;
        public event Action<int> OnNoteOff;
        public event Action<int, float> OnCcChanged;

        [SerializeField]
        private MidiSurfaceParams _params = new();

        private IMidiInput _input;
        private IMidiOutput _output;

        private readonly Color[] _padColors = new Color[PadCount];
        private readonly ApcMiniMk2.ButtonLedState[] _trackLeds = new ApcMiniMk2.ButtonLedState[ButtonCount];
        private readonly ApcMiniMk2.ButtonLedState[] _sceneLeds = new ApcMiniMk2.ButtonLedState[ButtonCount];

        private readonly bool[] _emulatedNotes = new bool[ValueCount];

        // UIのクリックはフレームのどこで処理されるか決まっていないため、
        // 一度溜めてからUpdateでこのフレームの入力として確定させる
        private readonly bool[] _emulatedNoteOnThisFrame = new bool[ValueCount];
        private readonly bool[] _emulatedNoteOffThisFrame = new bool[ValueCount];
        private readonly List<int> _pendingEmulatedNoteOn = new();
        private readonly List<int> _pendingEmulatedNoteOff = new();

        /// <summary> エミュレートされたCC値。負値は未設定 </summary>
        private readonly float[] _emulatedCcValues = CreateFilledArray(-1f);

        private bool _wasOutputConnected;

        [Inject]
        public void Construct(IMidiInput input, IMidiOutput output)
        {
            _input = input;
            _output = output;

            // MidiBindingがInspector / RosettaUIのどちらからでもLearnできるように、操作面を登録する
            MidiBinding.SetSource(this);

            if (_input == null) return;

            _input.OnNoteOn += HandleHardwareNoteOn;
            _input.OnNoteOff += HandleHardwareNoteOff;
            _input.OnCcChanged += HandleHardwareCcChanged;
        }

        #region Input

        public bool IsNoteOn(int noteNumber)
            => _emulatedNotes[noteNumber] || (_input?.IsNoteOn(noteNumber) ?? false);

        public bool WasNoteOn(int noteNumber)
            => _emulatedNoteOnThisFrame[noteNumber] || (_input?.WasNoteOn(noteNumber) ?? false);

        public bool WasNoteOff(int noteNumber)
            => _emulatedNoteOffThisFrame[noteNumber] || (_input?.WasNoteOff(noteNumber) ?? false);

        public float GetVelocity(int noteNumber)
        {
            float velocity = _input?.GetVelocity(noteNumber) ?? 0f;
            return velocity > 0f ? velocity : (_emulatedNotes[noteNumber] ? 1f : 0f);
        }

        public float GetCc(int number, float defaultValue = 0f)
        {
            // 実機の値を優先し、受信していないものだけエミュレート値を使う
            if (_input != null && _input.TryGetCcRaw(number, out int rawValue)) return rawValue / 127f;

            float emulated = _emulatedCcValues[number];
            return emulated < 0f ? defaultValue : emulated;
        }

        private void HandleHardwareNoteOn(int noteNumber, float velocity) => OnNoteOn?.Invoke(noteNumber, velocity);
        private void HandleHardwareNoteOff(int noteNumber) => OnNoteOff?.Invoke(noteNumber);
        private void HandleHardwareCcChanged(int number, float value) => OnCcChanged?.Invoke(number, value);

        #endregion

        #region LED

        public void SetPad(int noteNumber, Color color)
        {
            int index = noteNumber - ApcMiniMk2.PadFirst;
            if (index < 0 || index >= PadCount) return;

            if (_padColors[index] == color) return;

            _padColors[index] = color;
            _output?.SetPadRgb(noteNumber, color);
        }

        public void SetPad(int x, int y, Color color) => SetPad(ApcMiniMk2.GetPadNote(x, y), color);

        public void SetTrackLed(int index, ApcMiniMk2.ButtonLedState state)
        {
            if (index < 0 || index >= ButtonCount || _trackLeds[index] == state) return;

            _trackLeds[index] = state;
            _output?.SetButtonLed(ApcMiniMk2.TrackButtonFirst + index, state);
        }

        public void SetSceneLed(int index, ApcMiniMk2.ButtonLedState state)
        {
            if (index < 0 || index >= ButtonCount || _sceneLeds[index] == state) return;

            _sceneLeds[index] = state;
            _output?.SetButtonLed(ApcMiniMk2.SceneButtonFirst + index, state);
        }

        public void ClearLeds()
        {
            Array.Fill(_padColors, Color.black);
            Array.Fill(_trackLeds, ApcMiniMk2.ButtonLedState.Off);
            Array.Fill(_sceneLeds, ApcMiniMk2.ButtonLedState.Off);

            _output?.ClearAllLeds();
        }

        public Color GetPadColor(int noteNumber)
        {
            int index = noteNumber - ApcMiniMk2.PadFirst;
            return index < 0 || index >= PadCount ? Color.black : _padColors[index];
        }

        /// <summary>
        /// 保持しているLEDの状態を実機に送り直す
        /// 後から接続された実機のLEDは消えているため、接続を検知したタイミングで呼ぶ
        /// </summary>
        private void ResendAllLeds()
        {
            if (_output == null) return;

            for (int i = 0; i < PadCount; i++)
            {
                _output.SetPadRgb(ApcMiniMk2.PadFirst + i, _padColors[i]);
            }

            for (int i = 0; i < ButtonCount; i++)
            {
                _output.SetButtonLed(ApcMiniMk2.TrackButtonFirst + i, _trackLeds[i]);
                _output.SetButtonLed(ApcMiniMk2.SceneButtonFirst + i, _sceneLeds[i]);
            }
        }

        #endregion

        #region エミュレート

        private static T[] CreateFilledArray<T>(T value)
        {
            var array = new T[ValueCount];
            Array.Fill(array, value);
            return array;
        }

        private void Update()
        {
            PublishEmulatedEvents();

            // 実機が後から接続されたらLEDの状態を送り直す
            bool outputConnected = _output != null && _output.IsConnected;
            if (outputConnected && !_wasOutputConnected) ResendAllLeds();
            _wasOutputConnected = outputConnected;
        }

        /// <summary>
        /// 溜まったUI操作をこのフレームの入力として確定させ、イベントを発火する
        /// </summary>
        private void PublishEmulatedEvents()
        {
            // エミュレーション側の押下状態も毎フレーム破棄し、1フレームだけOnにする。
            Array.Clear(_emulatedNotes, 0, ValueCount);
            Array.Clear(_emulatedNoteOnThisFrame, 0, ValueCount);
            Array.Clear(_emulatedNoteOffThisFrame, 0, ValueCount);

            foreach (int noteNumber in _pendingEmulatedNoteOn)
            {
                _emulatedNotes[noteNumber] = true;
                _emulatedNoteOnThisFrame[noteNumber] = true;
                OnNoteOn?.Invoke(noteNumber, 1f);
            }

            foreach (int noteNumber in _pendingEmulatedNoteOff)
            {
                _emulatedNoteOffThisFrame[noteNumber] = true;
                OnNoteOff?.Invoke(noteNumber);
            }

            _pendingEmulatedNoteOn.Clear();
            _pendingEmulatedNoteOff.Clear();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(MidiBinding.Source, this)) MidiBinding.SetSource(null);

            if (_input == null) return;

            _input.OnNoteOn -= HandleHardwareNoteOn;
            _input.OnNoteOff -= HandleHardwareNoteOff;
            _input.OnCcChanged -= HandleHardwareCcChanged;
        }

        private void TriggerEmulatedNote(int noteNumber)
        {
            // UIのクリックは実機の瞬間的なNoteOnと同じ扱いにする。
            // 押下状態を保持しないため、同じボタンを連続して押せる。
            _pendingEmulatedNoteOn.Add(noteNumber);
        }

        private void SetEmulatedCc(int number, float value)
        {
            _emulatedCcValues[number] = Mathf.Clamp01(value);
            OnCcChanged?.Invoke(number, _emulatedCcValues[number]);
        }

        #endregion

        #region UI

        public Element AdditiveUi()
        {
            return UI.Fold("Surface",
                UI.Label(() => IsHardwareConnected
                    ? (IsEmulating ? "Hardware connected (emulation enabled)" : "Hardware connected")
                    : "Hardware not connected : クリック/ドラッグで操作できます"),
                UI.DynamicElementOnStatusChanged(
                    // 実機の接続状態が変わったら操作可能・不可を切り替えるため作り直す
                    readStatus: () => IsEmulating,
                    build: CreateSurfaceElement),
                UI.Button("Clear LEDs", ClearLeds)
            );
        }

        private Element CreateSurfaceElement(bool emulating)
        {
            return UI.Column(
                UI.Column(CreateGridRows(emulating)),
                CreateTrackRow(emulating),
                UI.Space(),
                UI.Column(CreateFaderElements(emulating))
            );
        }

        /// <summary>
        /// 8x8グリッドと右端のScene Launchボタンを、実機と同じ並び (上の行が先) で作る
        /// </summary>
        private IEnumerable<Element> CreateGridRows(bool emulating)
        {
            for (int y = ApcMiniMk2.GridSize - 1; y >= 0; y--)
            {
                var row = new List<Element>(ApcMiniMk2.GridSize + 1);

                for (int x = 0; x < ApcMiniMk2.GridSize; x++)
                {
                    row.Add(CreatePadElement(ApcMiniMk2.GetPadNote(x, y), emulating));
                }

                int sceneIndex = ApcMiniMk2.GridSize - 1 - y;
                row.Add(CreateButtonElement(ApcMiniMk2.SceneButtonFirst + sceneIndex, sceneIndex, _sceneLeds,
                    Color.green, emulating));

                yield return UI.Row(row);
            }
        }

        private Element CreateTrackRow(bool emulating)
        {
            var row = new List<Element>(ApcMiniMk2.GridSize + 1);

            for (int i = 0; i < ApcMiniMk2.GridSize; i++)
            {
                row.Add(CreateButtonElement(ApcMiniMk2.TrackButtonFirst + i, i, _trackLeds, Color.red, emulating));
            }

            // 実機と同じ並びで、Track Buttonの右隣 (グリッド右下) にShiftを置く
            row.Add(CreateShiftElement(emulating));

            return UI.Row(row);
        }

        /// <summary>
        /// ShiftボタンはLEDを持たないため、押下状態だけを表示する
        /// </summary>
        private Element CreateShiftElement(bool emulating)
        {
            return CreateCellElement(ApcMiniMk2.ShiftButton, emulating, () =>
            {
                var color = new Color(0.3f, 0.3f, 0.3f);
                return IsNoteOn(ApcMiniMk2.ShiftButton) ? Color.Lerp(color, Color.white, 0.65f) : color;
            });
        }

        private Element CreatePadElement(int noteNumber, bool emulating)
        {
            return CreateCellElement(noteNumber, emulating, () => GetPadDisplayColor(noteNumber));
        }

        private Element CreateButtonElement(int noteNumber, int index, ApcMiniMk2.ButtonLedState[] states,
            Color litColor, bool emulating)
        {
            return CreateCellElement(noteNumber, emulating,
                () => GetButtonDisplayColor(noteNumber, states[index], litColor));
        }

        private Element CreateCellElement(int noteNumber, bool emulating, Func<Color> readColor)
        {
            Action onClick = emulating ? () => TriggerEmulatedNote(noteNumber) : null;
            var button = UI.Button("", onClick);

            return button
                .SetWidth(_params.PadSize)
                .SetHeight(_params.PadSize)
                .SetInteractable(emulating)
                .RegisterUpdateCallback(element => element.SetBackgroundColor(readColor()));
        }

        private IEnumerable<Element> CreateFaderElements(bool emulating)
        {
            for (int i = 0; i < FaderCount; i++)
            {
                int cc = i < ApcMiniMk2.GridSize ? ApcMiniMk2.FaderCcFirst + i : ApcMiniMk2.MasterFaderCc;
                string label = i < ApcMiniMk2.GridSize ? $"Fader {i + 1}" : "Master";

                yield return (emulating
                        ? UI.Slider(label, () => GetCc(cc), value => SetEmulatedCc(cc, value), 0f, 1f)
                        : UI.SliderReadOnly(label, () => GetCc(cc), 0f, 1f))
                    .SetWidth(_params.FaderWidth);
            }
        }

        /// <summary> パッドの表示色。押されている間は白寄りにして分かるようにする </summary>
        private Color GetPadDisplayColor(int noteNumber)
        {
            var color = GetPadColor(noteNumber);

            // 消灯しているパッドもグリッドとして見えるようにする
            if (color.maxColorComponent < 0.04f) color = new Color(0.16f, 0.16f, 0.16f);

            return IsNoteOn(noteNumber) ? Color.Lerp(color, Color.white, 0.65f) : color;
        }

        private Color GetButtonDisplayColor(int noteNumber, ApcMiniMk2.ButtonLedState state, Color litColor)
        {
            var color = state switch
            {
                ApcMiniMk2.ButtonLedState.On => litColor,
                ApcMiniMk2.ButtonLedState.Blink => Color.Lerp(litColor * 0.2f, litColor,
                    Mathf.PingPong(Time.unscaledTime * 2f, 1f)),
                _ => litColor * 0.18f,
            };

            return IsNoteOn(noteNumber) ? Color.Lerp(color, Color.white, 0.65f) : color;
        }

        #endregion
    }
}
