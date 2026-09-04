using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnitySimpleContainer;

namespace Aetherin
{
    /// <summary>
    /// CameraStageへ直接描画するプリミティブ立体レイヤー。
    /// TransformとSizeはShader行列で処理し、形状または分割数が変わったときだけMeshを再生成する。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed partial class Primitive3DLayer : StageLayer, IRepeaterCopyProvider
    {
        private const int MaxRepeaterCopies = 128;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        private static readonly int ColorModeId = Shader.PropertyToID("_ColorMode");
        private static readonly int UvParamsId = Shader.PropertyToID("_UvParams");
        private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
        private static readonly int ToonThresholdId = Shader.PropertyToID("_ToonThreshold");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MaterialModeId = Shader.PropertyToID("_MaterialMode");
        private static readonly int GlassRefractionId = Shader.PropertyToID("_GlassRefraction");
        private static readonly int GlassTintId = Shader.PropertyToID("_GlassTint");
        private static readonly int GlassFresnelPowerId = Shader.PropertyToID("_GlassFresnelPower");
        private static readonly int GlassFresnelIntensityId = Shader.PropertyToID("_GlassFresnelIntensity");
        private static readonly int GlassChromaticAberrationId = Shader.PropertyToID("_GlassChromaticAberration");
        private static readonly int GlassDistortionId = Shader.PropertyToID("_GlassDistortion");
        private static readonly int GlassDistortionScaleId = Shader.PropertyToID("_GlassDistortionScale");
        private static readonly int ShapeMatrixId = Shader.PropertyToID("_ShapeMatrix");
        private static readonly int ShapeNormalMatrixId = Shader.PropertyToID("_ShapeNormalMatrix");
        private static readonly int UsePaletteRandomId = Shader.PropertyToID("_UsePaletteRandom");
        private static readonly int PaletteRandomSeedId = Shader.PropertyToID("_PaletteRandomSeed");
        private static readonly int[] PaletteColorIds = CreatePaletteColorIds();

        [SerializeField] private Primitive3DLayerParams _params = new();
        [SerializeField] private Shader _surfaceShader;

        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<Color> _vertexColors = new();
        private readonly List<int> _triangles = new();
        private readonly List<Vector3> _wireVertices = new();
        private readonly List<Vector2> _wireUvs = new();
        private readonly List<Color> _wireVertexColors = new();
        private readonly List<int> _wireTriangles = new();

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _material;
        private GameObject _wireObject;
        private MeshFilter _wireMeshFilter;
        private MeshRenderer _wireRenderer;
        private Mesh _wireMesh;
        private Material _wireMaterial;
        private Bounds _geometryBounds;
        private Bounds _wireGeometryBounds;
        private int _geometryHash;

        private Vector3 _evaluatedPosition;
        private Vector3 _evaluatedRotation;
        private Vector3 _evaluatedScale = Vector3.one;
        private Vector3 _evaluatedAnchor;
        private Vector3 _evaluatedSize = Vector3.one;
        private float _evaluatedCornerRadius = 0.15f;
        private float _evaluatedOpacity = 1f;
        private float _evaluatedColorIntensity = 1f;
        private float _evaluatedAlpha = 1f;
        private float _evaluatedUvScale = 1f;
        private float _evaluatedUvOffset;
        private Vector3 _evaluatedLightDirection = Vector3.up;
        private float _evaluatedToonThreshold = 0.5f;
        private float _evaluatedMetallic;
        private float _evaluatedSmoothness = 0.5f;
        private float _evaluatedGlassRefraction = 0.025f;
        private float _evaluatedGlassTint = 0.2f;
        private float _evaluatedGlassFresnelPower = 3f;
        private float _evaluatedGlassFresnelIntensity = 0.8f;
        private float _evaluatedGlassChromaticAberration = 0.002f;
        private float _evaluatedGlassDistortion = 0.003f;
        private float _evaluatedGlassDistortionScale = 12f;
        private EvaluatedRepeater _evaluatedRepeater;
        private Color _evaluatedColorA = Color.white;
        private Color _evaluatedColorB = Color.white;
        private Color _evaluatedWireColor = Color.white;
        private float _evaluatedWireWidth = 0.015f;
        private ModulationContext _modulationContext;

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
            _params.IcosphereSubdivisions = Mathf.Clamp(_params.IcosphereSubdivisions, 0, 5);
            _params.CornerSegments = Mathf.Max(1, _params.CornerSegments);
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
            _params.CornerRadius ??= new FloatParameter(0.15f);
            _params.ColorIntensity ??= new FloatParameter(1f);
            _params.Alpha ??= new FloatParameter(1f);
            _params.WireWidth ??= new FloatParameter(0.015f);
            _params.WireColorIntensity ??= new FloatParameter(1f);
            _params.WireAlpha ??= new FloatParameter(1f);
            _params.UvScale ??= new FloatParameter(1f);
            _params.UvOffset ??= new FloatParameter(0f);
            _params.LightDirection ??= new Vector3Parameter(new Vector3(0.3f, 0.8f, -0.5f));
            _params.ToonThreshold ??= new FloatParameter(0.5f);
            _params.Metallic ??= new FloatParameter(0f);
            _params.Smoothness ??= new FloatParameter(0.5f);
            _params.GlassRefraction ??= new FloatParameter(0.025f);
            _params.GlassTint ??= new FloatParameter(0.2f);
            _params.GlassFresnelPower ??= new FloatParameter(3f);
            _params.GlassFresnelIntensity ??= new FloatParameter(0.8f);
            _params.GlassChromaticAberration ??= new FloatParameter(0.002f);
            _params.GlassDistortion ??= new FloatParameter(0.003f);
            _params.GlassDistortionScale ??= new FloatParameter(12f);
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

            EnsureWireResources();

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
            _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        }

