using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnitySimpleContainer;

namespace Aetherin
{
    public enum ShapePrimitive
    {
        Rectangle,
        Ellipse,
        Polygon,
        Star,
    }

    [Serializable]
    public class StrokeTrimParams
    {
        public bool Enabled;
        public FloatParameter Start = new(0f);
        public FloatParameter End = new(1f);

        [Tooltip("周長に対するオフセット。1で一周します")]
        public FloatParameter Offset = new(0f);
    }

    [Serializable]
    public class ShapeLayerParams : StageLayerParams
    {
        public ShapePrimitive Shape = ShapePrimitive.Rectangle;
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public Vector2Parameter Size = new(new Vector2(2f, 2f));

        public IntParameter Points = new(5);

        public FloatParameter InnerRadius = new(0.5f);

        [Min(3)]
        public int EllipseSegments = 64;

        public PaletteColorParameter FillColor = new();

        public bool FillEnabled = true;
        public bool StrokeEnabled;

        [Min(0f)]
        public FloatParameter StrokeWidth = new(0.05f);

        public PaletteColorParameter StrokeColor = new() { Color = PaletteColorSource.AccentColor2 };
        public StrokeTrimParams StrokeTrim = new();

        public RepeaterParams Repeater = new();
    }

    /// <summary>
    /// パラメトリックな基本図形を Mesh に変換し、CameraStage のカメラへ直接描画するレイヤー。
    /// Mesh は実行時キャッシュで、保存対象は ShapeLayerParams のみ。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ShapeLayer : StageLayer, IRepeaterCopyProvider
    {
        private const int MaxRepeaterCopies = 128;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        private static readonly int GradientParamsId = Shader.PropertyToID("_GradientParams");
        private static readonly int UseGradientId = Shader.PropertyToID("_UseGradient");
        private static readonly int UsePaletteRandomId = Shader.PropertyToID("_UsePaletteRandom");
        private static readonly int PaletteRandomSeedId = Shader.PropertyToID("_PaletteRandomSeed");
        private static readonly int[] PaletteColorIds = CreatePaletteColorIds();
        private static readonly int ShapeMatrixId = Shader.PropertyToID("_ShapeMatrix");

        [SerializeField] private ShapeLayerParams _params = new();
        [SerializeField] private Shader _fillShader;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _fillMaterial;
        private Material _strokeMaterial;
        private int _geometryHash;
        private Vector3 _evaluatedRotation;
        private float _evaluatedStrokeWidth;
        private float _evaluatedOpacity;
        private Vector3 _evaluatedPosition;
        private Vector3 _evaluatedScale;
        private Vector3 _evaluatedAnchor;
        private Vector2 _evaluatedSize;
        private int _evaluatedPoints;
        private float _evaluatedInnerRadius;
        private float _evaluatedTrimStart;
        private float _evaluatedTrimEnd = 1f;
        private float _evaluatedTrimOffset;
        private EvaluatedRepeater _evaluatedRepeater;
        private EvaluatedPaletteColor _evaluatedFillColor;
        private EvaluatedPaletteColor _evaluatedStrokeColor;
        private ModulationContext _modulationContext;
        private IAudioFeatureProvider _audioFeatureProvider;
        private IBeatManager _beatManager;
        private IDeckStateProvider _deckStateProvider;
        private StageBase _stage;
        private Bounds _geometryBounds;

        // Geometry更新時に使い回す。要素数が増えた場合だけList内部の容量が拡張される。
        private readonly List<Vector2> _boundary = new();
        private readonly List<Vector2> _strokePath = new();
        private readonly List<float> _cumulativeLengths = new();
        private readonly List<Vector3> _vertices = new();
        private readonly List<Color> _vertexColors = new();
        private readonly List<int> _fillTriangles = new();
        private readonly List<int> _strokeTriangles = new();

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
            Initialize(audioFeatureProvider, beatManager, deckStateProvider);
        }

        /// <summary>実行時に追加されたレイヤーへ依存関係を設定する。</summary>
        public void Initialize(
            IAudioFeatureProvider audioFeatureProvider,
            IBeatManager beatManager,
            IDeckStateProvider deckStateProvider)
        {
            _audioFeatureProvider = audioFeatureProvider;
            _beatManager = beatManager;
            _deckStateProvider = deckStateProvider;
            _stage = GetComponentInParent<StageBase>();
        }

