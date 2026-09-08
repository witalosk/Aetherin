using System;
using UnityEngine;

namespace Aetherin
{
    public enum VertexNoiseType { Block, Simplex, Curl }
    public enum VertexNoiseDisplacementDirection { Normal, WorldX, WorldY, WorldZ }

    [Serializable]
    public sealed class VertexNoiseParams
    {
        public bool Enabled;
        public VertexNoiseType Type = VertexNoiseType.Simplex;
        public VertexNoiseDisplacementDirection Direction;
        [Min(0f)] public FloatParameter Amount = new(0.1f);
        [Min(0.001f)] public FloatParameter Frequency = new(1f);
        public FloatParameter Speed = new(1f);
        public Vector3Parameter Offset = new();

        public void EnsureInitialized()
        {
            Amount ??= new FloatParameter(0.1f);
            Frequency ??= new FloatParameter(1f);
            Speed ??= new FloatParameter(1f);
            Offset ??= new Vector3Parameter();
        }
    }
}