        private void EnsureWireResources()
        {
            if (_wireObject == null)
            {
                // Stageのswapではレイヤーの子も複製される。実行時フィールドの参照は
                // 引き継がれないため、既存のWireframeを先に拾わないとswapごとに増殖する。
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    var child = transform.GetChild(i).gameObject;
                    if (child.name != "Wireframe") continue;

                    if (_wireObject == null)
                    {
                        _wireObject = child;
                    }
                    else
                    {
                        child.SetActive(false);
                        if (Application.isPlaying) Destroy(child);
                        else DestroyImmediate(child);
                    }
                }

                if (_wireObject == null)
                {
                    _wireObject = new GameObject("Wireframe");
                    _wireObject.transform.SetParent(transform, false);
                }

                _wireObject.hideFlags = HideFlags.DontSave;
                _wireObject.layer = gameObject.layer;
            }

            // UnityEngine.Objectは破棄済みでもC#参照自体はnullではないため、
            // ?? / ??= ではMissing Componentを検出できない。
            if (_wireMeshFilter == null)
            {
                _wireMeshFilter = _wireObject.GetComponent<MeshFilter>();
                if (_wireMeshFilter == null) _wireMeshFilter = _wireObject.AddComponent<MeshFilter>();
            }

            if (_wireRenderer == null)
            {
                _wireRenderer = _wireObject.GetComponent<MeshRenderer>();
                if (_wireRenderer == null) _wireRenderer = _wireObject.AddComponent<MeshRenderer>();
            }

            if (_wireMesh == null)
            {
                _wireMesh = new Mesh
                {
                    name = $"{name} Primitive 3D Wire Mesh",
                    hideFlags = HideFlags.DontSave,
                    indexFormat = IndexFormat.UInt32,
                };
                _wireMesh.MarkDynamic();
                _wireMeshFilter.sharedMesh = _wireMesh;
            }

            if (_wireMaterial != null) return;
            Shader shader = _surfaceShader != null
                ? _surfaceShader
                : Shader.Find("Aetherin/Primitive 3D Unlit");
            if (shader == null) return;

