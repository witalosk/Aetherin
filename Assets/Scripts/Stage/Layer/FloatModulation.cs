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

    public readonly struct ModulationContext
    {
        public readonly double Time;
        public readonly IAudioFeatureProvider Audio;
        public readonly IBeatManager Beat;
        public readonly bool AllowMidi;

        public ModulationContext(
            double time,
            IAudioFeatureProvider audio,
            IBeatManager beat,
            bool allowMidi)
        {
            Time = time;
            Audio = audio;
            Beat = beat;
            AllowMidi = allowMidi;
        }
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

        public bool IsAvailable(in ModulationContext context) =>
            Source != FloatModulationSource.MidiCc || context.AllowMidi;

        public float Evaluate(in ModulationContext context)
        {
            float source = Source switch
            {
                FloatModulationSource.Lfo => EvaluateLfo(context.Time),
                FloatModulationSource.Beat => EvaluateBeatPulse(context.Beat, false),
                FloatModulationSource.Bar => EvaluateBeatPulse(context.Beat, true),
                FloatModulationSource.ElapsedTime => (float)context.Time,
                FloatModulationSource.Kick => context.Audio?.Kick ?? 0f,
                FloatModulationSource.SnareClap => context.Audio?.SnareClap ?? 0f,
                FloatModulationSource.MidiCc => Midi?.GetValue() ?? 0f,
                _ => 0f,
            };

            return Offset + source * Amount;
        }

        private float EvaluateBeatPulse(IBeatManager beat, bool useBar)
        {
            if (beat == null || !beat.IsRunning) return 0f;

            float phase = useBar ? beat.BarPhase : beat.BeatPhase;
            return Mathf.Pow(1f - Mathf.Clamp01(phase), Mathf.Max(0.01f, BeatPulseSharpness));
        }

        private float EvaluateLfo(double time)
        {
            float cycle = Mathf.Repeat((float)(time * Mathf.Max(0f, LfoFrequency)) + LfoPhase, 1f);
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
