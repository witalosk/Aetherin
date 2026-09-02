using UnityEngine;

namespace Aetherin
{
    public sealed partial class Primitive3DLayer
    {
        private void RebuildWireMesh()
        {
            if (_wireMesh == null) return;

            int baseIndexCount = _wireTriangles.Count;
            int baseVertexCount = RepeaterMeshUtility.ApplyVertices(
                _wireVertices, _wireVertexColors, _wireUvs, _evaluatedRepeater,
                _evaluatedRepeater.TransformMode == RepeaterTransformMode.FromSource ? this : null);
            RepeaterMeshUtility.ApplyIndices(
                _wireTriangles, baseIndexCount, baseVertexCount, _evaluatedRepeater.Copies);

            _wireMesh.Clear();
            _wireMesh.SetVertices(_wireVertices);
            _wireMesh.SetUVs(0, _wireUvs);
            _wireMesh.SetColors(_wireVertexColors);
            _wireMesh.SetTriangles(_wireTriangles, 0, false);
            _wireMesh.RecalculateNormals();
            _wireMesh.RecalculateBounds();
            _wireGeometryBounds = _wireMesh.bounds;
        }

        private void BuildWireframe()
        {
            float radius = Mathf.Max(0.0001f, _evaluatedWireWidth * 0.5f);
            switch (_params.Primitive)
            {
                case Primitive3DType.Cube:
                    BuildCubeWireframe(radius);
                    break;
                case Primitive3DType.RoundedBox:
                    BuildRoundedBoxWireframe(radius);
                    break;
                case Primitive3DType.Sphere:
                    BuildSphereWireframe(radius);
                    break;
                case Primitive3DType.Tetrahedron:
                    BuildTetrahedronWireframe(radius);
                    break;
                case Primitive3DType.Cylinder:
                    BuildCylinderWireframe(radius);
                    break;
            }
        }

