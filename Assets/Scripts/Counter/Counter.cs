using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public class CounterParams : IParams
    {
        [Tooltip("押すたびにカウントを1増やすPad")]
        public MidiBinding IncrementPad = new();

        [Tooltip("カウントを0へ戻すPad")]
        public MidiBinding ResetPad = new();

        public Color IncrementColor = new(0.3f, 0.9f, 1f);
        public Color ResetColor = new(1f, 0.3f, 0.35f);
        [Tooltip("カウント値へ追従する速さ。0で即時に切り替わります")]
        [Min(0f)] public float AnimationSpeed = 5f;
        [Range(0f, 0.5f)] public float IdleBrightness = 0.08f;
    }

    /// <summary>MIDI Padで操作し、FXのモジュレーション入力として使える整数カウンター。</summary>
    public class Counter : MonoBehaviour, ICounter, ISaveAndUiTarget
    {
        private static Counter _active;

        public static ICounter Active
        {
            get
            {
                if (_active == null) _active = FindFirstObjectByType<Counter>();
                return _active;
            }
        }

        public IParams Params => _params;
        public string Category => UiCategory.Settings;
        public int Value { get; private set; }
        public float AnimatedValue { get; private set; }
        public double LastIncrementTime { get; private set; } = double.NegativeInfinity;

        [SerializeField] private CounterParams _params = new();

        private void Awake() => _active = this;

        private void OnDestroy()
        {
            if (ReferenceEquals(_active, this)) _active = null;
        }

        private void Update()
        {
            _params ??= new CounterParams();

            if (_params.IncrementPad.WasNoteOn) Increment();
            if (_params.ResetPad.WasNoteOn) Reset();

            float speed = Mathf.Max(0f, _params.AnimationSpeed);
            AnimatedValue = speed <= 0f
                ? Value
                : Mathf.MoveTowards(AnimatedValue, Value, speed * Time.unscaledDeltaTime);

            _params.IncrementPad.SetLed(_params.IncrementColor * _params.IdleBrightness);
            _params.ResetPad.SetLed(_params.ResetColor * _params.IdleBrightness);
        }

        public void Increment()
        {
            Value++;
            LastIncrementTime = Time.unscaledTimeAsDouble;
        }

        public void Reset()
        {
            Value = 0;
            AnimatedValue = 0f;
            LastIncrementTime = double.NegativeInfinity;
        }
    }
}
