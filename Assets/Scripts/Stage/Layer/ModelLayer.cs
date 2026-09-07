using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ModelLayer : StageLayer
    {
        private const int MaxPooledModelsPerSource = 8;
        private static readonly Dictionary<GameObject, Stack<PooledModel>> ModelPool = new();
        private static Transform _poolRoot;

        private static readonly int ColorAId = Shader.PropertyToID("_ColorA");
        private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        private static readonly int GradientId = Shader.PropertyToID("_UseGradient");
        private static readonly int GradientParamsId = Shader.PropertyToID("_GradientParams");
        private static readonly int MaterialModeId = Shader.PropertyToID("_MaterialMode");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int GlassRefractionId = Shader.PropertyToID("_GlassRefraction");
        private static readonly int GlassTintId = Shader.PropertyToID("_GlassTint");
        private static readonly int GlassFresnelPowerId = Shader.PropertyToID("_GlassFresnelPower");
        private static readonly int GlassFresnelIntensityId = Shader.PropertyToID("_GlassFresnelIntensity");
        private static readonly int GlassChromaticAberrationId = Shader.PropertyToID("_GlassChromaticAberration");
        private static readonly int GlassDistortionId = Shader.PropertyToID("_GlassDistortion");
        private static readonly int GlassDistortionScaleId = Shader.PropertyToID("_GlassDistortionScale");

        [SerializeField] private ModelLayerParams _params = new();
        [SerializeField] private Shader _surfaceShader;
        [SerializeField] private Shader _wireShader;

        private CameraStage _cameraStage;
        private StageBase _stage;
        private IAudioFeatureProvider _audio;
        private IBeatManager _beat;
        private IDeckStateProvider _deckState;
        private GameObject _modelInstance;
        private GameObject _loadedSource;
        private string _loadedKey;
        private readonly List<Renderer> _surfaceRenderers = new();
        private readonly List<Renderer> _wireRenderers = new();
        private readonly List<Material> _materials = new();
        private readonly List<Mesh> _wireMeshes = new();

        public override IParams Params => _params;
        protected override StageLayerParams LayerParams => _params;
        protected override Renderer LayerRenderer => null;

        [Inject]
        private void Construct(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckState)
        {
            _audio = audio;
            _beat = beat;
            _deckState = deckState;
        }

        public void Initialize(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckState)
        {
            _audio = audio;
            _beat = beat;
            _deckState = deckState;
            Initialize();
        }

        private void Awake() => Initialize();
        private void OnEnable() => Initialize();

        private void Initialize()
        {
            EnsureParameters();
            _cameraStage = GetComponentInParent<CameraStage>();
            _params.GetAvailableModelKeys = _cameraStage != null ? _cameraStage.GetModelKeys : null;
            var keys = _params.GetAvailableModelKeys?.Invoke();
            if (string.IsNullOrWhiteSpace(_params.ModelKey) && keys != null && keys.Count > 0)
                _params.ModelKey = keys[0];
            _stage = GetComponentInParent<StageBase>();
            if (_surfaceShader == null) _surfaceShader = Shader.Find("Aetherin/Model Layer Surface");
            if (_wireShader == null) _wireShader = Shader.Find("Aetherin/Model Layer Wire");
            RemoveCopiedModelInstances();
            EnsureModel();
            EvaluateAndApply();
        }

        private void Update()
        {
            EnsureParameters();
            EnsureModel();
            EvaluateAndApply();
        }

        protected override void OnValidate()
        {
            EnsureParameters();
            base.OnValidate();
            if (isActiveAndEnabled) Initialize();
        }

        private void EnsureParameters()
        {
            _params ??= new ModelLayerParams();
            _params.Opacity ??= new FloatParameter(1f);
            _params.Position ??= new Vector3Parameter();
            _params.Rotation ??= new Vector3Parameter();
            _params.Scale ??= new Vector3Parameter(Vector3.one);
            _params.Anchor ??= new Vector3Parameter();
            _params.Color ??= new PaletteColorParameter();
            _params.WireColor ??= new PaletteColorParameter();
            _params.AnimationSpeed ??= new FloatParameter(1f);
            _params.Metallic ??= new FloatParameter(0f);
            _params.Smoothness ??= new FloatParameter(0.5f);
            _params.GlassRefraction ??= new FloatParameter(0.025f);
            _params.GlassTint ??= new FloatParameter(0.2f);
            _params.GlassFresnelPower ??= new FloatParameter(3f);
            _params.GlassFresnelIntensity ??= new FloatParameter(0.8f);
            _params.GlassChromaticAberration ??= new FloatParameter(0.002f);
            _params.GlassDistortion ??= new FloatParameter(0.003f);
            _params.GlassDistortionScale ??= new FloatParameter(12f);
            _params.Color.EnsureInitialized();
            _params.WireColor.EnsureInitialized();
        }

        private void EnsureModel()
        {
            _cameraStage ??= GetComponentInParent<CameraStage>();
            string key = _params.ModelKey ?? string.Empty;
            if (_loadedKey == key && _modelInstance != null) return;
            ClearModel();
            _loadedKey = key;
            GameObject source = _cameraStage != null ? _cameraStage.ResolveModel(key) : null;
            if (source == null) return;

            _loadedSource = source;
            if (TryRentModel(source, out PooledModel pooled))
            {
                _modelInstance = pooled.Instance;
                _surfaceRenderers.AddRange(pooled.SurfaceRenderers);
                _wireRenderers.AddRange(pooled.WireRenderers);
                _materials.AddRange(pooled.Materials);
                _wireMeshes.AddRange(pooled.WireMeshes);
                AttachModelInstance(key, source);
                return;
            }

            _modelInstance = Instantiate(source, transform, false);
            _modelInstance.name = $"Model ({key})";
            foreach (var renderer in _modelInstance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer) continue;
                _surfaceRenderers.Add(renderer);
                var material = new Material(_surfaceShader) { name = "Model Layer Surface (Runtime)" };
                renderer.sharedMaterials = BuildMaterialArray(renderer.sharedMaterials.Length, material);
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
                _materials.Add(material);
                CreateWireRenderer(renderer);
            }
        }

        private void AttachModelInstance(string key, GameObject source)
        {
            _modelInstance.transform.SetParent(transform, false);
            _modelInstance.name = $"Model ({key})";
            ResetModelRuntime(_modelInstance);
            _modelInstance.SetActive(true);

            // Animator.Rebind/UpdateやOnEnableがroot Transformをクリップの初期姿勢へ戻す場合が
            // あるため、再アクティブ化した後でPrefab基準のlocal Transformを復元する。
            _modelInstance.transform.localPosition = source.transform.localPosition;
            _modelInstance.transform.localRotation = source.transform.localRotation;
            _modelInstance.transform.localScale = source.transform.localScale;
        }

        /// <summary>
        /// Stage複製時にruntimeモデルまで子として複製されるのを防ぐため、一時的に階層外へ退避する。
        /// </summary>
        internal GameObject DetachModelForStageClone()
        {
            if (_modelInstance == null) return null;
            _modelInstance.transform.SetParent(GetPoolRoot(), true);
            return _modelInstance;
        }

        internal void RestoreModelAfterStageClone(GameObject instance)
        {
            if (instance == null || instance != _modelInstance) return;
            // 退避時と同じくworldPositionStaysを有効にし、元のlocal Transformへ戻す。
            instance.transform.SetParent(transform, true);
        }

        // StageManagerはStage全体をInstantiateしてSwap後のNextを作る。
        // この実行時生成モデルも子として複製される一方、_modelInstanceは非シリアライズなので
        // 複製先ではnullとなり、EnsureModelがさらに1個生成してしまう。複製された子を除去して
        // 新しいStage専用のモデルだけを生成する。
        private void RemoveCopiedModelInstances()
        {
            if (_modelInstance != null) return;

            for (int index = transform.childCount - 1; index >= 0; index--)
            {
                Transform child = transform.GetChild(index);
                if (!child.name.StartsWith("Model (")) continue;

                child.gameObject.SetActive(false);
                DestroyRuntimeObject(child.gameObject);
            }
        }

        private static Material[] BuildMaterialArray(int count, Material material)
        {
            count = Mathf.Max(1, count);
            var result = new Material[count];
            for (int i = 0; i < count; i++) result[i] = material;
            return result;
        }

        private void CreateWireRenderer(Renderer sourceRenderer)
        {
            Mesh sourceMesh = sourceRenderer switch
            {
                MeshRenderer meshRenderer => meshRenderer.GetComponent<MeshFilter>()?.sharedMesh,
                SkinnedMeshRenderer skinned => skinned.sharedMesh,
                _ => null,
            };
            if (sourceMesh == null || _wireShader == null) return;

            var wireObject = new GameObject($"{sourceRenderer.name} Wire");
            wireObject.transform.SetParent(sourceRenderer.transform, false);
            var filter = wireObject.AddComponent<MeshFilter>();
            var wireRenderer = wireObject.AddComponent<MeshRenderer>();
            var wireMesh = BuildWireMesh(sourceMesh);
            var wireMaterial = new Material(_wireShader) { name = "Model Layer Wire (Runtime)" };
            filter.sharedMesh = wireMesh;
            wireRenderer.sharedMaterial = wireMaterial;
            _wireMeshes.Add(wireMesh);
            _wireRenderers.Add(wireRenderer);
            _materials.Add(wireMaterial);
        }

        private static Mesh BuildWireMesh(Mesh source)
        {
            var edges = new HashSet<ulong>();
            var indices = new List<int>();
            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                int[] triangles = source.GetTriangles(subMesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    AddEdge(triangles[i], triangles[i + 1], edges, indices);
                    AddEdge(triangles[i + 1], triangles[i + 2], edges, indices);
                    AddEdge(triangles[i + 2], triangles[i], edges, indices);
                }
            }
            var mesh = new Mesh { name = $"{source.name} Wire (Runtime)", indexFormat = source.indexFormat };
            mesh.vertices = source.vertices;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.bounds = source.bounds;
            return mesh;
        }

        private static void AddEdge(int a, int b, HashSet<ulong> edges, List<int> indices)
        {
            uint min = (uint)Mathf.Min(a, b);
            uint max = (uint)Mathf.Max(a, b);
            ulong key = ((ulong)min << 32) | max;
            if (!edges.Add(key)) return;
            indices.Add(a);
            indices.Add(b);
        }

        private void EvaluateAndApply()
        {
            var context = new ModulationContext(Time.unscaledTimeAsDouble,
                Application.isPlaying ? _audio : null, Application.isPlaying ? _beat : null, Application.isPlaying);
            float layerOpacity = Mathf.Clamp01(_params.Opacity.Evaluate(context));
            Vector3 position = _params.Position.Evaluate(context);
            Vector3 rotation = _params.Rotation.Evaluate(context);
            Vector3 scale = _params.Scale.Evaluate(context);
            Vector3 anchor = _params.Anchor.Evaluate(context);
            transform.localPosition = position - Quaternion.Euler(rotation) * Vector3.Scale(anchor, scale);
            transform.localRotation = Quaternion.Euler(rotation);
            transform.localScale = scale;

            ColorPalette palette = Application.isPlaying && _deckState != null
                ? _deckState.GetState(_stage != null ? _stage.Deck : StageDeck.Next).Palette
                : PaletteColorParameter.FallbackPalette;
            ApplyColors(EvaluatedPaletteColor.Evaluate(_params.Color, palette, context), false, layerOpacity, context);
            ApplyColors(EvaluatedPaletteColor.Evaluate(_params.WireColor, palette, context), true, layerOpacity, context);
            float speed = _params.AnimationSpeed.Evaluate(context);
            if (_modelInstance != null)
                foreach (var animator in _modelInstance.GetComponentsInChildren<Animator>(true))
                    animator.speed = _params.PlayAnimation ? speed : 0f;
            // Apply the effective visibility, including every ancestor GroupLayer.
            // Calling ApplyCustomLayerState with _params.Visible here used to
            // overwrite the group state on every Update.
            ApplyLayerState();
        }

        private void ApplyColors(EvaluatedPaletteColor color, bool wire, float layerOpacity, ModulationContext context)
        {
            var renderers = wire ? _wireRenderers : _surfaceRenderers;
            for (int i = 0; i < renderers.Count; i++)
            {
                var material = renderers[i].sharedMaterial;
                if (material == null) continue;
                Color a = color.IsPaletteRandom && color.PaletteColors?.Length > 0
                    ? color.PaletteColors[Mathf.Abs(color.RandomSeed + i * 31) % color.PaletteColors.Length]
                    : color.ColorA;
                a.a *= layerOpacity;
                Color b = color.IsGradient ? color.ColorB : a;
                b.a *= layerOpacity;
                material.SetColor(ColorAId, a);
                material.SetColor(ColorBId, b);
                material.SetFloat(GradientId, color.IsGradient ? 1f : 0f);
                material.SetVector(GradientParamsId, new Vector4(color.AngleDegrees, color.Offset, color.Scale, 0f));
                if (!wire)
                {
                    bool glass = _params.MaterialMode == ModelLayerMaterialMode.Glass;
                    bool lit = _params.MaterialMode == ModelLayerMaterialMode.Lit;
                    material.SetFloat(MaterialModeId, lit ? 2f : glass ? 1f : 0f);
                    material.SetFloat(MetallicId, Mathf.Clamp01(_params.Metallic.Evaluate(context)));
                    material.SetFloat(SmoothnessId, Mathf.Clamp01(_params.Smoothness.Evaluate(context)));
                    material.SetFloat(GlassRefractionId, Mathf.Max(0f, _params.GlassRefraction.Evaluate(context)));
                    material.SetFloat(GlassTintId, Mathf.Clamp01(_params.GlassTint.Evaluate(context)));
                    material.SetFloat(GlassFresnelPowerId, Mathf.Max(0.01f, _params.GlassFresnelPower.Evaluate(context)));
                    material.SetFloat(GlassFresnelIntensityId, Mathf.Max(0f, _params.GlassFresnelIntensity.Evaluate(context)));
                    material.SetFloat(GlassChromaticAberrationId, Mathf.Max(0f, _params.GlassChromaticAberration.Evaluate(context)));
                    material.SetFloat(GlassDistortionId, Mathf.Max(0f, _params.GlassDistortion.Evaluate(context)));
                    material.SetFloat(GlassDistortionScaleId, Mathf.Max(0.01f, _params.GlassDistortionScale.Evaluate(context)));
                    LayerMaterialUtility.ApplyBlendMode(material, glass ? LayerBlendMode.Transparent : lit ? LayerBlendMode.Opaque : _params.BlendMode);
                    renderers[i].receiveShadows = !glass;
                }
                else LayerMaterialUtility.ApplyBlendMode(material, _params.MaterialMode == ModelLayerMaterialMode.Glass ? LayerBlendMode.Transparent : _params.BlendMode);
            }
        }


        protected override void ApplyCustomLayerState(bool visible, int order)
        {
            bool surface = _params.RenderMode != ModelLayerRenderMode.Wireframe;
            bool wire = _params.RenderMode != ModelLayerRenderMode.Surface;
            for (int i = 0; i < _surfaceRenderers.Count; i++)
            {
                _surfaceRenderers[i].forceRenderingOff = !visible || !surface;
                _surfaceRenderers[i].sortingOrder = order;
            }
            for (int i = 0; i < _wireRenderers.Count; i++)
            {
                _wireRenderers[i].forceRenderingOff = !visible || !wire;
                _wireRenderers[i].sortingOrder = order + 1;
            }
        }

        private void ClearModel()
        {
            if (_modelInstance != null && Application.isPlaying && _loadedSource != null)
            {
                ReturnModel(_loadedSource, new PooledModel(
                    _modelInstance,
                    _surfaceRenderers.ToArray(),
                    _wireRenderers.ToArray(),
                    _materials.ToArray(),
                    _wireMeshes.ToArray()));
            }
            else
            {
                if (_modelInstance != null) DestroyRuntimeObject(_modelInstance);
                foreach (var material in _materials) DestroyRuntimeObject(material);
                foreach (var mesh in _wireMeshes) DestroyRuntimeObject(mesh);
            }
            _modelInstance = null;
            _loadedSource = null;
            _materials.Clear();
            _wireMeshes.Clear();
            _surfaceRenderers.Clear();
            _wireRenderers.Clear();
        }

        private void OnDisable()
        {
            if (Application.isPlaying) ClearModel();
        }

        private void OnDestroy() => ClearModel();

        private static bool TryRentModel(GameObject source, out PooledModel pooled)
        {
            if (ModelPool.TryGetValue(source, out Stack<PooledModel> models))
            {
                while (models.Count > 0)
                {
                    pooled = models.Pop();
                    if (pooled.Instance != null) return true;
                }
            }

            pooled = default;
            return false;
        }

        private static void ReturnModel(GameObject source, PooledModel pooled)
        {
            pooled.Instance.SetActive(false);
            pooled.Instance.transform.SetParent(GetPoolRoot(), false);

            if (!ModelPool.TryGetValue(source, out Stack<PooledModel> models))
            {
                models = new Stack<PooledModel>();
                ModelPool.Add(source, models);
            }

            if (models.Count < MaxPooledModelsPerSource)
                models.Push(pooled);
            else
                DestroyPooledModel(pooled);
        }

        private static Transform GetPoolRoot()
        {
            if (_poolRoot != null) return _poolRoot;

            var root = new GameObject("Model Layer Pool")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            DontDestroyOnLoad(root);
            _poolRoot = root.transform;
            return _poolRoot;
        }

        private static void ResetModelRuntime(GameObject instance)
        {
            foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
            {
                animator.Rebind();
                animator.Update(0f);
            }

            foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static void DestroyPooledModel(PooledModel pooled)
        {
            if (pooled.Instance != null) DestroyRuntimeObject(pooled.Instance);
            foreach (Material material in pooled.Materials) DestroyRuntimeObject(material);
            foreach (Mesh mesh in pooled.WireMeshes) DestroyRuntimeObject(mesh);
        }

        private readonly struct PooledModel
        {
            public readonly GameObject Instance;
            public readonly Renderer[] SurfaceRenderers;
            public readonly Renderer[] WireRenderers;
            public readonly Material[] Materials;
            public readonly Mesh[] WireMeshes;

            public PooledModel(
                GameObject instance,
                Renderer[] surfaceRenderers,
                Renderer[] wireRenderers,
                Material[] materials,
                Mesh[] wireMeshes)
            {
                Instance = instance;
                SurfaceRenderers = surfaceRenderers;
                WireRenderers = wireRenderers;
                Materials = materials;
                WireMeshes = wireMeshes;
            }
        }

        private static void DestroyRuntimeObject(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
