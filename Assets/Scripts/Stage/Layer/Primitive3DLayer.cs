using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnitySimpleContainer;

namespace Aetherin
{
    public enum Primitive3DType
    {
        Cube,
        Sphere,
        Tetrahedron,
        Cylinder,
    }

    public enum Primitive3DColorMode
    {
        Solid,
        UvLerp,
        ShadedLerp,
        ToonTwoTone,
    }

    [Serializable]
    public class Primitive3DLayerParams : StageLayerParams
    {
        public Primitive3DType Primitive;
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public Vector3Parameter Size = new(Vector3.one);

        [Min(3)]
        public int RadialSegments = 32;

        [Min(2)]
        public int LatitudeSegments = 16;

        public Primitive3DColorMode ColorMode;
        public PaletteColorSource ColorA = PaletteColorSource.AccentColor1;
        public PaletteColorSource ColorB = PaletteColorSource.AccentColor2;
        public FloatParameter ColorIntensity = new(1f);
        public FloatParameter Alpha = new(1f);

        [Tooltip("UVのU座標へ掛ける値")]
        public FloatParameter UvScale = new(1f);
        public FloatParameter UvOffset = new(0f);

        [Tooltip("Shadingで使う、面から光へ向かう方向")]
        public Vector3Parameter LightDirection = new(new Vector3(0.3f, 0.8f, -0.5f));

        [Range(0f, 1f)]
        public FloatParameter ToonThreshold = new(0.5f);

        public RepeaterParams Repeater = new();
    }

