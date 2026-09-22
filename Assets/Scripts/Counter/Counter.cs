using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public class CounterParams : IParams
    {
        [Tooltip("独立したカウンターの一覧。ModulationのCounter #はこの並び順です")]
        public List<CounterChannelParams> Counters = new()
        {
            new() { Name = "Counter 1" },
        };
    }

    [Serializable]
    public class CounterChannelParams
    {
        public string Name = "Counter";

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
    public class Counter : MonoBehaviour, ICounter, ICounterBank, ISaveAndUiTarget
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
        public int Value => PrimaryCounter?.Value ?? 0;
        public float AnimatedValue => PrimaryCounter?.AnimatedValue ?? 0f;
        public double LastIncrementTime => PrimaryCounter?.LastIncrementTime ?? double.NegativeInfinity;
        public long IncrementEventId => PrimaryCounter?.IncrementEventId ?? 0;
        public int CounterCount => _params?.Counters?.Count ?? 0;

        [SerializeField] private CounterParams _params = new();
        private readonly List<CounterChannelState> _states = new();
        private CounterChannelState PrimaryCounter => GetCounter(0) as CounterChannelState;

        private void Awake() => _active = this;

        private void OnDestroy()
        {
            if (ReferenceEquals(_active, this)) _active = null;
        }

        private void Update()
        {
            _params ??= new CounterParams();
            _params.Counters ??= new List<CounterChannelParams>();
            if (_params.Counters.Count == 0)
                _params.Counters.Add(new CounterChannelParams { Name = "Counter 1" });

            EnsureStates();
            for (int i = 0; i < _params.Counters.Count; i++)
                _states[i].Update(_params.Counters[i]);
        }

        public void Increment() => PrimaryCounter?.Increment();

        public void Reset() => PrimaryCounter?.Reset();

        public ICounter GetCounter(int index)
        {
            EnsureStates();
            return index >= 0 && index < _states.Count
                ? _states[index]
                : null;
        }

        private void EnsureStates()
        {
            int count = _params?.Counters?.Count ?? 0;
            while (_states.Count < count) _states.Add(new CounterChannelState());
            if (_states.Count > count) _states.RemoveRange(count, _states.Count - count);
        }

        private sealed class CounterChannelState : ICounter
        {
            public int Value { get; private set; }
            public float AnimatedValue { get; private set; }
            public double LastIncrementTime { get; private set; } = double.NegativeInfinity;
            public long IncrementEventId { get; private set; }

            public void Update(CounterChannelParams parameters)
            {
                if (parameters == null) return;
                parameters.IncrementPad ??= new MidiBinding();
                parameters.ResetPad ??= new MidiBinding();

                if (parameters.IncrementPad.WasNoteOn) Increment();
                if (parameters.ResetPad.WasNoteOn) Reset();

                float speed = Mathf.Max(0f, parameters.AnimationSpeed);
                AnimatedValue = speed <= 0f
                    ? Value
                    : Mathf.MoveTowards(AnimatedValue, Value, speed * Time.unscaledDeltaTime);

                parameters.IncrementPad.SetLed(parameters.IncrementColor * parameters.IdleBrightness);
                parameters.ResetPad.SetLed(parameters.ResetColor * parameters.IdleBrightness);
            }

            public void Increment()
            {
                Value++;
                IncrementEventId++;
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
}
