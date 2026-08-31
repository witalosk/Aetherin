using System;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// MIDIコントローラのボタン1つへの割り当て
    /// </summary>
    [Serializable]
    public class MidiBinding
    {
        public const int Unassigned = -1;

        [SerializeField]
        [Tooltip("割り当てられたノート番号 (-1は未割り当て)")]
        [Range(Unassigned, 127)]
        private int _noteNumber = Unassigned;

        public static IMidiSurface Source { get; private set; }

        private int _assignedFrame = -1;
        private int _litNoteNumber = Unassigned;
        
        private static MidiBinding _learningBinding;

        public MidiBinding()
        {
        }

        public MidiBinding(int noteNumber)
        {
            _noteNumber = noteNumber;
        }

        public int NoteNumber
        {
            get => _noteNumber;
            set => _noteNumber = value < 0 || value > 127 ? Unassigned : value;
        }

        public bool IsAssigned => _noteNumber >= 0 && _noteNumber <= 127;

        public bool IsLearning => _learningBinding == this;

        #region Input

        public bool IsNoteOn => IsAssigned && Source != null && Source.IsNoteOn(_noteNumber);

        public bool WasNoteOn =>
            IsAssigned && _assignedFrame != Time.frameCount && Source != null && Source.WasNoteOn(_noteNumber);

        public bool WasNoteOff =>
            IsAssigned && _assignedFrame != Time.frameCount && Source != null && Source.WasNoteOff(_noteNumber);

        public float Velocity => IsAssigned && Source != null ? Source.GetVelocity(_noteNumber) : 0f;

        #endregion

        #region LED

        /// <summary>
        /// 割り当てられたボタンのLEDを設定する
        /// 8x8パッドはRGB、Track / Sceneボタンは単色なので明るさで点灯・消灯を決める
        /// (ShiftボタンはLEDを持たないため何も起きない)
        /// </summary>
        public void SetLed(Color color)
        {
            if (Source == null) return;

            // 割り当てを変えたときに元のボタンが光ったままにならないように消灯する
            if (_litNoteNumber != _noteNumber)
            {
                if (_litNoteNumber >= 0) SetLed(_litNoteNumber, Color.black);
                _litNoteNumber = _noteNumber;
            }

            if (!IsAssigned) return;

            SetLed(_noteNumber, color);
        }

        public void ClearLed() => SetLed(Color.black);

        private static void SetLed(int noteNumber, Color color)
        {
            if (noteNumber <= ApcMiniMk2.PadLast)
            {
                Source.SetPad(noteNumber, color);
                return;
            }

            var state = color.maxColorComponent > 0.5f
                ? ApcMiniMk2.ButtonLedState.On
                : ApcMiniMk2.ButtonLedState.Off;

            if (noteNumber >= ApcMiniMk2.TrackButtonFirst && noteNumber <= ApcMiniMk2.TrackButtonLast)
            {
                Source.SetTrackLed(noteNumber - ApcMiniMk2.TrackButtonFirst, state);
            }
            else if (noteNumber >= ApcMiniMk2.SceneButtonFirst && noteNumber <= ApcMiniMk2.SceneButtonLast)
            {
                Source.SetSceneLed(noteNumber - ApcMiniMk2.SceneButtonFirst, state);
            }
        }

        #endregion

        #region Learn

        /// <summary>
        /// 次に押されたボタンを割り当てる
        /// </summary>
        public void BeginLearn()
        {
            if (Source == null) return;

            StopLearn();

            _learningBinding = this;
            Source.OnNoteOn += HandleLearnNoteOn;
        }

        public void CancelLearn()
        {
            if (IsLearning) StopLearn();
        }

        public void Clear()
        {
            CancelLearn();
            ClearLed();
            _noteNumber = Unassigned;
        }

        private static void HandleLearnNoteOn(int noteNumber, float velocity)
        {
            var target = _learningBinding;
            StopLearn();

            if (target == null) return;

            target._noteNumber = noteNumber;

            // Learnで押したボタンは、その押下を入力として扱わない
            target._assignedFrame = Time.frameCount;
        }

        private static void StopLearn()
        {
            if (_learningBinding == null) return;

            if (Source != null) Source.OnNoteOn -= HandleLearnNoteOn;
            _learningBinding = null;
        }

        #endregion

        /// <summary>
        /// 割り当て先を実機のボタン名で表す
        /// </summary>
        public string Describe()
        {
            if (!IsAssigned) return "unassigned";

            if (_noteNumber <= ApcMiniMk2.PadLast)
            {
                var (x, y) = ApcMiniMk2.GetPadPosition(_noteNumber);
                return $"Pad ({x}, {y}) / note {_noteNumber}";
            }

            if (_noteNumber >= ApcMiniMk2.TrackButtonFirst && _noteNumber <= ApcMiniMk2.TrackButtonLast)
            {
                return $"Track {_noteNumber - ApcMiniMk2.TrackButtonFirst + 1} / note {_noteNumber}";
            }

            if (_noteNumber >= ApcMiniMk2.SceneButtonFirst && _noteNumber <= ApcMiniMk2.SceneButtonLast)
            {
                return $"Scene {_noteNumber - ApcMiniMk2.SceneButtonFirst + 1} / note {_noteNumber}";
            }

            if (_noteNumber == ApcMiniMk2.ShiftButton) return $"Shift / note {_noteNumber}";

            return $"note {_noteNumber}";
        }

        public override string ToString() => Describe();

        public static void SetSource(IMidiSurface surface)
        {
            StopLearn();
            MidiCcBinding.StopLearn();
            Source = surface;
        }
    }
}