        private void BuildCubeWireframe(float radius)
        {
            Vector3[] p =
            {
                new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f),
                new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f),
                new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f),
                new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f),
            };
            int[,] edges =
            {
                { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
                { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
                { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
            };
            for (int i = 0; i < edges.GetLength(0); i++) AddWireEdge(p[edges[i, 0]], p[edges[i, 1]], radius);
        }

        private void BuildRoundedBoxWireframe(float radius)
        {
            int segments = Mathf.Max(1, _params.CornerSegments);
            for (int axis = 0; axis < 3; axis++)
            for (int side = 0; side < 2; side++)
            for (int v = 0; v <= segments; v++)
            for (int u = 0; u < segments; u++)
            {
                float fixedCoordinate = side - 0.5f;
                float a = u / (float)segments - 0.5f;
                float b = (u + 1f) / segments - 0.5f;
                float row = v / (float)segments - 0.5f;
                Vector3 p0 = axis switch
                {
                    0 => new Vector3(fixedCoordinate, a, row),
                    1 => new Vector3(a, fixedCoordinate, row),
                    _ => new Vector3(a, row, fixedCoordinate),
                };
                Vector3 p1 = axis switch
                {
                    0 => new Vector3(fixedCoordinate, b, row),
                    1 => new Vector3(b, fixedCoordinate, row),
                    _ => new Vector3(b, row, fixedCoordinate),
                };
                AddWireEdge(RoundedBoxPoint(p0), RoundedBoxPoint(p1), radius);

                p0 = axis switch
                {
                    0 => new Vector3(fixedCoordinate, row, a),
                    1 => new Vector3(row, fixedCoordinate, a),
                    _ => new Vector3(row, a, fixedCoordinate),
                };
                p1 = axis switch
                {
                    0 => new Vector3(fixedCoordinate, row, b),
                    1 => new Vector3(row, fixedCoordinate, b),
                    _ => new Vector3(row, b, fixedCoordinate),
                };
                AddWireEdge(RoundedBoxPoint(p0), RoundedBoxPoint(p1), radius);
            }
        }

        private void BuildTetrahedronWireframe(float radius)
        {
            float s = 0.5f;
            Vector3[] p = { new(s, s, s), new(-s, -s, s), new(-s, s, -s), new(s, -s, -s) };
            AddWireEdge(p[0], p[1], radius); AddWireEdge(p[0], p[2], radius);
            AddWireEdge(p[0], p[3], radius); AddWireEdge(p[1], p[2], radius);
            AddWireEdge(p[1], p[3], radius); AddWireEdge(p[2], p[3], radius);
        }

        private void BuildCylinderWireframe(float radius)
        {
            int radial = Mathf.Max(3, _params.RadialSegments);
            for (int i = 0; i < radial; i++)
            {
                float a0 = Mathf.PI * 2f * i / radial;
                float a1 = Mathf.PI * 2f * ((i + 1) % radial) / radial;
                Vector3 bottom0 = new(Mathf.Cos(a0) * 0.5f, -0.5f, Mathf.Sin(a0) * 0.5f);
                Vector3 bottom1 = new(Mathf.Cos(a1) * 0.5f, -0.5f, Mathf.Sin(a1) * 0.5f);
                Vector3 top0 = new(bottom0.x, 0.5f, bottom0.z);
                Vector3 top1 = new(bottom1.x, 0.5f, bottom1.z);
                AddWireEdge(bottom0, bottom1, radius);
                AddWireEdge(top0, top1, radius);
                AddWireEdge(bottom0, top0, radius);
            }
        }

        private void BuildSphereWireframe(float radius)
        {
            int radial = Mathf.Max(3, _params.RadialSegments);
            int latitude = Mathf.Max(2, _params.LatitudeSegments);

            // 緯線。極では半径が0になるので除外する。
            for (int y = 1; y < latitude; y++)
            {
                float theta = Mathf.PI * y / latitude;
                float ringRadius = Mathf.Sin(theta) * 0.5f;
                float py = Mathf.Cos(theta) * 0.5f;
                for (int x = 0; x < radial; x++)
                {
                    float a0 = Mathf.PI * 2f * x / radial;
                    float a1 = Mathf.PI * 2f * (x + 1) / radial;
                    AddWireEdge(
                        new Vector3(Mathf.Cos(a0) * ringRadius, py, Mathf.Sin(a0) * ringRadius),
                        new Vector3(Mathf.Cos(a1) * ringRadius, py, Mathf.Sin(a1) * ringRadius), radius);
                }
            }

            // 経線。三角形分割の対角線は含めない。
            for (int x = 0; x < radial; x++)
            {
                float phi = Mathf.PI * 2f * x / radial;
                for (int y = 0; y < latitude; y++)
                {
                    float t0 = Mathf.PI * y / latitude;
                    float t1 = Mathf.PI * (y + 1) / latitude;
                    AddWireEdge(SpherePoint(phi, t0), SpherePoint(phi, t1), radius);
                }
            }
        }

        private static Vector3 SpherePoint(float phi, float theta) => new(
            Mathf.Cos(phi) * Mathf.Sin(theta) * 0.5f,
            Mathf.Cos(theta) * 0.5f,
            Mathf.Sin(phi) * Mathf.Sin(theta) * 0.5f);

        private void AddWireEdge(Vector3 a, Vector3 b, float radius)
        {
            Vector3 direction = b - a;
            if (direction.sqrMagnitude < 0.0000001f) return;
            direction.Normalize();
            Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) < 0.9f
                ? Vector3.up
                : Vector3.right;
            Vector3 side = Vector3.Cross(direction, reference).normalized * radius;
            Vector3 up = Vector3.Cross(direction, side).normalized * radius;
            Vector3[] ring = { side + up, -side + up, -side - up, side - up };

            int start = _wireVertices.Count;
            for (int i = 0; i < 4; i++)
            {
                _wireVertices.Add(a + ring[i]);
                _wireVertices.Add(b + ring[i]);
                _wireUvs.Add(Vector2.zero);
                _wireUvs.Add(Vector2.one);
            }
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) & 3;
                int a0 = start + i * 2;
                int b0 = a0 + 1;
                int a1 = start + next * 2;
                int b1 = a1 + 1;
                _wireTriangles.Add(a0); _wireTriangles.Add(b0); _wireTriangles.Add(a1);
                _wireTriangles.Add(a1); _wireTriangles.Add(b0); _wireTriangles.Add(b1);
            }
        }

    }
}
