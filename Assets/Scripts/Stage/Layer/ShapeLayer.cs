using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnitySimpleContainer;

namespace Aetherin
{
    /// <summary>
    /// パラメトリックな基本図形を Mesh に変換し、CameraStage のカメラへ直接描画するレイヤー。
    /// Mesh は実行時キャッシュで、保存対象は ShapeLayerParams のみ。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed partial class ShapeLayer : StageLayer, IRepeaterCopyProvider
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
