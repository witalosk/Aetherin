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
    }

    public enum FloatModulationOperation
    {
        Add,
        Multiply,
        Override,
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
        public readonly IAudioFeatureProvider Audio;
        public readonly IBeatManager Beat;
        public readonly bool AllowMidi;
        public readonly float AnimationPhaseOffset;

        public ModulationContext(
            double time,
            IAudioFeatureProvider audio,
            IBeatManager beat,
            bool allowMidi,
            float animationPhaseOffset = 0f)
        {
            Time = time;
            Audio = audio;
            Beat = beat;
            AllowMidi = allowMidi;
            AnimationPhaseOffset = animationPhaseOffset;
        }

        public ModulationContext WithAnimationPhaseOffset(float offset) =>
            new(Time, Audio, Beat, AllowMidi, AnimationPhaseOffset + offset);
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

        [Tooltip("Beat / Barの頭からの減衰の鋭さ。1で線形、大きいほど短いパルスになります")]
        [Min(0.01f)]
        public float BeatPulseSharpness = 3f;

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
                FloatModulationSource.Bar => EvaluateBeatPulse(context.Beat, true, context.AnimationPhaseOffset),
                FloatModulationSource.ElapsedTime => (float)context.Time + context.AnimationPhaseOffset,
                FloatModulationSource.Kick => context.Audio?.Kick ?? 0f,
                FloatModulationSource.SnareClap => context.Audio?.SnareClap ?? 0f,
                FloatModulationSource.MidiCc => Midi?.GetValue() ?? 0f,
                _ => 0f,
            };

            return Offset + source * Amount;
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

            float phase = Mathf.Repeat((useBar ? beat.BarPhase : beat.BeatPhase) + phaseOffset, 1f);
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

    [Serializable]
    public class FloatModulationStack
    {
        public List<FloatModulator> Modulators = new();

        public float Evaluate(float baseValue, in ModulationContext context)
        {
            float result = baseValue;
            foreach (var modulator in Modulators)
            {
                if (modulator == null || !modulator.Enabled || !modulator.IsAvailable(context)) continue;

                float value = modulator.Evaluate(context);
                result = modulator.Operation switch
                {
                    FloatModulationOperation.Add => result + value,
                    FloatModulationOperation.Multiply => result * value,
                    FloatModulationOperation.Override => value,
                    _ => result,
                };
            }

            return result;
        }
    }

    [Serializable]
    public abstract class AnimatedParameter<T>
    {
        public T BaseValue;

        public abstract T Evaluate(in ModulationContext context);

        protected AnimatedParameter()
        {
        }

        protected AnimatedParameter(T baseValue)
        {
            BaseValue = baseValue;
        }
    }

    [Serializable]
    public sealed class FloatParameter : AnimatedParameter<float>
    {
        public FloatModulationStack Modulation = new();

        public FloatParameter()
        {
        }

        public FloatParameter(float baseValue) : base(baseValue)
        {
        }

        public override float Evaluate(in ModulationContext context) =>
            Modulation?.Evaluate(BaseValue, context) ?? BaseValue;
    }

    [Serializable]
    public sealed class Vector2Parameter : AnimatedParameter<Vector2>
    {
        [Tooltip("X ModulationをYにも適用します。有効時はY Modulationを使用しません")]
        public bool ApplyXModulationToBothAxes;

        public FloatModulationStack XModulation = new();
        public FloatModulationStack YModulation = new();

        public Vector2Parameter()
        {
        }

        public Vector2Parameter(Vector2 baseValue) : base(baseValue)
        {
        }

        public override Vector2 Evaluate(in ModulationContext context)
        {
            FloatModulationStack yModulation = ApplyXModulationToBothAxes
                ? XModulation
                : YModulation;

            return new Vector2(
                XModulation?.Evaluate(BaseValue.x, context) ?? BaseValue.x,
                yModulation?.Evaluate(BaseValue.y, context) ?? BaseValue.y);
        }
    }

    [Serializable]
    public sealed class Vector3Parameter : AnimatedParameter<Vector3>
    {
        [Tooltip("X ModulationをY/Zにも適用します。有効時はY/Z Modulationを使用しません")]
        public bool ApplyXModulationToAllAxes;

        public FloatModulationStack XModulation = new();
        public FloatModulationStack YModulation = new();
        public FloatModulationStack ZModulation = new();

        public Vector3Parameter()
        {
        }

        public Vector3Parameter(Vector3 baseValue) : base(baseValue)
        {
        }

        public override Vector3 Evaluate(in ModulationContext context)
        {
            FloatModulationStack yModulation = ApplyXModulationToAllAxes ? XModulation : YModulation;
            FloatModulationStack zModulation = ApplyXModulationToAllAxes ? XModulation : ZModulation;

            return new Vector3(
                XModulation?.Evaluate(BaseValue.x, context) ?? BaseValue.x,
                yModulation?.Evaluate(BaseValue.y, context) ?? BaseValue.y,
                zModulation?.Evaluate(BaseValue.z, context) ?? BaseValue.z);
        }
    }

    [Serializable]
    public sealed class IntParameter : AnimatedParameter<int>
    {
        public FloatModulationStack Modulation = new();

        public IntParameter()
        {
        }

        public IntParameter(int baseValue) : base(baseValue)
        {
        }

        public override int Evaluate(in ModulationContext context) => Mathf.RoundToInt(
            Modulation?.Evaluate(BaseValue, context) ?? BaseValue);
    }
}
