using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    public sealed partial class Primitive3DLayer
    {
        private void RebuildGeometry()
        {
            if (_mesh == null) return;

            _vertices.Clear();
            _uvs.Clear();
            _vertexColors.Clear();
            _triangles.Clear();
            _wireVertices.Clear();
            _wireUvs.Clear();
            _wireVertexColors.Clear();
            _wireTriangles.Clear();

            switch (_params.Primitive)
            {
                case Primitive3DType.Cube:
                    BuildCube();
                    break;
                case Primitive3DType.RoundedBox:
                    BuildRoundedBox();
                    break;
                case Primitive3DType.Icosphere:
                    BuildIcosphere();
                    break;
                case Primitive3DType.Tetrahedron:
                    BuildTetrahedron();
                    break;
                case Primitive3DType.Cylinder:
                    BuildCylinder();
                    break;
            }

            BuildWireframe();

            int baseIndexCount = _triangles.Count;
            int baseVertexCount = RepeaterMeshUtility.ApplyVertices(
                _vertices, _vertexColors, _uvs, _evaluatedRepeater,
                _evaluatedRepeater.TransformMode == RepeaterTransformMode.FromSource ? this : null);
            RepeaterMeshUtility.ApplyIndices(
                _triangles, baseIndexCount, baseVertexCount, _evaluatedRepeater.Copies);

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_vertexColors);
            _mesh.SetTriangles(_triangles, 0, false);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            _geometryBounds = _mesh.bounds;
            RebuildWireMesh();
            _geometryHash = CalculateGeometryHash();
        }

        private void BuildCube()
        {
            Vector3[] corners =
            {
                new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f),
                new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f),
                new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f),
                new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f),
            };

            AddQuad(corners[0], corners[3], corners[2], corners[1]);
            AddQuad(corners[4], corners[5], corners[6], corners[7]);
            AddQuad(corners[0], corners[4], corners[7], corners[3]);
            AddQuad(corners[1], corners[2], corners[6], corners[5]);
            AddQuad(corners[0], corners[1], corners[5], corners[4]);
            AddQuad(corners[3], corners[7], corners[6], corners[2]);
        }

        private void BuildRoundedBox()
        {
            int segments = Mathf.Max(1, _params.CornerSegments);
            int stride = segments + 1;
            var indices = new System.Collections.Generic.Dictionary<int, int>(stride * stride * 6);

            int GetVertex(int x, int y, int z)
            {
                int key = (x * stride + y) * stride + z;
                if (indices.TryGetValue(key, out int index)) return index;

                Vector3 normalized = new(x / (float)segments - 0.5f,
                    y / (float)segments - 0.5f, z / (float)segments - 0.5f);
                Vector3 point = RoundedBoxPoint(normalized);
                index = _vertices.Count;
                indices.Add(key, index);
                _vertices.Add(point);
                _uvs.Add(new Vector2(x / (float)segments, y / (float)segments));
                return index;
            }

            void AddFace(int axis, int side)
            {
                for (int v = 0; v < segments; v++)
                for (int u = 0; u < segments; u++)
                {
                    int fixedCoordinate = side == 0 ? 0 : segments;
                    int a, b, c, d;
                    if (axis == 0)
                    {
                        a = GetVertex(fixedCoordinate, u, v); b = GetVertex(fixedCoordinate, u + 1, v);
                        c = GetVertex(fixedCoordinate, u + 1, v + 1); d = GetVertex(fixedCoordinate, u, v + 1);
                    }
                    else if (axis == 1)
                    {
                        a = GetVertex(u, fixedCoordinate, v); b = GetVertex(u + 1, fixedCoordinate, v);
                        c = GetVertex(u + 1, fixedCoordinate, v + 1); d = GetVertex(u, fixedCoordinate, v + 1);
                    }
                    else
                    {
                        a = GetVertex(u, v, fixedCoordinate); b = GetVertex(u + 1, v, fixedCoordinate);
                        c = GetVertex(u + 1, v + 1, fixedCoordinate); d = GetVertex(u, v + 1, fixedCoordinate);
                    }
                    AddIndexedTriangleOutward(a, b, c);
                    AddIndexedTriangleOutward(a, c, d);
                }
            }

            for (int axis = 0; axis < 3; axis++)
            {
                AddFace(axis, 0);
                AddFace(axis, 1);
            }
        }

        private Vector3 RoundedBoxPoint(Vector3 normalized)
        {
            Vector3 half = _evaluatedSize * 0.5f;
            Vector3 point = Vector3.Scale(normalized * 2f, half);
            Vector3 inner = new(
                Mathf.Max(0f, half.x - _evaluatedCornerRadius),
                Mathf.Max(0f, half.y - _evaluatedCornerRadius),
                Mathf.Max(0f, half.z - _evaluatedCornerRadius));
            Vector3 nearest = new(
                Mathf.Clamp(point.x, -inner.x, inner.x),
                Mathf.Clamp(point.y, -inner.y, inner.y),
                Mathf.Clamp(point.z, -inner.z, inner.z));
            Vector3 offset = point - nearest;
            return offset.sqrMagnitude > 0.000001f
                ? nearest + offset.normalized * _evaluatedCornerRadius
                : point;
        }

        private void BuildTetrahedron()
        {
            float s = 0.5f;
            Vector3 a = new(s, s, s);
            Vector3 b = new(-s, -s, s);
            Vector3 c = new(-s, s, -s);
            Vector3 d = new(s, -s, -s);
            AddTriangleOutward(a, b, c);
            AddTriangleOutward(a, d, b);
            AddTriangleOutward(a, c, d);
            AddTriangleOutward(b, d, c);
        }

        private void BuildIcosphere()
        {
            BuildIcosphereTopology(
                Mathf.Clamp(_params.IcosphereSubdivisions, 0, 5),
                out List<Vector3> vertices,
                out List<int> triangles);
            EnsureCapacity(_vertices, vertices.Count);
            EnsureCapacity(_uvs, vertices.Count);
            EnsureCapacity(_triangles, triangles.Count);

            foreach (Vector3 vertex in vertices)
            {
                _vertices.Add(vertex);
                Vector3 direction = vertex.normalized;
                _uvs.Add(new Vector2(
                    Mathf.Atan2(direction.z, direction.x) / (Mathf.PI * 2f) + 0.5f,
                    Mathf.Asin(direction.y) / Mathf.PI + 0.5f));
            }
            _triangles.AddRange(triangles);
        }

        private static void BuildIcosphereTopology(
            int subdivisions,
            out List<Vector3> vertices,
            out List<int> triangles)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var generatedVertices = new List<Vector3>
            {
                new(-1f, t, 0f), new(1f, t, 0f), new(-1f, -t, 0f), new(1f, -t, 0f),
                new(0f, -1f, t), new(0f, 1f, t), new(0f, -1f, -t), new(0f, 1f, -t),
                new(t, 0f, -1f), new(t, 0f, 1f), new(-t, 0f, -1f), new(-t, 0f, 1f),
            };
            for (int i = 0; i < generatedVertices.Count; i++)
                generatedVertices[i] = generatedVertices[i].normalized * 0.5f;

            var generatedTriangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            for (int subdivision = 0; subdivision < subdivisions; subdivision++)
            {
                var midpointCache = new Dictionary<ulong, int>();
                var subdivided = new List<int>(generatedTriangles.Count * 4);

                int Midpoint(int a, int b)
                {
                    uint min = (uint)Mathf.Min(a, b);
                    uint max = (uint)Mathf.Max(a, b);
                    ulong key = ((ulong)min << 32) | max;
                    if (midpointCache.TryGetValue(key, out int cached)) return cached;

                    int index = generatedVertices.Count;
                    generatedVertices.Add(
                        ((generatedVertices[a] + generatedVertices[b]) * 0.5f).normalized * 0.5f);
                    midpointCache.Add(key, index);
                    return index;
                }

                for (int i = 0; i < generatedTriangles.Count; i += 3)
                {
                    int a = generatedTriangles[i];
                    int b = generatedTriangles[i + 1];
                    int c = generatedTriangles[i + 2];
                    int ab = Midpoint(a, b);
                    int bc = Midpoint(b, c);
                    int ca = Midpoint(c, a);
                    subdivided.AddRange(new[]
                    {
                        a, ab, ca,
                        b, bc, ab,
                        c, ca, bc,
                        ab, bc, ca,
                    });
                }

                generatedTriangles = subdivided;
            }

            vertices = generatedVertices;
            triangles = generatedTriangles;
        }

        private void BuildCylinder()
        {
            int radial = Mathf.Max(3, _params.RadialSegments);
            EnsureCapacity(_vertices, radial * 2 + 2);
            EnsureCapacity(_triangles, radial * 12);

            for (int i = 0; i < radial; i++)
            {
                float angle = Mathf.PI * 2f * i / radial;
                float x = Mathf.Cos(angle) * 0.5f;
                float z = Mathf.Sin(angle) * 0.5f;
                _vertices.Add(new Vector3(x, -0.5f, z));
                _vertices.Add(new Vector3(x, 0.5f, z));
                float u = i / (float)radial;
                _uvs.Add(new Vector2(u, 0f));
                _uvs.Add(new Vector2(u, 1f));
            }

            int bottomCenter = _vertices.Count;
            _vertices.Add(new Vector3(0f, -0.5f, 0f));
            _uvs.Add(new Vector2(0.5f, 0.5f));
            int topCenter = _vertices.Count;
            _vertices.Add(new Vector3(0f, 0.5f, 0f));
            _uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i < radial; i++)
            {
                int next = (i + 1) % radial;
                int bottom = i * 2;
                int top = bottom + 1;
                int nextBottom = next * 2;
                int nextTop = nextBottom + 1;

                AddIndexedTriangleOutward(bottom, top, nextBottom);
                AddIndexedTriangleOutward(nextBottom, top, nextTop);
                AddIndexedTriangleOutward(bottomCenter, bottom, nextBottom);
                AddIndexedTriangleOutward(topCenter, nextTop, top);
            }
        }

        private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            AddTriangleOutward(a, b, c, Vector2.zero, Vector2.up, Vector2.one);
            AddTriangleOutward(a, c, d, Vector2.zero, Vector2.one, Vector2.right);
        }

        private void AddTriangleOutward(Vector3 a, Vector3 b, Vector3 c)
        {
            AddTriangleOutward(a, b, c, Vector2.zero, Vector2.right, new Vector2(0.5f, 1f));
        }

        private void AddTriangleOutward(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector2 uvA,
            Vector2 uvB,
            Vector2 uvC)
        {
            int start = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _uvs.Add(uvA);
            _uvs.Add(uvB);
            _uvs.Add(uvC);

            Vector3 normal = Vector3.Cross(b - a, c - a);
            Vector3 center = (a + b + c) / 3f;
            if (Vector3.Dot(normal, center) >= 0f)
            {
                _triangles.Add(start);
                _triangles.Add(start + 1);
                _triangles.Add(start + 2);
            }
            else
            {
                _triangles.Add(start);
                _triangles.Add(start + 2);
                _triangles.Add(start + 1);
            }
        }

        private void AddIndexedTriangleOutward(int a, int b, int c)
        {
            Vector3 normal = Vector3.Cross(_vertices[b] - _vertices[a], _vertices[c] - _vertices[a]);
            Vector3 center = (_vertices[a] + _vertices[b] + _vertices[c]) / 3f;
            if (Vector3.Dot(normal, center) >= 0f)
            {
                _triangles.Add(a);
                _triangles.Add(b);
                _triangles.Add(c);
            }
            else
            {
                _triangles.Add(a);
                _triangles.Add(c);
                _triangles.Add(b);
            }
        }

    }
}