        private void Awake()
        {
            _stage = GetComponentInParent<StageBase>();
            EnsureResources();
            EvaluateParameters(Application.isPlaying);
            RebuildGeometry();
            ApplyAppearance();
            ApplyLayerState();
        }

        private void OnEnable()
        {
            _stage = GetComponentInParent<StageBase>();
            EnsureResources();
            EvaluateParameters(Application.isPlaying);
            RebuildGeometry();
            ApplyAppearance();
            ApplyLayerState();
        }

        private void Update()
        {
            EnsureResources();
            EvaluateParameters(Application.isPlaying);

            int hash = CalculateGeometryHash();
            if (hash != _geometryHash) RebuildGeometry();

            ApplyAppearance();
            ApplyShapeTransform();
        }

        protected override void OnValidate()
        {
            _params.Opacity ??= new FloatParameter(1f);
            _params.Position ??= new Vector3Parameter();
            _params.Rotation ??= new Vector3Parameter();
            _params.Scale ??= new Vector3Parameter(Vector3.one);
            _params.Anchor ??= new Vector3Parameter();
            _params.Size ??= new Vector2Parameter(new Vector2(2f, 2f));
            _params.Points ??= new IntParameter(5);
            _params.InnerRadius ??= new FloatParameter(0.5f);
            _params.StrokeWidth ??= new FloatParameter(0.05f);
            _params.StrokeTrim ??= new StrokeTrimParams();
            _params.StrokeTrim.Start ??= new FloatParameter(0f);
            _params.StrokeTrim.End ??= new FloatParameter(1f);
            _params.StrokeTrim.Offset ??= new FloatParameter(0f);
            _params.FillColor ??= new PaletteColorParameter();
            _params.FillColor.EnsureInitialized();
            _params.StrokeColor ??= new PaletteColorParameter { Color = PaletteColorSource.AccentColor2 };
            _params.StrokeColor.EnsureInitialized();
            _params.Repeater ??= new RepeaterParams();
            _params.Repeater.EnsureInitialized(MaxRepeaterCopies);
            _params.Points.BaseValue = Mathf.Max(3, _params.Points.BaseValue);
            _params.EllipseSegments = Mathf.Max(3, _params.EllipseSegments);
            _params.Size.BaseValue.x = Mathf.Max(0f, _params.Size.BaseValue.x);
            _params.Size.BaseValue.y = Mathf.Max(0f, _params.Size.BaseValue.y);
            _params.InnerRadius.BaseValue = Mathf.Clamp01(_params.InnerRadius.BaseValue);
            _params.StrokeWidth.BaseValue = Mathf.Max(0f, _params.StrokeWidth.BaseValue);

            base.OnValidate();
            if (!isActiveAndEnabled) return;

            EnsureResources();
            EvaluateParameters(false);
            RebuildGeometry();
            ApplyAppearance();
            ApplyShapeTransform();
        }

        private void EnsureResources()
        {
            if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
            if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();

            if (_mesh == null)
            {
                _mesh = new Mesh
                {
                    name = $"{name} Shape Mesh",
                    hideFlags = HideFlags.DontSave,
                    // Repeaterで頂点数が65535を超えることがある
                    indexFormat = IndexFormat.UInt32,
                };
                _mesh.MarkDynamic();
                _meshFilter.sharedMesh = _mesh;
            }

            if (_fillMaterial != null && _strokeMaterial != null) return;

            var shader = _fillShader != null ? _fillShader : Shader.Find("Aetherin/Shape Fill");
            if (shader == null) return;

            _fillMaterial = CreateMaterial(shader, "Fill");
            _strokeMaterial = CreateMaterial(shader, "Stroke");
            _meshRenderer.sharedMaterials = new[] { _fillMaterial, _strokeMaterial };
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
        }

        private Material CreateMaterial(Shader shader, string role)
        {
            return new Material(shader)
            {
                name = $"{name} Shape {role} Material",
                hideFlags = HideFlags.DontSave,
            };
        }

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

        private void ApplyShapeTransform()
        {
            if (_fillMaterial == null || _strokeMaterial == null || _mesh == null) return;

            // ElapsedTimeなどで回転値が増え続けても、長時間実行時にQuaternion変換の精度を失わないよう正規化する。
            Vector3 rotation = new(
                Mathf.Repeat(_evaluatedRotation.x, 360f),
                Mathf.Repeat(_evaluatedRotation.y, 360f),
                Mathf.Repeat(_evaluatedRotation.z, 360f));
            Matrix4x4 shapeMatrix = Matrix4x4.TRS(
                                            _evaluatedPosition,
                                            Quaternion.Euler(rotation),
                                            _evaluatedScale) *
                                        Matrix4x4.Translate(-_evaluatedAnchor);

            _fillMaterial.SetMatrix(ShapeMatrixId, shapeMatrix);
            _strokeMaterial.SetMatrix(ShapeMatrixId, shapeMatrix);
            ApplyTransformedBounds(shapeMatrix);
        }

