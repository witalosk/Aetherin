using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    public enum RepeaterTransformMode
    {
        Cumulative,
        FromSource,
    }

    public enum RepeaterLayoutMode
    {
        Linear,
        GridXY,
        GridXZ,
        GridXYZ,
    }

    public interface IRepeaterCopyProvider
    {
        Matrix4x4 GetRepeaterCopyTransform(int copyIndex, float phaseOffset);
        float GetRepeaterCopyOpacity(int copyIndex, float phaseOffset);
    }

    [Serializable]
    public class RepeaterParams
    {
        public bool Enabled;
        public IntParameter Copies = new(3);
        public RepeaterLayoutMode LayoutMode;

        [Tooltip("グリッドの1行あたりの個数")]
        public IntParameter Columns = new(4);

        [Tooltip("3Dグリッドの1層あたりの行数。XY / XZでは使用しません")]
        public IntParameter Rows = new(4);

        [Tooltip("Linearではコピーごとの移動量、GridではXYZ各軸のセル間隔")]
        public Vector3Parameter Position = new(new Vector3(1f, 0f, 0f));
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public RepeaterTransformMode TransformMode;

        [Tooltip("Linear時、有効ならAE RepeaterのようにPositionも回転しながら累積します")]
        public bool RotationAffectsPosition = true;

        [Tooltip("コピーごとにLFO / Beat / Barなどの位相をずらす量。1で1周期です")]
        public FloatParameter AnimationPhaseOffset = new(0f);

        [Tooltip("最初のコピーの不透明度。最後のコピーに向かってEndOpacityへ補間されます")]
        public FloatParameter StartOpacity = new(1f);
        public FloatParameter EndOpacity = new(1f);

        public void EnsureInitialized(int maxCopies)
        {
            Copies ??= new IntParameter(3);
            Columns ??= new IntParameter(4);
            Rows ??= new IntParameter(4);
            Position ??= new Vector3Parameter(new Vector3(1f, 0f, 0f));
            Rotation ??= new Vector3Parameter();
            Scale ??= new Vector3Parameter(Vector3.one);
            Anchor ??= new Vector3Parameter();
            AnimationPhaseOffset ??= new FloatParameter(0f);
            StartOpacity ??= new FloatParameter(1f);
            EndOpacity ??= new FloatParameter(1f);
            Copies.BaseValue = Mathf.Clamp(Copies.BaseValue, 1, maxCopies);
        }
    }

    public readonly struct EvaluatedRepeater
    {
        public readonly int Copies;
        public readonly RepeaterLayoutMode LayoutMode;
        public readonly int Columns;
        public readonly int Rows;
        public readonly Vector3 Position;
        public readonly Vector3 Rotation;
        public readonly Vector3 Scale;
        public readonly Vector3 Anchor;
        public readonly float StartOpacity;
        public readonly float EndOpacity;
        public readonly bool RotationAffectsPosition;
        public readonly float AnimationPhaseOffset;
        public readonly RepeaterTransformMode TransformMode;

        private readonly RepeaterParams _parameters;
        private readonly ModulationContext _context;

        public EvaluatedRepeater(
            int copies,
            RepeaterLayoutMode layoutMode,
            int columns,
            int rows,
            Vector3 position,
            Vector3 rotation,
            Vector3 scale,
            Vector3 anchor,
            float startOpacity,
            float endOpacity,
            bool rotationAffectsPosition = true,
            float animationPhaseOffset = 0f,
            RepeaterParams parameters = null,
            ModulationContext context = default)
        {
            Copies = copies;
            LayoutMode = layoutMode;
            Columns = columns;
            Rows = rows;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Anchor = anchor;
            StartOpacity = startOpacity;
            EndOpacity = endOpacity;
            RotationAffectsPosition = rotationAffectsPosition;
            AnimationPhaseOffset = animationPhaseOffset;
            TransformMode = parameters?.TransformMode ?? RepeaterTransformMode.Cumulative;
            _parameters = parameters;
            _context = context;
        }

        public static EvaluatedRepeater Evaluate(
            RepeaterParams parameters,
            in ModulationContext context,
            int maxCopies)
        {
            if (parameters is not { Enabled: true })
                return new EvaluatedRepeater(1, RepeaterLayoutMode.Linear, 1, 1,
                    Vector3.zero, Vector3.zero, Vector3.one, Vector3.zero, 1f, 1f);

            return new EvaluatedRepeater(
                Mathf.Clamp(parameters.Copies?.Evaluate(context) ?? 1, 1, maxCopies),
                parameters.LayoutMode,
                Mathf.Max(1, parameters.Columns?.Evaluate(context) ?? 1),
                Mathf.Max(1, parameters.Rows?.Evaluate(context) ?? 1),
                parameters.Position?.Evaluate(context) ?? Vector3.zero,
                parameters.Rotation?.Evaluate(context) ?? Vector3.zero,
                parameters.Scale?.Evaluate(context) ?? Vector3.one,
                parameters.Anchor?.Evaluate(context) ?? Vector3.zero,
                parameters.StartOpacity?.Evaluate(context) ?? 1f,
                parameters.EndOpacity?.Evaluate(context) ?? 1f,
                parameters.RotationAffectsPosition,
                parameters.AnimationPhaseOffset?.Evaluate(context) ?? 0f,
                parameters,
                context);
        }

        public float GetOpacity(int index)
        {
            float startOpacity = StartOpacity;
            float endOpacity = EndOpacity;
            if (_parameters != null && index > 0 && AnimationPhaseOffset != 0f)
            {
                ModulationContext context = GetCopyContext(index);
                startOpacity = _parameters.StartOpacity?.Evaluate(context) ?? 1f;
                endOpacity = _parameters.EndOpacity?.Evaluate(context) ?? 1f;
            }

            if (Copies <= 1) return Mathf.Clamp01(startOpacity);
            float t = index / (float)(Copies - 1);
            return Mathf.Clamp01(Mathf.LerpUnclamped(startOpacity, endOpacity, t));
        }

        public void GetTransform(int index, out Vector3 position, out Vector3 rotation,
            out Vector3 scale, out Vector3 anchor)
        {
            if (_parameters == null || index <= 0 || AnimationPhaseOffset == 0f)
            {
                position = Position;
                rotation = Rotation;
                scale = Scale;
                anchor = Anchor;
                return;
            }

            ModulationContext context = GetCopyContext(index);
            position = _parameters.Position?.Evaluate(context) ?? Vector3.zero;
            rotation = _parameters.Rotation?.Evaluate(context) ?? Vector3.zero;
            scale = _parameters.Scale?.Evaluate(context) ?? Vector3.one;
            anchor = _parameters.Anchor?.Evaluate(context) ?? Vector3.zero;
        }

        private ModulationContext GetCopyContext(int index) =>
            _context.WithAnimationPhaseOffset(AnimationPhaseOffset * index);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Copies;
                hash = hash * 31 + (int)LayoutMode;
                hash = hash * 31 + Columns;
                hash = hash * 31 + Rows;
                hash = hash * 31 + Position.GetHashCode();
                hash = hash * 31 + Rotation.GetHashCode();
                hash = hash * 31 + Scale.GetHashCode();
                hash = hash * 31 + Anchor.GetHashCode();
                hash = hash * 31 + StartOpacity.GetHashCode();
                hash = hash * 31 + EndOpacity.GetHashCode();
                hash = hash * 31 + RotationAffectsPosition.GetHashCode();
                hash = hash * 31 + AnimationPhaseOffset.GetHashCode();
                hash = hash * 31 + (int)TransformMode;
                for (int i = 1; i < Copies && AnimationPhaseOffset != 0f; i++)
                {
                    GetTransform(i, out Vector3 position, out Vector3 rotation,
                        out Vector3 scale, out Vector3 anchor);
                    hash = hash * 31 + position.GetHashCode();
                    hash = hash * 31 + rotation.GetHashCode();
                    hash = hash * 31 + scale.GetHashCode();
                    hash = hash * 31 + anchor.GetHashCode();
                    hash = hash * 31 + GetOpacity(i).GetHashCode();
                }
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
            in EvaluatedRepeater repeater,
            IRepeaterCopyProvider copyProvider = null)
        {
            int baseVertexCount = vertices.Count;
            EnsureCapacity(vertices, baseVertexCount * repeater.Copies);
            EnsureCapacity(colors, baseVertexCount * repeater.Copies);
            if (uvs != null) EnsureCapacity(uvs, baseVertexCount * repeater.Copies);

            colors.Clear();
            Color color = Color.white;
            color.a = repeater.GetOpacity(0) *
                      (copyProvider?.GetRepeaterCopyOpacity(0, 0f) ?? 1f);
            color.r = 0f;
            for (int i = 0; i < baseVertexCount; i++) colors.Add(color);

            if (repeater.Copies <= 1) return baseVertexCount;

            Matrix4x4 accumulated = Matrix4x4.identity;

            for (int copy = 1; copy < repeater.Copies; copy++)
            {
                repeater.GetTransform(copy, out Vector3 position, out Vector3 rotation,
                    out Vector3 scale, out Vector3 anchor);
                if (repeater.LayoutMode != RepeaterLayoutMode.Linear)
                {
                    Vector3 gridIndex = GetGridIndex(copy, repeater.LayoutMode,
                        repeater.Columns, repeater.Rows);
                    Vector3 gridPosition = Vector3.Scale(position, gridIndex);
                    float index = copy;
                    accumulated = Matrix4x4.Translate(gridPosition) *
                                  Matrix4x4.Translate(anchor) *
                                  Matrix4x4.TRS(Vector3.zero,
                                      Quaternion.Euler(rotation * index),
                                      new Vector3(
                                          Mathf.Pow(scale.x, index),
                                          Mathf.Pow(scale.y, index),
                                          Mathf.Pow(scale.z, index))) *
                                  Matrix4x4.Translate(-anchor);
                }
                else if (repeater.TransformMode == RepeaterTransformMode.Cumulative &&
                    repeater.RotationAffectsPosition)
                {
                    Matrix4x4 step = Matrix4x4.Translate(anchor) *
                                     Matrix4x4.TRS(position, Quaternion.Euler(rotation), scale) *
                                     Matrix4x4.Translate(-anchor);
                    accumulated = step * accumulated;
                }
                else
                {
                    float index = copy;
                    accumulated = Matrix4x4.Translate(position * index) *
                                  Matrix4x4.Translate(anchor) *
                                  Matrix4x4.TRS(Vector3.zero,
                                      Quaternion.Euler(rotation * index),
                                      new Vector3(
                                          Mathf.Pow(scale.x, index),
                                          Mathf.Pow(scale.y, index),
                                          Mathf.Pow(scale.z, index))) *
                                  Matrix4x4.Translate(-anchor);
                }
                if (repeater.TransformMode == RepeaterTransformMode.FromSource && copyProvider != null)
                {
                    accumulated = copyProvider.GetRepeaterCopyTransform(
                                      copy, repeater.AnimationPhaseOffset * copy) * accumulated;
                }

                color.a = repeater.GetOpacity(copy) *
                          (copyProvider?.GetRepeaterCopyOpacity(
                              copy, repeater.AnimationPhaseOffset * copy) ?? 1f);
                color.r = copy;
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

        private static Vector3 GetGridIndex(
            int copy,
            RepeaterLayoutMode layout,
            int columns,
            int rows)
        {
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            int column = copy % columns;
            int line = copy / columns;

            return layout switch
            {
                RepeaterLayoutMode.GridXY => new Vector3(column, line, 0f),
                RepeaterLayoutMode.GridXZ => new Vector3(column, 0f, line),
                RepeaterLayoutMode.GridXYZ => new Vector3(column, line % rows, line / rows),
                _ => new Vector3(copy, 0f, 0f),
            };
        }
    }
}
