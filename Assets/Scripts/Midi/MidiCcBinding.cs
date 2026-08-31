using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// MIDIコントローラのフェーダー (CC) 1つへの割り当て
    /// ノート用の<see cref="MidiBinding"/>と同様にLearnに対応する
    /// </summary>
    [Serializable]
    public class MidiCcBinding
    {
        public const int Unassigned = -1;

        /// <summary> Learn確定に必要なフェーダーの移動量 (触れただけで誤割り当てされないようにする) </summary>
        private const float LearnMoveThreshold = 0.05f;

        [SerializeField]
        [Tooltip("割り当てられたCC番号 (-1は未割り当て)")]
        [Range(Unassigned, 127)]
        private int _ccNumber = Unassigned;

        private static MidiCcBinding _learningBinding;

        /// <summary> Learn開始後に各CCから最初に受信した値。ここからの移動量で割り当てを確定する </summary>
        private static readonly Dictionary<int, float> LearnInitialValues = new();

        public MidiCcBinding()
        {
        }

        public MidiCcBinding(int ccNumber)
        {
            _ccNumber = ccNumber;
        }

        public int CcNumber
        {
            get => _ccNumber;
            set => _ccNumber = value < 0 || value > 127 ? Unassigned : value;
        }

        public bool IsAssigned => _ccNumber >= 0 && _ccNumber <= 127;

        public bool IsLearning => _learningBinding == this;

        private static IMidiSurface Source => MidiBinding.Source;

        /// <summary> 現在値 (0..1) </summary>
        public float Value => GetValue();

        public float GetValue(float defaultValue = 0f)
            => IsAssigned && Source != null ? Source.GetCc(_ccNumber, defaultValue) : defaultValue;

        #region Learn

        /// <summary>
        /// 次に一定量動かされたフェーダーを割り当てる
        /// </summary>
        public void BeginLearn()
        {
            if (Source == null) return;

            StopLearn();

            _learningBinding = this;
            LearnInitialValues.Clear();
            Source.OnCcChanged += HandleLearnCcChanged;
        }

        public void CancelLearn()
        {
            if (IsLearning) StopLearn();
        }

        public void Clear()
        {
            CancelLearn();
            _ccNumber = Unassigned;
        }

        private static void HandleLearnCcChanged(int number, float value)
        {
            if (!LearnInitialValues.TryGetValue(number, out float initialValue))
            {
                LearnInitialValues[number] = value;
                return;
            }

            if (Mathf.Abs(value - initialValue) < LearnMoveThreshold) return;

            var target = _learningBinding;
            StopLearn();

            if (target != null) target._ccNumber = number;
        }

        /// <summary>
        /// MIDI操作面が差し替わるときにも呼ばれる
        /// </summary>
        public static void StopLearn()
        {
            if (_learningBinding == null) return;

            if (Source != null) Source.OnCcChanged -= HandleLearnCcChanged;
            _learningBinding = null;
            LearnInitialValues.Clear();
        }

        #endregion

        /// <summary>
        /// 割り当て先を実機のフェーダー名で表す
        /// </summary>
        public string Describe()
        {
            if (!IsAssigned) return "unassigned";

            if (_ccNumber >= ApcMiniMk2.FaderCcFirst && _ccNumber < ApcMiniMk2.FaderCcFirst + ApcMiniMk2.GridSize)
            {
                return $"Fader {_ccNumber - ApcMiniMk2.FaderCcFirst + 1} / CC {_ccNumber}";
            }

            if (_ccNumber == ApcMiniMk2.MasterFaderCc) return $"Master Fader / CC {_ccNumber}";

            return $"CC {_ccNumber}";
        }

        public override string ToString() => Describe();
    }
}