        private void ApplyTransformedBounds(Matrix4x4 matrix)
        {
            Vector3 center = _geometryBounds.center;
            Vector3 extents = _geometryBounds.extents;
            Vector3 first = matrix.MultiplyPoint3x4(center + new Vector3(-extents.x, -extents.y, 0f));
            Vector3 min = first;
            Vector3 max = first;

            Encapsulate(matrix.MultiplyPoint3x4(center + new Vector3(extents.x, -extents.y, 0f)), ref min, ref max);
            Encapsulate(matrix.MultiplyPoint3x4(center + new Vector3(-extents.x, extents.y, 0f)), ref min, ref max);
            Encapsulate(matrix.MultiplyPoint3x4(center + new Vector3(extents.x, extents.y, 0f)), ref min, ref max);
            _mesh.bounds = new Bounds((min + max) * 0.5f, max - min);
        }

        private static void Encapsulate(Vector3 point, ref Vector3 min, ref Vector3 max)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point); 
        }

        private void ApplyAppearance()
        {
            if (_fillMaterial == null || _strokeMaterial == null) return;

            ApplyColor(_fillMaterial, _evaluatedFillColor);
            ApplyColor(_strokeMaterial, _evaluatedStrokeColor);
        }

        private void ApplyColor(Material material, in EvaluatedPaletteColor evaluated)
        {
            Color colorA = evaluated.ColorA;
            if (_evaluatedRepeater.TransformMode != RepeaterTransformMode.FromSource)
                colorA.a *= _evaluatedOpacity;
            material.SetColor(BaseColorId, colorA);
            material.SetFloat(UseGradientId, evaluated.IsGradient ? 1f : 0f);
            material.SetFloat(UsePaletteRandomId, evaluated.IsPaletteRandom ? 1f : 0f);

            if (evaluated.IsPaletteRandom)
            {
                material.SetFloat(PaletteRandomSeedId, evaluated.RandomSeed);
                for (int i = 0; i < PaletteColorIds.Length; i++)
                    material.SetColor(PaletteColorIds[i], evaluated.PaletteColors[i]);
                return;
            }

            if (!evaluated.IsGradient) return;

            Color colorB = evaluated.ColorB;
            if (_evaluatedRepeater.TransformMode != RepeaterTransformMode.FromSource)
                colorB.a *= _evaluatedOpacity;
            material.SetColor(ColorBId, colorB);

            float radians = evaluated.AngleDegrees * Mathf.Deg2Rad;
            material.SetVector(GradientParamsId,
                new Vector4(Mathf.Cos(radians), Mathf.Sin(radians), evaluated.Offset, evaluated.Scale));
        }

        private static int[] CreatePaletteColorIds()
        {
            var ids = new int[6];
            for (int i = 0; i < ids.Length; i++) ids[i] = Shader.PropertyToID($"_PaletteColor{i}");
            return ids;
        }

        private void EvaluateParameters(bool useRuntimeSources = true)
        {
            var context = new ModulationContext(
                useRuntimeSources ? Time.timeAsDouble : 0d,
                useRuntimeSources ? _audioFeatureProvider : null,
                useRuntimeSources ? _beatManager : null,
                useRuntimeSources && (_stage == null || _stage.Deck == StageDeck.Next));
            _modulationContext = context;

            _evaluatedRotation = _params.Rotation?.Evaluate(context) ?? Vector3.zero;
            _evaluatedPosition = _params.Position?.Evaluate(context) ?? Vector3.zero;
            _evaluatedScale = _params.Scale?.Evaluate(context) ?? Vector3.one;
            _evaluatedAnchor = _params.Anchor?.Evaluate(context) ?? Vector3.zero;
            _evaluatedSize = _params.Size?.Evaluate(context) ?? Vector2.zero;
            _evaluatedSize.x = Mathf.Max(0f, _evaluatedSize.x);
            _evaluatedSize.y = Mathf.Max(0f, _evaluatedSize.y);
            _evaluatedPoints = Mathf.Max(3, _params.Points?.Evaluate(context) ?? 3);
            _evaluatedInnerRadius = Mathf.Clamp01(_params.InnerRadius?.Evaluate(context) ?? 0.5f);
            _evaluatedTrimStart = _params.StrokeTrim?.Start?.Evaluate(context) ?? 0f;
            _evaluatedTrimEnd = _params.StrokeTrim?.End?.Evaluate(context) ?? 1f;
            _evaluatedTrimOffset = _params.StrokeTrim?.Offset?.Evaluate(context) ?? 0f;
            _evaluatedStrokeWidth = Mathf.Max(0f, _params.StrokeWidth?.Evaluate(context) ?? 0f);
            _evaluatedOpacity = Mathf.Clamp01(_params.Opacity?.Evaluate(context) ?? 1f);

            var palette = Application.isPlaying && _deckStateProvider != null
                ? _deckStateProvider.GetState(_stage != null ? _stage.Deck : StageDeck.Next).Palette
                : null;
            _evaluatedFillColor = EvaluatedPaletteColor.Evaluate(_params.FillColor, palette, context);
            _evaluatedStrokeColor = EvaluatedPaletteColor.Evaluate(_params.StrokeColor, palette, context);

            _evaluatedRepeater = EvaluatedRepeater.Evaluate(_params.Repeater, context, MaxRepeaterCopies);
        }

        private int CalculateGeometryHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)_params.Shape;
                hash = hash * 31 + _evaluatedSize.GetHashCode();
                hash = hash * 31 + _evaluatedPoints;
                hash = hash * 31 + _evaluatedInnerRadius.GetHashCode();
                hash = hash * 31 + _params.EllipseSegments;
                hash = hash * 31 + _params.FillEnabled.GetHashCode();
                hash = hash * 31 + _params.StrokeEnabled.GetHashCode();
                hash = hash * 31 + _evaluatedStrokeWidth.GetHashCode();
                hash = hash * 31 + (_params.StrokeTrim?.Enabled.GetHashCode() ?? 0);
                hash = hash * 31 + _evaluatedTrimStart.GetHashCode();
                hash = hash * 31 + _evaluatedTrimEnd.GetHashCode();
                hash = hash * 31 + _evaluatedTrimOffset.GetHashCode();
                hash = hash * 31 + _evaluatedRepeater.GetHashCode();
                if (_evaluatedRepeater.TransformMode == RepeaterTransformMode.FromSource)
                {
                    for (int i = 0; i < _evaluatedRepeater.Copies; i++)
                    {
                        float phase = _evaluatedRepeater.AnimationPhaseOffset * i;
                        hash = hash * 31 + GetRepeaterCopyTransform(i, phase).GetHashCode();
                        hash = hash * 31 + GetRepeaterCopyOpacity(i, phase).GetHashCode();
                    }
                }
                return hash;
            }
        }

        public Matrix4x4 GetRepeaterCopyTransform(int copyIndex, float phaseOffset)
        {
            if (copyIndex == 0) return Matrix4x4.identity;
            ModulationContext context = _modulationContext.WithAnimationPhaseOffset(phaseOffset);
            Vector3 position = _params.Position?.Evaluate(context) ?? Vector3.zero;
            Vector3 rotation = _params.Rotation?.Evaluate(context) ?? Vector3.zero;
            Vector3 scale = _params.Scale?.Evaluate(context) ?? Vector3.one;
            Vector3 anchor = _params.Anchor?.Evaluate(context) ?? Vector3.zero;
            Matrix4x4 copyMatrix = Matrix4x4.TRS(position, Quaternion.Euler(rotation), scale) *
                                   Matrix4x4.Translate(-anchor);
            Matrix4x4 baseMatrix = Matrix4x4.TRS(
                                       _evaluatedPosition,
                                       Quaternion.Euler(_evaluatedRotation),
                                       _evaluatedScale) *
                                   Matrix4x4.Translate(-_evaluatedAnchor);
            return baseMatrix.inverse * copyMatrix;
        }

        public float GetRepeaterCopyOpacity(int copyIndex, float phaseOffset)
        {
            ModulationContext context = _modulationContext.WithAnimationPhaseOffset(phaseOffset);
            return Mathf.Clamp01(_params.Opacity?.Evaluate(context) ?? 1f);
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(_mesh);
            DestroyRuntimeObject(_fillMaterial);
            DestroyRuntimeObject(_strokeMaterial);
        }

        private static void DestroyRuntimeObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
