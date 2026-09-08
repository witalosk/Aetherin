using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    public enum FloatModulationSource
    {
        Lfo,
        Kick,
        SnareClap,
        MidiCc,
        Beat,
        Bar,
        ElapsedTime,
        BeatAccumulator,
        BarAccumulator,
        InputVolume,
        Beat2And4,
        Counter,
        CounterPulse,
    }

    public enum FloatModulationOperation
    {
        Add,
        Multiply,
        Override,
    }

    public enum ElapsedTimeCurve
    {
        Linear,
        Power,
        Logarithm,
    }

    public enum LfoWaveform
    {
        Sine,
        Triangle, 
        Saw,
        Square,
    }

    public enum AccumulatorResetMode
    {
        Never,
        EveryBar,
        AfterNEvents,
        OnStop,
    }

    public enum AccumulatorLimitMode
    {
        None,
        Clamp,
        Wrap,
        PingPong,
    }

    public enum CounterValueMode
    {
        Raw,
        Clamp,
        PingPong,
        RepeatModX,
    }

    public enum AccumulatorTransitionMode
    {
        Instant,
        Linear,
        SmoothStep,
        EaseOut,
    }

    public readonly struct ModulationContext
    {
        public readonly double Time;
        /// <summary>Owner activation (layer/effect) からの経過秒数。</summary>
        public readonly double ElapsedTime;
        public readonly IAudioFeatureProvider Audio;
        public readonly IBeatManager Beat;
        public readonly ICounter Counter;
        public readonly bool AllowMidi;
        public readonly float AnimationPhaseOffset;

        public ModulationContext(
            double time,
            IAudioFeatureProvider audio,
            IBeatManager beat,
            bool allowMidi,
            float animationPhaseOffset = 0f,
            ICounter counter = null,
            double? elapsedTime = null)
        {
            Time = time;
            ElapsedTime = elapsedTime ?? time;
            Audio = audio;
            Beat = beat;
            Counter = counter;
            AllowMidi = allowMidi;
            AnimationPhaseOffset = animationPhaseOffset;
        }

        public ModulationContext WithAnimationPhaseOffset(float offset) =>
            new(Time, Audio, Beat, AllowMidi, AnimationPhaseOffset + offset, Counter, ElapsedTime);

        public ModulationContext WithElapsedTime(double elapsedTime) =>
            new(Time, Audio, Beat, AllowMidi, AnimationPhaseOffset, Counter, elapsedTime);
    }

    [Serializable]
    public class FloatModulator
    {
        public bool Enabled = true;
        public FloatModulationSource Source;
        public FloatModulationOperation Operation;

        [Tooltip("入力値へ掛ける量")]
        public float Amount = 1f;

        [Tooltip("Amount適用後に加える値")]
        public float Offset;

        [Min(0f)]
        public float LfoFrequency = 1f;

        [Range(0f, 1f)]
        public float LfoPhase;

        public LfoWaveform LfoWaveform;
        public bool LfoUnipolar;

        [Tooltip("Elapsed Timeへ適用するカーブ")]
        public ElapsedTimeCurve ElapsedTimeCurve;
        [Tooltip("Power は指数、Logarithm は底。Logarithm は log(1 + t) を使用します")]
        [Min(0.001f)] public float ElapsedTimeCurveValue = 2f;
        [Tooltip("EveryBar と AfterNEvents は Beat Manager のイベントを基準に経過時間を0へ戻します")]
        public AccumulatorResetMode ElapsedTimeReset;
        [Min(1)] public int ElapsedTimeResetAfterEvents = 4;
        public AccumulatorLimitMode ElapsedTimeLimit;
        public float ElapsedTimeMin;
        public float ElapsedTimeMax = 1f;

        [Tooltip("Beat / Barはパルスの減衰、Counterは値のカーブを調整します。1で線形です")]
        [Min(0.01f)]
        public float BeatPulseSharpness = 3f;

        public CounterValueMode CounterValueMode;
        public float CounterMin;
        public float CounterMax = 1f;
        [Min(0.001f)] public float CounterRepeatMod = 4f;
        [Min(0.001f)] public float CounterPulseDuration = 0.5f;

        public MidiCcBinding Midi = new();

        public float AccumulatorInitialValue;
        public AccumulatorResetMode AccumulatorReset;
        [Min(1)] public int AccumulatorResetAfterEvents = 4;
        public AccumulatorLimitMode AccumulatorLimit;
        public float AccumulatorMin;
        public float AccumulatorMax = 1f;
        public AccumulatorTransitionMode AccumulatorTransition;
        [Min(0.001f)] public float AccumulatorTransitionDuration = 0.15f;
        [Range(1f, 8f)] public float AccumulatorTransitionSharpness = 3f;

        [NonSerialized] private bool _accumulatorInitialized;
        [NonSerialized] private float _accumulatorValue;
        [NonSerialized] private long _lastBeatEventId;
        [NonSerialized] private long _lastBarEventId;
        [NonSerialized] private int _accumulatorEventCount;
        [NonSerialized] private float _accumulatorTransitionFrom;
        [NonSerialized] private float _accumulatorTransitionTo;
        [NonSerialized] private double _accumulatorTransitionStartTime;
        [NonSerialized] private bool _elapsedTimeResetInitialized;
        [NonSerialized] private double _elapsedTimeResetStart;
        [NonSerialized] private double _lastElapsedTime;
        [NonSerialized] private long _lastElapsedTimeBeatEventId;
        [NonSerialized] private long _lastElapsedTimeBarEventId;
        [NonSerialized] private int _elapsedTimeResetEventCount;
        [NonSerialized] private bool _elapsedTimeWasStopped;

        public bool IsAvailable(in ModulationContext context) =>
            Source != FloatModulationSource.MidiCc || context.AllowMidi;

        public float Evaluate(in ModulationContext context)
        {
            if (Source == FloatModulationSource.BeatAccumulator ||
                Source == FloatModulationSource.BarAccumulator)
            {
                return Offset + EvaluateAccumulator(context,
                    Source == FloatModulationSource.BarAccumulator);
            }

            float source = Source switch
            {
                FloatModulationSource.Lfo => EvaluateLfo(context.Time, context.AnimationPhaseOffset),
                FloatModulationSource.Beat => EvaluateBeatPulse(context.Beat, false, context.AnimationPhaseOffset),
                FloatModulationSource.Beat2And4 => EvaluateBeat2And4Pulse(
                    context.Beat, context.AnimationPhaseOffset),
                FloatModulationSource.Bar => EvaluateBeatPulse(context.Beat, true, context.AnimationPhaseOffset),
                FloatModulationSource.ElapsedTime => EvaluateElapsedTime(context),
                FloatModulationSource.Kick => context.Audio?.Kick ?? 0f,
                FloatModulationSource.SnareClap => context.Audio?.SnareClap ?? 0f,
                FloatModulationSource.InputVolume => context.Audio?.InputVolume ?? 0f,
                FloatModulationSource.MidiCc => Midi?.GetValue() ?? 0f,
                FloatModulationSource.Counter => EvaluateCounter(context.Counter ?? Counter.Active),
                FloatModulationSource.CounterPulse => EvaluateCounterPulse(
                    context.Counter ?? Counter.Active, context.Time),
                _ => 0f,
            };

            return Offset + source * Amount;
        }

        private float EvaluateElapsedTime(in ModulationContext context)
        {
            float time = EvaluateElapsedTimeReset(context) + context.AnimationPhaseOffset;
            time = ApplyElapsedTimeLimit(Mathf.Max(0f, time));
            return ElapsedTimeCurve switch
            {
                ElapsedTimeCurve.Power => Mathf.Pow(time, Mathf.Max(0.001f, ElapsedTimeCurveValue)),
                ElapsedTimeCurve.Logarithm => Mathf.Log(1f + time, Mathf.Max(1.001f, ElapsedTimeCurveValue)),
                _ => time,
            };
        }

        private float EvaluateElapsedTimeReset(in ModulationContext context)
        {
            double elapsedTime = Mathf.Max(0f, (float)context.ElapsedTime);
            if (ElapsedTimeReset == AccumulatorResetMode.Never)
                return (float)elapsedTime;

            IBeatManager beat = context.Beat;
            if (!_elapsedTimeResetInitialized || elapsedTime < _lastElapsedTime)
            {
                _elapsedTimeResetInitialized = true;
                _elapsedTimeResetStart = 0d;
                _elapsedTimeResetEventCount = 0;
                _elapsedTimeWasStopped = beat != null && !beat.IsRunning;
                SynchronizeElapsedTimeEventIds(beat);
            }

            if (ElapsedTimeReset == AccumulatorResetMode.OnStop)
            {
                bool stopped = beat != null && !beat.IsRunning;
                if (stopped && !_elapsedTimeWasStopped)
                    ResetElapsedTime(elapsedTime, beat);
                _elapsedTimeWasStopped = stopped;
            }
            else if (beat != null)
            {
                if (ElapsedTimeReset == AccumulatorResetMode.EveryBar &&
                    beat.BarEventId != _lastElapsedTimeBarEventId)
                {
                    ResetElapsedTime(elapsedTime, beat);
                }
                else if (ElapsedTimeReset == AccumulatorResetMode.AfterNEvents)
                {
                    long eventDelta = beat.BeatEventId - _lastElapsedTimeBeatEventId;
                    if (eventDelta < 0)
                    {
                        ResetElapsedTime(elapsedTime, beat);
                    }
                    else
                    {
                        _elapsedTimeResetEventCount += (int)Math.Min(eventDelta, int.MaxValue);
                        if (_elapsedTimeResetEventCount >= Mathf.Max(1, ElapsedTimeResetAfterEvents))
                            ResetElapsedTime(elapsedTime, beat);
                        else
                            SynchronizeElapsedTimeEventIds(beat);
                    }
                }
            }

            _lastElapsedTime = elapsedTime;
            return Mathf.Max(0f, (float)(elapsedTime - _elapsedTimeResetStart));
        }

        private void ResetElapsedTime(double elapsedTime, IBeatManager beat)
        {
            _elapsedTimeResetStart = elapsedTime;
            _elapsedTimeResetEventCount = 0;
            SynchronizeElapsedTimeEventIds(beat);
        }

        private void SynchronizeElapsedTimeEventIds(IBeatManager beat)
        {
            if (beat == null) return;
            _lastElapsedTimeBeatEventId = beat.BeatEventId;
            _lastElapsedTimeBarEventId = beat.BarEventId;
        }

        private float ApplyElapsedTimeLimit(float value)
        {
            float min = Mathf.Min(ElapsedTimeMin, ElapsedTimeMax);
            float max = Mathf.Max(ElapsedTimeMin, ElapsedTimeMax);
            float range = max - min;

            return ElapsedTimeLimit switch
            {
                AccumulatorLimitMode.Clamp => Mathf.Clamp(value, min, max),
                AccumulatorLimitMode.Wrap when range > Mathf.Epsilon => min + Mathf.Repeat(value - min, range),
                AccumulatorLimitMode.PingPong when range > Mathf.Epsilon => min + Mathf.PingPong(value - min, range),
                _ => value,
            };
        }

        public void ResetAccumulator()
        {
            _accumulatorInitialized = false;
        }

        private float EvaluateAccumulator(in ModulationContext context, bool useBar)
        {
            IBeatManager beat = context.Beat;
            if (!_accumulatorInitialized)
            {
                InitializeAccumulator(beat, context.Time);
                return ApplyAccumulatorLimit(_accumulatorValue);
            }

            if (beat == null) return ApplyAccumulatorLimit(EvaluateAccumulatorTransition(context.Time));

            if (AccumulatorReset == AccumulatorResetMode.OnStop && !beat.IsRunning)
            {
                if (_accumulatorValue == AccumulatorInitialValue && _accumulatorEventCount == 0)
                    return ApplyAccumulatorLimit(EvaluateAccumulatorTransition(context.Time));
                float resetTransitionFrom = EvaluateAccumulatorTransition(context.Time);
                _accumulatorValue = AccumulatorInitialValue;
                _accumulatorEventCount = 0;
                SynchronizeAccumulatorEventIds(beat);
                BeginAccumulatorTransition(context.Time, resetTransitionFrom);
                return ApplyAccumulatorLimit(EvaluateAccumulatorTransition(context.Time));
            }

            if (AccumulatorReset == AccumulatorResetMode.EveryBar &&
                beat.BarEventId != _lastBarEventId)
            {
                float resetTransitionFrom = EvaluateAccumulatorTransition(context.Time);
                _accumulatorValue = AccumulatorInitialValue;
                _accumulatorEventCount = 0;
                SynchronizeAccumulatorEventIds(beat);
                BeginAccumulatorTransition(context.Time, resetTransitionFrom);
                return ApplyAccumulatorLimit(EvaluateAccumulatorTransition(context.Time));
            }

            long currentEventId = useBar ? beat.BarEventId : beat.BeatEventId;
            long previousEventId = useBar ? _lastBarEventId : _lastBeatEventId;
            long eventDelta = currentEventId - previousEventId;

            if (eventDelta < 0)
            {
                InitializeAccumulator(beat, context.Time);
                return ApplyAccumulatorLimit(_accumulatorValue);
            }

            float transitionFrom = EvaluateAccumulatorTransition(context.Time);

            for (long i = 0; i < eventDelta; i++)
            {
                if (AccumulatorReset == AccumulatorResetMode.AfterNEvents &&
                    _accumulatorEventCount >= Mathf.Max(1, AccumulatorResetAfterEvents))
                {
                    _accumulatorValue = AccumulatorInitialValue;
                    _accumulatorEventCount = 0;
                }

                _accumulatorValue += Amount;
                _accumulatorEventCount++;
            }

            SynchronizeAccumulatorEventIds(beat);
            if (eventDelta > 0) BeginAccumulatorTransition(context.Time, transitionFrom);
            return ApplyAccumulatorLimit(EvaluateAccumulatorTransition(context.Time));
        }

        private void InitializeAccumulator(IBeatManager beat, double time)
        {
            _accumulatorInitialized = true;
            _accumulatorValue = AccumulatorInitialValue;
            _accumulatorEventCount = 0;
            _accumulatorTransitionFrom = _accumulatorValue;
            _accumulatorTransitionTo = ApplyAccumulatorLimit(_accumulatorValue);
            _accumulatorTransitionStartTime = time;
            SynchronizeAccumulatorEventIds(beat);
        }

        private void BeginAccumulatorTransition(double time, float from)
        {
            _accumulatorTransitionFrom = from;
            _accumulatorTransitionTo = ApplyAccumulatorLimit(_accumulatorValue);
            _accumulatorTransitionStartTime = time;
        }

        private float EvaluateAccumulatorTransition(double time)
        {
            if (AccumulatorTransition == AccumulatorTransitionMode.Instant) return ApplyAccumulatorLimit(_accumulatorValue);

            float duration = Mathf.Max(0.001f, AccumulatorTransitionDuration);
            float t = Mathf.Clamp01((float)((time - _accumulatorTransitionStartTime) / duration));
            if (AccumulatorTransition == AccumulatorTransitionMode.SmoothStep)
                t = t * t * (3f - 2f * t);
            else if (AccumulatorTransition == AccumulatorTransitionMode.EaseOut)
                t = 1f - Mathf.Pow(1f - t, Mathf.Clamp(AccumulatorTransitionSharpness, 1f, 8f));
            return Mathf.LerpUnclamped(_accumulatorTransitionFrom, _accumulatorTransitionTo, t);
        }

        private void SynchronizeAccumulatorEventIds(IBeatManager beat)
        {
            if (beat == null) return;
            _lastBeatEventId = beat.BeatEventId;
            _lastBarEventId = beat.BarEventId;
        }

        private float ApplyAccumulatorLimit(float value)
        {
            float min = Mathf.Min(AccumulatorMin, AccumulatorMax);
            float max = Mathf.Max(AccumulatorMin, AccumulatorMax);
            float range = max - min;

            return AccumulatorLimit switch
            {
                AccumulatorLimitMode.Clamp => Mathf.Clamp(value, min, max),
                AccumulatorLimitMode.Wrap when range > Mathf.Epsilon => min + Mathf.Repeat(value - min, range),
                AccumulatorLimitMode.PingPong when range > Mathf.Epsilon => min + Mathf.PingPong(value - min, range),
                _ => value,
            };
        }

        private float EvaluateBeatPulse(IBeatManager beat, bool useBar, float phaseOffset)
        {
            if (beat == null || !beat.IsRunning) return 0f;

            float rawPhase = (useBar ? beat.BarPhase : beat.BeatPhase) + phaseOffset;
            float phase = Mathf.Repeat(rawPhase, 1f);
            if (phase == 0f && rawPhase > 0f) phase = 1f;

            return Mathf.Pow(1f - Mathf.Clamp01(phase), Mathf.Max(0.01f, BeatPulseSharpness));
        }

        private float EvaluateBeat2And4Pulse(IBeatManager beat, float phaseOffset)
        {
            if (beat == null || !beat.IsRunning) return 0f;

            // BeatInBarは0始まりなので、1と3がそれぞれ2拍目・4拍目。
            if (beat.BeatInBar is not (1 or 3)) return 0f;
            return EvaluateBeatPulse(beat, false, phaseOffset);
        }

        private float EvaluateCounter(ICounter counter)
        {
            float value = counter?.AnimatedValue ?? 0f;
            float min = Mathf.Min(CounterMin, CounterMax);
            float max = Mathf.Max(CounterMin, CounterMax);
            float range = max - min;

            value = CounterValueMode switch
            {
                CounterValueMode.Clamp => Mathf.Clamp(value, min, max),
                CounterValueMode.PingPong when range > Mathf.Epsilon => min + Mathf.PingPong(value - min, range),
                CounterValueMode.RepeatModX => Mathf.Repeat(value, Mathf.Max(0.001f, CounterRepeatMod)),
                _ => value,
            };

            return Mathf.Pow(Mathf.Max(0f, value), Mathf.Max(0.01f, BeatPulseSharpness));
        }

        private float EvaluateCounterPulse(ICounter counter, double time)
        {
            if (counter == null || double.IsNegativeInfinity(counter.LastIncrementTime)) return 0f;

            float phase = Mathf.Max(0f, (float)(time - counter.LastIncrementTime)) /
                Mathf.Max(0.001f, CounterPulseDuration);
            return Mathf.Pow(1f - Mathf.Clamp01(phase), Mathf.Max(0.01f, BeatPulseSharpness));
        }

        private float EvaluateLfo(double time, float phaseOffset)
        {
            float cycle = Mathf.Repeat((float)(time * Mathf.Max(0f, LfoFrequency)) + LfoPhase + phaseOffset, 1f);
            float value = LfoWaveform switch
            {
                LfoWaveform.Sine => Mathf.Sin(cycle * Mathf.PI * 2f),
                LfoWaveform.Triangle => 1f - 4f * Mathf.Abs(cycle - 0.5f),
                LfoWaveform.Saw => cycle * 2f - 1f,
                LfoWaveform.Square => cycle < 0.5f ? 1f : -1f,
                _ => 0f,
            };

            return LfoUnipolar ? value * 0.5f + 0.5f : value;
        }
    }
}