    /// <summary>
    /// CameraStageへ直接描画するプリミティブ立体レイヤー。
    /// TransformとSizeはShader行列で処理し、形状または分割数が変わったときだけMeshを再生成する。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class Primitive3DLayer : StageLayer
    {
        private const int MaxRepeaterCopies = 128;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        private static readonly int ColorModeId = Shader.PropertyToID("_ColorMode");
        private static readonly int UvParamsId = Shader.PropertyToID("_UvParams");
        private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
        private static readonly int ToonThresholdId = Shader.PropertyToID("_ToonThreshold");
        private static readonly int ShapeMatrixId = Shader.PropertyToID("_ShapeMatrix");
        private static readonly int ShapeNormalMatrixId = Shader.PropertyToID("_ShapeNormalMatrix");

        [SerializeField] private Primitive3DLayerParams _params = new();
        [SerializeField] private Shader _surfaceShader;

        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<Color> _vertexColors = new();
        private readonly List<int> _triangles = new();

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _material;
        private Bounds _geometryBounds;
        private int _geometryHash;

        private Vector3 _evaluatedPosition;
        private Vector3 _evaluatedRotation;
        private Vector3 _evaluatedScale = Vector3.one;
        private Vector3 _evaluatedAnchor;
        private Vector3 _evaluatedSize = Vector3.one;
        private float _evaluatedOpacity = 1f;
        private float _evaluatedColorIntensity = 1f;
        private float _evaluatedAlpha = 1f;
        private float _evaluatedUvScale = 1f;
        private float _evaluatedUvOffset;
        private Vector3 _evaluatedLightDirection = Vector3.up;
        private float _evaluatedToonThreshold = 0.5f;
        private EvaluatedRepeater _evaluatedRepeater;
        private Color _evaluatedColorA = Color.white;
        private Color _evaluatedColorB = Color.white;

        private IAudioFeatureProvider _audioFeatureProvider;
        private IBeatManager _beatManager;
        private IDeckStateProvider _deckStateProvider;
        private StageBase _stage;

        public override IParams Params => _params;
        protected override StageLayerParams LayerParams => _params;
        protected override Renderer LayerRenderer => MeshRendererComponent;

        private MeshRenderer MeshRendererComponent
        {
            get
            {
                if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
                return _meshRenderer;
            }
        }

        [Inject]
        private void Construct(
            IAudioFeatureProvider audioFeatureProvider,
            IBeatManager beatManager,
            IDeckStateProvider deckStateProvider)
        {
            _audioFeatureProvider = audioFeatureProvider;
            _beatManager = beatManager;
            _deckStateProvider = deckStateProvider;
        }

        /// <summary>実行時にCameraStageから追加されたLayerへ依存を設定する。</summary>
        public void Initialize(
            IAudioFeatureProvider audioFeatureProvider,
            IBeatManager beatManager,
            IDeckStateProvider deckStateProvider)
        {
            _audioFeatureProvider = audioFeatureProvider;
            _beatManager = beatManager;
            _deckStateProvider = deckStateProvider;
            Initialize();
        }

        private void Awake() => Initialize();
        private void OnEnable() => Initialize();

        private void Initialize()
        {
            _stage = GetComponentInParent<StageBase>();
            EnsureResources();
            EvaluateParameters(Application.isPlaying);
            RebuildGeometry();
            ApplyAppearanceAndTransform();
            ApplyLayerState();
        }

        private void Update()
        {
            EnsureResources();
            EvaluateParameters(Application.isPlaying);

            int hash = CalculateGeometryHash();
            if (hash != _geometryHash) RebuildGeometry();

            ApplyAppearanceAndTransform();
        }

        protected override void OnValidate()
        {
            EnsureParameterObjects();
            _params.RadialSegments = Mathf.Max(3, _params.RadialSegments);
            _params.LatitudeSegments = Mathf.Max(2, _params.LatitudeSegments);
            _params.Size.BaseValue.x = Mathf.Max(0f, _params.Size.BaseValue.x);
            _params.Size.BaseValue.y = Mathf.Max(0f, _params.Size.BaseValue.y);
            _params.Size.BaseValue.z = Mathf.Max(0f, _params.Size.BaseValue.z);

            base.OnValidate();
            if (!isActiveAndEnabled) return;

            _stage = GetComponentInParent<StageBase>();
            EnsureResources();
            EvaluateParameters(false);
            RebuildGeometry();
            ApplyAppearanceAndTransform();
        }

        private void EnsureParameterObjects()
        {
            _params ??= new Primitive3DLayerParams();
            _params.Opacity ??= new FloatParameter(1f);
            _params.Position ??= new Vector3Parameter();
            _params.Rotation ??= new Vector3Parameter();
            _params.Scale ??= new Vector3Parameter(Vector3.one);
            _params.Anchor ??= new Vector3Parameter();
            _params.Size ??= new Vector3Parameter(Vector3.one);
            _params.ColorIntensity ??= new FloatParameter(1f);
            _params.Alpha ??= new FloatParameter(1f);
            _params.UvScale ??= new FloatParameter(1f);
            _params.UvOffset ??= new FloatParameter(0f);
            _params.LightDirection ??= new Vector3Parameter(new Vector3(0.3f, 0.8f, -0.5f));
            _params.ToonThreshold ??= new FloatParameter(0.5f);
            _params.Repeater ??= new RepeaterParams();
            _params.Repeater.EnsureInitialized(MaxRepeaterCopies);
        }

        private void EnsureResources()
        {
            EnsureParameterObjects();
            if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
            if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();

            if (_mesh == null)
            {
                _mesh = new Mesh
                {
                    name = $"{name} Primitive 3D Mesh",
                    hideFlags = HideFlags.DontSave,
                    indexFormat = IndexFormat.UInt32,
                };
                _mesh.MarkDynamic();
                _meshFilter.sharedMesh = _mesh;
            }

            if (_material != null) return;

            Shader shader = _surfaceShader != null
                ? _surfaceShader
                : Shader.Find("Aetherin/Primitive 3D Unlit");
            if (shader == null) return;

            _material = new Material(shader)
            {
                name = $"{name} Primitive 3D Material",
                hideFlags = HideFlags.DontSave,
            };
            _meshRenderer.sharedMaterial = _material;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
        }

        private void RebuildGeometry()
        {
            if (_mesh == null) return;

            _vertices.Clear();
            _uvs.Clear();
            _vertexColors.Clear();
            _triangles.Clear();

            switch (_params.Primitive)
            {
                case Primitive3DType.Cube:
                    BuildCube();
                    break;
                case Primitive3DType.Sphere:
                    BuildSphere();
                    break;
                case Primitive3DType.Tetrahedron:
                    BuildTetrahedron();
                    break;
                case Primitive3DType.Cylinder:
                    BuildCylinder();
                    break;
            }

            int baseIndexCount = _triangles.Count;
            int baseVertexCount = RepeaterMeshUtility.ApplyVertices(
                _vertices, _vertexColors, _uvs, _evaluatedRepeater);
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

        private void BuildSphere()
        {
            int radial = Mathf.Max(3, _params.RadialSegments);
            int latitude = Mathf.Max(2, _params.LatitudeSegments);
            EnsureCapacity(_vertices, (radial + 1) * (latitude + 1));
            EnsureCapacity(_triangles, radial * latitude * 6);

            for (int y = 0; y <= latitude; y++)
            {
                float theta = Mathf.PI * y / latitude;
                float ringRadius = Mathf.Sin(theta) * 0.5f;
                float py = Mathf.Cos(theta) * 0.5f;
                for (int x = 0; x <= radial; x++)
                {
                    float phi = Mathf.PI * 2f * x / radial;
                    _vertices.Add(new Vector3(
                        Mathf.Cos(phi) * ringRadius,
                        py,
                        Mathf.Sin(phi) * ringRadius));
                    _uvs.Add(new Vector2(x / (float)radial, 1f - y / (float)latitude));
                }
            }

            int stride = radial + 1;
            for (int y = 0; y < latitude; y++)
            {
                for (int x = 0; x < radial; x++)
                {
                    int a = y * stride + x;
                    int b = a + stride;
                    AddIndexedTriangleOutward(a, a + 1, b);
                    AddIndexedTriangleOutward(a + 1, b + 1, b);
                }
            }
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

        private void EvaluateParameters(bool runtime)
        {
            var context = new ModulationContext(
                runtime ? Time.timeAsDouble : 0d,
                runtime ? _audioFeatureProvider : null,
                runtime ? _beatManager : null,
                runtime && (_stage == null || _stage.Deck == StageDeck.Next));

            _evaluatedPosition = _params.Position?.Evaluate(context) ?? Vector3.zero;
            _evaluatedRotation = _params.Rotation?.Evaluate(context) ?? Vector3.zero;
            _evaluatedScale = _params.Scale?.Evaluate(context) ?? Vector3.one;
            _evaluatedAnchor = _params.Anchor?.Evaluate(context) ?? Vector3.zero;
            _evaluatedSize = _params.Size?.Evaluate(context) ?? Vector3.one;
            _evaluatedSize.x = Mathf.Max(0f, _evaluatedSize.x);
            _evaluatedSize.y = Mathf.Max(0f, _evaluatedSize.y);
            _evaluatedSize.z = Mathf.Max(0f, _evaluatedSize.z);
            _evaluatedOpacity = Mathf.Clamp01(_params.Opacity?.Evaluate(context) ?? 1f);
            _evaluatedColorIntensity = Mathf.Max(0f, _params.ColorIntensity?.Evaluate(context) ?? 1f);
            _evaluatedAlpha = Mathf.Clamp01(_params.Alpha?.Evaluate(context) ?? 1f);
            _evaluatedUvScale = _params.UvScale?.Evaluate(context) ?? 1f;
            _evaluatedUvOffset = _params.UvOffset?.Evaluate(context) ?? 0f;
            _evaluatedLightDirection = _params.LightDirection?.Evaluate(context) ?? Vector3.up;
            if (_evaluatedLightDirection.sqrMagnitude < 0.000001f) _evaluatedLightDirection = Vector3.up;
            _evaluatedLightDirection.Normalize();
            _evaluatedToonThreshold = Mathf.Clamp01(_params.ToonThreshold?.Evaluate(context) ?? 0.5f);
            _evaluatedRepeater = EvaluatedRepeater.Evaluate(_params.Repeater, context, MaxRepeaterCopies);

            ColorPalette palette = Application.isPlaying && _deckStateProvider != null
                ? _deckStateProvider.GetState(_stage != null ? _stage.Deck : StageDeck.Next).Palette
                : null;
            _evaluatedColorA = EvaluatePaletteColor(palette, _params.ColorA);
            _evaluatedColorB = EvaluatePaletteColor(palette, _params.ColorB);
        }

        private Color EvaluatePaletteColor(ColorPalette palette, PaletteColorSource source)
        {
            Color color = PaletteColorParameter.Resolve(palette, source).linear * _evaluatedColorIntensity;
            color.a = _evaluatedAlpha * _evaluatedOpacity;
            return color;
        }

        private void ApplyAppearanceAndTransform()
        {
            if (_material == null || _mesh == null) return;

            _material.SetColor(BaseColorId, _evaluatedColorA);
            _material.SetColor(ColorBId, _evaluatedColorB);
            _material.SetFloat(ColorModeId, (float)_params.ColorMode);
            _material.SetVector(UvParamsId, new Vector4(_evaluatedUvScale, _evaluatedUvOffset, 0f, 0f));
            _material.SetVector(LightDirectionId, _evaluatedLightDirection);
            _material.SetFloat(ToonThresholdId, _evaluatedToonThreshold);

            Vector3 rotation = new(
                Mathf.Repeat(_evaluatedRotation.x, 360f),
                Mathf.Repeat(_evaluatedRotation.y, 360f),
                Mathf.Repeat(_evaluatedRotation.z, 360f));
            Matrix4x4 matrix = Matrix4x4.TRS(
                                   _evaluatedPosition,
                                   Quaternion.Euler(rotation),
                                   _evaluatedScale) *
                               Matrix4x4.Translate(-_evaluatedAnchor) *
                               Matrix4x4.Scale(_evaluatedSize);

            _material.SetMatrix(ShapeMatrixId, matrix);
            _material.SetMatrix(ShapeNormalMatrixId, matrix.inverse.transpose);
            ApplyTransformedBounds(matrix);
        }

        private void ApplyTransformedBounds(Matrix4x4 matrix)
        {
            Vector3 center = _geometryBounds.center;
            Vector3 extents = _geometryBounds.extents;
            Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = center + Vector3.Scale(extents, new Vector3(x, y, z));
                point = matrix.MultiplyPoint3x4(point);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }

            _mesh.bounds = new Bounds((min + max) * 0.5f, max - min);
        }

        private int CalculateGeometryHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)_params.Primitive;
                if (_params.Primitive is Primitive3DType.Sphere or Primitive3DType.Cylinder)
                    hash = hash * 31 + _params.RadialSegments;
                if (_params.Primitive == Primitive3DType.Sphere)
                    hash = hash * 31 + _params.LatitudeSegments;
                hash = hash * 31 + _evaluatedRepeater.GetHashCode();
                return hash;
            }
        }

        private static void EnsureCapacity<T>(List<T> list, int capacity)
        {
            if (list.Capacity < capacity) list.Capacity = capacity;
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(_mesh);
            DestroyRuntimeObject(_material);
        }

        private static void DestroyRuntimeObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
