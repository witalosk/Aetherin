using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
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