            _wireMaterial = new Material(shader)
            {
                name = $"{name} Primitive 3D Wire Material",
                hideFlags = HideFlags.DontSave,
            };
            _wireRenderer.sharedMaterial = _wireMaterial;
            _wireRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _wireRenderer.receiveShadows = false;
        }

        private void EvaluateParameters(bool runtime)
        {
            var context = new ModulationContext(
                runtime ? Time.timeAsDouble : 0d,
                runtime ? _audioFeatureProvider : null,
                runtime ? _beatManager : null,
                runtime && (_stage == null || _stage.Deck == StageDeck.Next));
            _modulationContext = context;

            _evaluatedPosition = _params.Position?.Evaluate(context) ?? Vector3.zero;
            _evaluatedRotation = _params.Rotation?.Evaluate(context) ?? Vector3.zero;
            _evaluatedScale = _params.Scale?.Evaluate(context) ?? Vector3.one;
            _evaluatedAnchor = _params.Anchor?.Evaluate(context) ?? Vector3.zero;
            _evaluatedSize = _params.Size?.Evaluate(context) ?? Vector3.one;
            _evaluatedSize.x = Mathf.Max(0f, _evaluatedSize.x);
            _evaluatedSize.y = Mathf.Max(0f, _evaluatedSize.y);
            _evaluatedSize.z = Mathf.Max(0f, _evaluatedSize.z);
            float maxCornerRadius = Mathf.Min(_evaluatedSize.x, Mathf.Min(_evaluatedSize.y, _evaluatedSize.z)) * 0.5f;
            _evaluatedCornerRadius = Mathf.Clamp(_params.CornerRadius?.Evaluate(context) ?? 0f, 0f, maxCornerRadius);
            _evaluatedOpacity = Mathf.Clamp01(_params.Opacity?.Evaluate(context) ?? 1f);
            _evaluatedColorIntensity = Mathf.Max(0f, _params.ColorIntensity?.Evaluate(context) ?? 1f);
            _evaluatedAlpha = Mathf.Clamp01(_params.Alpha?.Evaluate(context) ?? 1f);
            _evaluatedWireWidth = Mathf.Max(0.0001f, _params.WireWidth?.Evaluate(context) ?? 0.015f);
            _evaluatedUvScale = _params.UvScale?.Evaluate(context) ?? 1f;
            _evaluatedUvOffset = _params.UvOffset?.Evaluate(context) ?? 0f;
            _evaluatedLightDirection = _params.LightDirection?.Evaluate(context) ?? Vector3.up;
            if (_evaluatedLightDirection.sqrMagnitude < 0.000001f) _evaluatedLightDirection = Vector3.up;
            _evaluatedLightDirection.Normalize();
            _evaluatedToonThreshold = Mathf.Clamp01(_params.ToonThreshold?.Evaluate(context) ?? 0.5f);
            _evaluatedMetallic = Mathf.Clamp01(_params.Metallic?.Evaluate(context) ?? 0f);
            _evaluatedSmoothness = Mathf.Clamp01(_params.Smoothness?.Evaluate(context) ?? 0.5f);
            _evaluatedGlassRefraction = Mathf.Max(0f, _params.GlassRefraction?.Evaluate(context) ?? 0.025f);
            _evaluatedGlassTint = Mathf.Clamp01(_params.GlassTint?.Evaluate(context) ?? 0.2f);
            _evaluatedGlassFresnelPower = Mathf.Max(0.01f, _params.GlassFresnelPower?.Evaluate(context) ?? 3f);
            _evaluatedGlassFresnelIntensity = Mathf.Max(0f, _params.GlassFresnelIntensity?.Evaluate(context) ?? 0.8f);
            _evaluatedGlassChromaticAberration = Mathf.Max(0f,
                _params.GlassChromaticAberration?.Evaluate(context) ?? 0.002f);
            _evaluatedGlassDistortion = Mathf.Max(0f, _params.GlassDistortion?.Evaluate(context) ?? 0.003f);
            _evaluatedGlassDistortionScale = Mathf.Max(0.01f,
                _params.GlassDistortionScale?.Evaluate(context) ?? 12f);
            _evaluatedRepeater = EvaluatedRepeater.Evaluate(_params.Repeater, context, MaxRepeaterCopies);

            ColorPalette palette = Application.isPlaying && _deckStateProvider != null
                ? _deckStateProvider.GetState(_stage != null ? _stage.Deck : StageDeck.Next).Palette
                : null;
            _evaluatedColorA = EvaluatePaletteColor(palette, _params.ColorA);
            _evaluatedColorB = EvaluatePaletteColor(palette, _params.ColorB);
            float wireIntensity = Mathf.Max(0f, _params.WireColorIntensity?.Evaluate(context) ?? 1f);
            float wireAlpha = Mathf.Clamp01(_params.WireAlpha?.Evaluate(context) ?? 1f);
            _evaluatedWireColor = PaletteColorParameter.Resolve(palette, _params.WireColor).linear * wireIntensity;
            _evaluatedWireColor.a = wireAlpha *
                                    (_evaluatedRepeater.TransformMode == RepeaterTransformMode.FromSource
                                        ? 1f
                                        : _evaluatedOpacity);
        }

        private Color EvaluatePaletteColor(ColorPalette palette, PaletteColorSource source)
        {
            Color color = PaletteColorParameter.Resolve(palette, source).linear * _evaluatedColorIntensity;
            color.a = _evaluatedAlpha *
                      (_evaluatedRepeater.TransformMode == RepeaterTransformMode.FromSource
                          ? 1f
                          : _evaluatedOpacity);
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
            _material.SetFloat(MetallicId, _evaluatedMetallic);
            _material.SetFloat(SmoothnessId, _evaluatedSmoothness);
            bool glass = _params.MaterialMode == Primitive3DMaterialMode.Glass;
            bool lit = _params.MaterialMode == Primitive3DMaterialMode.Lit;
            _material.SetFloat(MaterialModeId, lit ? 2f : glass ? 1f : 0f);
            // Lit is a real opaque surface so it can participate in URP's depth/normals
            // prepass used by SSR.  Previously the default Transparent layer blend mode
            // kept a Lit primitive in the transparent queue, where SSR cannot reflect it.
            LayerMaterialUtility.ApplyBlendMode(_material,
                glass ? LayerBlendMode.Transparent : lit ? LayerBlendMode.Opaque : _params.BlendMode);
            _meshRenderer.receiveShadows = lit;
            _material.SetFloat(GlassRefractionId, _evaluatedGlassRefraction);
            _material.SetFloat(GlassTintId, _evaluatedGlassTint);
            _material.SetFloat(GlassFresnelPowerId, _evaluatedGlassFresnelPower);
            _material.SetFloat(GlassFresnelIntensityId, _evaluatedGlassFresnelIntensity);
            _material.SetFloat(GlassChromaticAberrationId, _evaluatedGlassChromaticAberration);
            _material.SetFloat(GlassDistortionId, _evaluatedGlassDistortion);
            _material.SetFloat(GlassDistortionScaleId, _evaluatedGlassDistortionScale);
            ApplyRandomPalette();

            Vector3 rotation = new(
                Mathf.Repeat(_evaluatedRotation.x, 360f),
                Mathf.Repeat(_evaluatedRotation.y, 360f),
                Mathf.Repeat(_evaluatedRotation.z, 360f));
            Matrix4x4 matrix = Matrix4x4.TRS(
                                   _evaluatedPosition,
                                   Quaternion.Euler(rotation),
                                   _evaluatedScale) *
                               Matrix4x4.Translate(-_evaluatedAnchor) *
                               Matrix4x4.Scale(GetGeometryScale(_evaluatedSize));

            _material.SetMatrix(ShapeMatrixId, matrix);
            _material.SetMatrix(ShapeNormalMatrixId, matrix.inverse.transpose);
            ApplyWireAppearance(matrix);
            ApplyTransformedBounds(matrix);
        }

        private void ApplyWireAppearance(Matrix4x4 matrix)
        {
            if (_wireMaterial == null) return;
            _wireMaterial.SetColor(BaseColorId, _evaluatedWireColor);
            _wireMaterial.SetColor(ColorBId, _evaluatedWireColor);
            _wireMaterial.SetFloat(ColorModeId, (float)Primitive3DColorMode.Solid);
            _wireMaterial.SetFloat(MaterialModeId, 0f);
            LayerMaterialUtility.ApplyBlendMode(_wireMaterial,
                _params.MaterialMode == Primitive3DMaterialMode.Glass
                    ? LayerBlendMode.Transparent
                    : _params.BlendMode);
            _wireMaterial.SetFloat(UsePaletteRandomId, 0f);
            _wireMaterial.SetMatrix(ShapeMatrixId, matrix);
            _wireMaterial.SetMatrix(ShapeNormalMatrixId, matrix.inverse.transpose);

            if (_wireMesh == null) return;
            Bounds bounds = _wireGeometryBounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = matrix.MultiplyPoint3x4(center + Vector3.Scale(extents, new Vector3(x, y, z)));
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }
            _wireMesh.bounds = new Bounds((min + max) * 0.5f, max - min);
        }

        protected override void ApplyCustomLayerState(bool visible, int order)
        {
            _meshRenderer.forceRenderingOff = !visible || _params.RenderMode == Primitive3DRenderMode.Wireframe;
            if (_wireRenderer != null)
            {
                _wireRenderer.forceRenderingOff = !visible || _params.RenderMode == Primitive3DRenderMode.Surface;
                _wireRenderer.sortingOrder = order + 1;
            }
            _meshRenderer.sortingOrder = order;
        }

        private void ApplyRandomPalette()
        {
            bool enabled = _params.ColorMode == Primitive3DColorMode.PaletteRandom;
            _material.SetFloat(UsePaletteRandomId, enabled ? 1f : 0f);
            if (!enabled) return;

            _material.SetFloat(PaletteRandomSeedId, _params.PaletteRandomSeed);
            ColorPalette palette = Application.isPlaying && _deckStateProvider != null
                ? _deckStateProvider.GetState(_stage != null ? _stage.Deck : StageDeck.Next).Palette
                : null;
            for (int i = 0; i < PaletteColorIds.Length; i++)
                _material.SetColor(PaletteColorIds[i], EvaluatePaletteColor(palette, (PaletteColorSource)i));
        }

        private static int[] CreatePaletteColorIds()
        {
            var ids = new int[6];
            for (int i = 0; i < ids.Length; i++) ids[i] = Shader.PropertyToID($"_PaletteColor{i}");
            return ids;
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
                if (_params.Primitive == Primitive3DType.Cylinder)
                    hash = hash * 31 + _params.RadialSegments;
                if (_params.Primitive == Primitive3DType.Icosphere)
                    hash = hash * 31 + _params.IcosphereSubdivisions;
                if (_params.Primitive == Primitive3DType.RoundedBox)
                {
                    hash = hash * 31 + _params.CornerSegments;
                    hash = hash * 31 + _evaluatedCornerRadius.GetHashCode();
                    hash = hash * 31 + _evaluatedSize.GetHashCode();
                }
                hash = hash * 31 + _evaluatedRepeater.GetHashCode();
                hash = hash * 31 + _evaluatedWireWidth.GetHashCode();
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
                                   Matrix4x4.Translate(-anchor) *
                                   Matrix4x4.Scale(GetGeometryScale(_evaluatedSize));
            Matrix4x4 baseMatrix = Matrix4x4.TRS(
                                       _evaluatedPosition,
                                       Quaternion.Euler(_evaluatedRotation),
                                       _evaluatedScale) *
                                   Matrix4x4.Translate(-_evaluatedAnchor) *
                                   Matrix4x4.Scale(GetGeometryScale(_evaluatedSize));
            return baseMatrix.inverse * copyMatrix;
        }

        private Vector3 GetGeometryScale(Vector3 size) =>
            _params.Primitive == Primitive3DType.RoundedBox ? Vector3.one : size;

        public float GetRepeaterCopyOpacity(int copyIndex, float phaseOffset)
        {
            ModulationContext context = _modulationContext.WithAnimationPhaseOffset(phaseOffset);
            return Mathf.Clamp01(_params.Opacity?.Evaluate(context) ?? 1f);
        }

        private static void EnsureCapacity<T>(List<T> list, int capacity)
        {
            if (list.Capacity < capacity) list.Capacity = capacity;
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(_mesh);
            DestroyRuntimeObject(_material);
            DestroyRuntimeObject(_wireMesh);
            DestroyRuntimeObject(_wireMaterial);
            DestroyRuntimeObject(_wireObject);
        }

        private static void DestroyRuntimeObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
