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
        public Vector3Parameter Frequency = new(Vector3.one);
        public Vector4Parameter Speed = new(Vector4.one);
        public Vector3Parameter Offset = new();

        public void EnsureInitialized()
        {
            Amount ??= new FloatParameter(0.1f);
            Frequency ??= new Vector3Parameter(Vector3.one);
            Speed ??= new Vector4Parameter(Vector4.one);
            Offset ??= new Vector3Parameter();
        }
    }
}
