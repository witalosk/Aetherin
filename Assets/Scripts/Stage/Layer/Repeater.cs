using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public class RepeaterParams
    {
        public bool Enabled;
        public IntParameter Copies = new(3);

        [Tooltip("コピーごとに累積適用されるトランスフォーム")]
        public Vector3Parameter Position = new(new Vector3(1f, 0f, 0f));
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();

        [Tooltip("最初のコピーの不透明度。最後のコピーに向かってEndOpacityへ補間されます")]
        public FloatParameter StartOpacity = new(1f);
        public FloatParameter EndOpacity = new(1f);

        public void EnsureInitialized(int maxCopies)
        {
            Copies ??= new IntParameter(3);
            Position ??= new Vector3Parameter(new Vector3(1f, 0f, 0f));
            Rotation ??= new Vector3Parameter();
            Scale ??= new Vector3Parameter(Vector3.one);
            Anchor ??= new Vector3Parameter();
            StartOpacity ??= new FloatParameter(1f);
            EndOpacity ??= new FloatParameter(1f);
            Copies.BaseValue = Mathf.Clamp(Copies.BaseValue, 1, maxCopies);
        }
    }

    public readonly struct EvaluatedRepeater
    {
        public readonly int Copies;
        public readonly Vector3 Position;
        public readonly Vector3 Rotation;
        public readonly Vector3 Scale;
        public readonly Vector3 Anchor;
        public readonly float StartOpacity;
        public readonly float EndOpacity;

        public EvaluatedRepeater(
            int copies,
            Vector3 position,
            Vector3 rotation,
            Vector3 scale,
            Vector3 anchor,
            float startOpacity,
            float endOpacity)
        {
            Copies = copies;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Anchor = anchor;
            StartOpacity = startOpacity;
            EndOpacity = endOpacity;
        }

        public static EvaluatedRepeater Evaluate(
            RepeaterParams parameters,
            in ModulationContext context,
            int maxCopies)
        {
            if (parameters is not { Enabled: true })
                return new EvaluatedRepeater(1, Vector3.zero, Vector3.zero, Vector3.one, Vector3.zero, 1f, 1f);

            return new EvaluatedRepeater(
                Mathf.Clamp(parameters.Copies?.Evaluate(context) ?? 1, 1, maxCopies),
                parameters.Position?.Evaluate(context) ?? Vector3.zero,
                parameters.Rotation?.Evaluate(context) ?? Vector3.zero,
                parameters.Scale?.Evaluate(context) ?? Vector3.one,
                parameters.Anchor?.Evaluate(context) ?? Vector3.zero,
                parameters.StartOpacity?.Evaluate(context) ?? 1f,
                parameters.EndOpacity?.Evaluate(context) ?? 1f);
        }

        public float GetOpacity(int index)
        {
            if (Copies <= 1) return Mathf.Clamp01(StartOpacity);
            float t = index / (float)(Copies - 1);
            return Mathf.Clamp01(Mathf.LerpUnclamped(StartOpacity, EndOpacity, t));
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Copies;
                hash = hash * 31 + Position.GetHashCode();
                hash = hash * 31 + Rotation.GetHashCode();
                hash = hash * 31 + Scale.GetHashCode();
                hash = hash * 31 + Anchor.GetHashCode();
                hash = hash * 31 + StartOpacity.GetHashCode();
                hash = hash * 31 + EndOpacity.GetHashCode();
                return hash;
            }
        }
    }

    public static class RepeaterMeshUtility
    {
        /// <summary>頂点、任意のUV、コピー別Opacity用の頂点色を複製する。</summary>
        public static int ApplyVertices(
            List<Vector3> vertices,
            List<Color> colors,
            List<Vector2> uvs,
            in EvaluatedRepeater repeater)
        {
            int baseVertexCount = vertices.Count;
            EnsureCapacity(vertices, baseVertexCount * repeater.Copies);
            EnsureCapacity(colors, baseVertexCount * repeater.Copies);
            if (uvs != null) EnsureCapacity(uvs, baseVertexCount * repeater.Copies);

            colors.Clear();
            Color color = Color.white;
            color.a = repeater.GetOpacity(0);
            for (int i = 0; i < baseVertexCount; i++) colors.Add(color);

            if (repeater.Copies <= 1) return baseVertexCount;

            Matrix4x4 step = Matrix4x4.Translate(repeater.Anchor) *
                             Matrix4x4.TRS(repeater.Position, Quaternion.Euler(repeater.Rotation), repeater.Scale) *
                             Matrix4x4.Translate(-repeater.Anchor);
            Matrix4x4 accumulated = Matrix4x4.identity;

            for (int copy = 1; copy < repeater.Copies; copy++)
            {
                accumulated = step * accumulated;
                color.a = repeater.GetOpacity(copy);
                for (int vertex = 0; vertex < baseVertexCount; vertex++)
                {
                    vertices.Add(accumulated.MultiplyPoint3x4(vertices[vertex]));
                    colors.Add(color);
                    if (uvs != null) uvs.Add(uvs[vertex]);
                }
            }

            return baseVertexCount;
        }

        public static void ApplyIndices(
            List<int> indices,
            int baseIndexCount,
            int baseVertexCount,
            int copies)
        {
            EnsureCapacity(indices, baseIndexCount * copies);
            for (int copy = 1; copy < copies; copy++)
            {
                int offset = baseVertexCount * copy;
                for (int i = 0; i < baseIndexCount; i++) indices.Add(indices[i] + offset);
            }
        }

        private static void EnsureCapacity<T>(List<T> list, int capacity)
        {
            if (list.Capacity < capacity) list.Capacity = capacity;
        }
    }
}
