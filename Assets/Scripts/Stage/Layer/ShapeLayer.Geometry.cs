using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    public sealed partial class ShapeLayer
    {
        private void RebuildGeometry()
        {
            if (_mesh == null) return;

            int edgeCount = GetEdgeCount();
            _boundary.Clear();
            EnsureCapacity(_boundary, edgeCount);
            for (int i = 0; i < edgeCount; i++) _boundary.Add(GetBoundaryPoint(i, edgeCount));

            float trimSpan = Mathf.Clamp01(_evaluatedTrimEnd) - Mathf.Clamp01(_evaluatedTrimStart);
            bool strokeClosed = !_params.StrokeTrim.Enabled || Mathf.Abs(trimSpan) >= 0.999999f;
            BuildStrokePath(strokeClosed);

            int fillVertexCount = edgeCount + 1;
            int strokeSegmentCount = strokeClosed ? _strokePath.Count : Mathf.Max(0, _strokePath.Count - 1);

            _vertices.Clear();
            _fillTriangles.Clear();
            _strokeTriangles.Clear();
            EnsureCapacity(_vertices, fillVertexCount + _strokePath.Count * 2);
            EnsureCapacity(_fillTriangles, _params.FillEnabled ? edgeCount * 3 : 0);
            EnsureCapacity(_strokeTriangles,
                _params.StrokeEnabled && _evaluatedStrokeWidth > 0f ? strokeSegmentCount * 6 : 0);

            _vertices.Add(Vector3.zero);
            for (int i = 0; i < edgeCount; i++)
            {
                _vertices.Add(_boundary[i]);

                if (_params.FillEnabled)
                {
                    _fillTriangles.Add(0);
                    _fillTriangles.Add(i + 1);
                    _fillTriangles.Add((i + 1) % edgeCount + 1);
                }
            }

            BuildStroke(_strokePath, strokeClosed, fillVertexCount);
            ApplyRepeater();

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_vertexColors);
            _mesh.subMeshCount = 2;
            _mesh.SetTriangles(_fillTriangles, 0, false);
            _mesh.SetTriangles(_strokeTriangles, 1, false);
            _mesh.RecalculateBounds();
            _geometryBounds = _mesh.bounds;
            ApplyShapeTransform();
            _geometryHash = CalculateGeometryHash();
        }

        private void BuildStrokePath(bool strokeClosed)
        {
            _strokePath.Clear();
            if (strokeClosed)
            {
                EnsureCapacity(_strokePath, _boundary.Count);
                _strokePath.AddRange(_boundary);
                return;
            }

            float rawSpan = Mathf.Clamp01(_evaluatedTrimEnd) - Mathf.Clamp01(_evaluatedTrimStart);
            if (Mathf.Abs(rawSpan) < 0.000001f) return;

            float span = rawSpan > 0f ? rawSpan : rawSpan + 1f;
            int count = _boundary.Count;
            _cumulativeLengths.Clear();
            EnsureCapacity(_cumulativeLengths, count + 1);
            _cumulativeLengths.Add(0f);
            for (int i = 0; i < count; i++)
            {
                float next = _cumulativeLengths[i] +
                             Vector2.Distance(_boundary[i], _boundary[(i + 1) % count]);
                _cumulativeLengths.Add(next);
            }

            float perimeter = _cumulativeLengths[count];
            if (perimeter <= 0.000001f) return;

            float startNormalized = Mathf.Repeat(Mathf.Clamp01(_evaluatedTrimStart) + _evaluatedTrimOffset, 1f);
            float startDistance = startNormalized * perimeter;
            float endDistance = startDistance + span * perimeter;
            EnsureCapacity(_strokePath, count + 2);
            _strokePath.Add(SampleBoundary(startDistance));

            int firstLoop = Mathf.FloorToInt(startDistance / perimeter);
            int lastLoop = Mathf.CeilToInt(endDistance / perimeter);
            for (int loop = firstLoop; loop <= lastLoop; loop++)
            {
                float loopOffset = loop * perimeter;
                for (int i = 1; i <= count; i++)
                {
                    float vertexDistance = _cumulativeLengths[i] + loopOffset;
                    if (vertexDistance <= startDistance + 0.000001f || vertexDistance >= endDistance - 0.000001f)
                        continue;

                    _strokePath.Add(_boundary[i % count]);
                }
            }

            _strokePath.Add(SampleBoundary(endDistance));
        }

        private Vector2 SampleBoundary(float distance)
        {
            float perimeter = _cumulativeLengths[_cumulativeLengths.Count - 1];
            float wrapped = Mathf.Repeat(distance, perimeter);
            for (int i = 0; i < _boundary.Count; i++)
            {
                if (wrapped > _cumulativeLengths[i + 1]) continue;

                float segmentLength = _cumulativeLengths[i + 1] - _cumulativeLengths[i];
                float t = segmentLength <= 0.000001f
                    ? 0f
                    : (wrapped - _cumulativeLengths[i]) / segmentLength;
                return Vector2.LerpUnclamped(_boundary[i], _boundary[(i + 1) % _boundary.Count], t);
            }

            return _boundary[0];
        }

        private void BuildStroke(
            IReadOnlyList<Vector2> boundary,
            bool closed,
            int vertexOffset)
        {
            if (!_params.StrokeEnabled || _evaluatedStrokeWidth <= 0f) return;

            int count = boundary.Count;
            float halfWidth = _evaluatedStrokeWidth * 0.5f;
            for (int i = 0; i < count; i++)
            {
                Vector2 previous = boundary[closed ? (i - 1 + count) % count : Mathf.Max(0, i - 1)];
                Vector2 current = boundary[i];
                Vector2 next = boundary[closed ? (i + 1) % count : Mathf.Min(count - 1, i + 1)];
                Vector2 previousDirection = (current - previous).normalized;
                Vector2 nextDirection = (next - current).normalized;

                if (!closed && i == 0) previousDirection = nextDirection;
                if (!closed && i == count - 1) nextDirection = previousDirection;

                Vector2 previousNormal = new(previousDirection.y, -previousDirection.x);
                Vector2 nextNormal = new(nextDirection.y, -nextDirection.x);
                Vector2 miter = previousNormal + nextNormal;

                if (miter.sqrMagnitude < 0.000001f) miter = nextNormal;
                else miter.Normalize();

                float denominator = Mathf.Abs(Vector2.Dot(miter, nextNormal));
                float miterLength = halfWidth / Mathf.Max(denominator, 0.0001f);
                miterLength = Mathf.Min(miterLength, halfWidth * 4f);
                Vector2 offset = miter * miterLength;

                _vertices.Add(current + offset);
                _vertices.Add(current - offset);
            }

            int segmentCount = closed ? count : count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                int next = (i + 1) % count;
                int outer = vertexOffset + i * 2;
                int inner = outer + 1;
                int nextOuter = vertexOffset + next * 2;
                int nextInner = nextOuter + 1;
                _strokeTriangles.Add(outer);
                _strokeTriangles.Add(nextOuter);
                _strokeTriangles.Add(inner);
                _strokeTriangles.Add(inner);
                _strokeTriangles.Add(nextOuter);
                _strokeTriangles.Add(nextInner);
            }
        }

        /// <summary>
        /// 構築済みの1コピー分の頂点/三角形を、トランスフォームを累積適用しながら複製する
        /// コピーごとの不透明度は頂点カラーのアルファでシェーダへ渡す
        /// (Repeaterは_ShapeMatrixより前のメッシュ空間で適用されるため、レイヤーのTransformとは独立して累積する)
        /// </summary>
        private void ApplyRepeater()
        {
            int baseVertexCount = _vertices.Count;
            int baseFillCount = _fillTriangles.Count;
            int baseStrokeCount = _strokeTriangles.Count;
            RepeaterMeshUtility.ApplyVertices(_vertices, _vertexColors, null, _evaluatedRepeater,
                _evaluatedRepeater.TransformMode == RepeaterTransformMode.FromSource ? this : null);
            RepeaterMeshUtility.ApplyIndices(
                _fillTriangles, baseFillCount, baseVertexCount, _evaluatedRepeater.Copies);
            RepeaterMeshUtility.ApplyIndices(
                _strokeTriangles, baseStrokeCount, baseVertexCount, _evaluatedRepeater.Copies);
        }

        private static void EnsureCapacity<T>(List<T> list, int capacity)
        {
            if (list.Capacity < capacity) list.Capacity = capacity;
        }

        private int GetEdgeCount()
        {
            return _params.Shape switch
            {
                ShapePrimitive.Rectangle => 4,
                ShapePrimitive.Ellipse => Mathf.Max(3, _params.EllipseSegments),
                ShapePrimitive.Polygon => Mathf.Max(3, _evaluatedPoints),
                ShapePrimitive.Star => Mathf.Max(3, _evaluatedPoints) * 2,
                _ => 4,
            };
        }

        private Vector2 GetBoundaryPoint(int index, int edgeCount)
        {
            Vector2 halfSize = _evaluatedSize * 0.5f;
            if (_params.Shape == ShapePrimitive.Rectangle)
            {
                return index switch
                {
                    0 => new Vector2(-halfSize.x, -halfSize.y),
                    1 => new Vector2(halfSize.x, -halfSize.y),
                    2 => new Vector2(halfSize.x, halfSize.y),
                    _ => new Vector2(-halfSize.x, halfSize.y),
                };
            }

            float angle = Mathf.PI * 2f * index / edgeCount + Mathf.PI * 0.5f;
            float radius = _params.Shape == ShapePrimitive.Star && (index & 1) == 1
                ? _evaluatedInnerRadius
                : 1f;

            return new Vector2(Mathf.Cos(angle) * halfSize.x, Mathf.Sin(angle) * halfSize.y) * radius;
        }

    }
}
