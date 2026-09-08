using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityRuntimeShader;
using UnitySimpleContainer;

namespace Aetherin
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class RuntimeShaderLayer : StageLayer
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int OpacityId = Shader.PropertyToID("_AetherinOpacity");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");

        public override IParams Params => _params;
        protected override StageLayerParams LayerParams => _params;
        protected override Renderer LayerRenderer => RendererComponent;

        private MeshRenderer RendererComponent =>
            _meshRenderer != null ? _meshRenderer : _meshRenderer = GetComponent<MeshRenderer>();
        
        [SerializeField] private RuntimeShaderLayerParams _params = new();

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _material;
        private ShaderRenderer _runtimeRenderer;
        private RenderTexture _runtimeTexture;
        private RenderTexture _waveformTexture;
        private Vector2Int _runtimeResolution;
        private int _shaderCodeHash;
        private bool _isShaderCompiled;
        private bool _compileAttempted;
        private int _appliedTextureRebuildRevision = int.MinValue;

        private IAudioFeatureProvider _audio;
        private IBeatManager _beat;
        private IDeckStateProvider _deckStateProvider;
        private StageBase _stage;


        [Inject]
        private void Construct(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckStateProvider) =>
            Initialize(audio, beat, deckStateProvider);

        public void Initialize(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckStateProvider)
        {
            _audio = audio;
            _beat = beat;
            _deckStateProvider = deckStateProvider;
            _stage = GetComponentInParent<StageBase>();
        }

        private void Awake() => InitializeLayer();
        private void OnEnable() => InitializeLayer();

        private void InitializeLayer()
        {
            _params ??= new RuntimeShaderLayerParams();
            _params.EnsureInitialized();
            _stage = GetComponentInParent<StageBase>();
            EnsureResources();
            ApplyLayerState();
        }

        private void Update()
        {
            _params.EnsureInitialized();
            EnsureResources();
            if (_material == null || _runtimeRenderer == null) return;

            var context = CreateModulationContext(
                Application.isPlaying ? Time.unscaledTimeAsDouble : Time.realtimeSinceStartupAsDouble,
                Application.isPlaying ? _audio : null,
                Application.isPlaying ? _beat : null,
                Application.isPlaying && (_stage == null || _stage.Deck == StageDeck.Next));
            ApplyTransform(context);
            ApplyAppearance(context);

            // UnityRuntimeShader's native renderer only runs in Play Mode. Keeping the
            // preview quad alive in Edit Mode makes layer layout and ordering editable.
            if (!Application.isPlaying) return;

            CompileIfNeeded();
            if (!_isShaderCompiled) return;

            if (_runtimeTexture == null || _appliedTextureRebuildRevision != _params.TextureRebuildRevision)
            {
                EnsureRuntimeTexture(GetRequestedResolution(context));
                _appliedTextureRebuildRevision = _params.TextureRebuildRevision;
            }
            if (_runtimeTexture == null) return;
            _runtimeRenderer.SetConstantBuffer(0, CreateConstantBuffer(context, _runtimeResolution));
            _runtimeRenderer.SetTexture(0, GetWaveformTexture());
            _runtimeRenderer.SetTexture(1, _audio?.SpectrumTexture ?? Texture2D.blackTexture);
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!_params.ScreenSpace) return;

            var context = CreateModulationContext(
                Application.isPlaying ? Time.unscaledTimeAsDouble : Time.realtimeSinceStartupAsDouble,
                Application.isPlaying ? _audio : null,
                Application.isPlaying ? _beat : null,
                Application.isPlaying && (_stage == null || _stage.Deck == StageDeck.Next));
            ApplyTransform(context);
        }

        private void EnsureResources()
        {
            _meshFilter ??= GetComponent<MeshFilter>();
            _meshRenderer ??= GetComponent<MeshRenderer>();
            if (_mesh == null)
            {
                _mesh = CreateQuad();
                _meshFilter.sharedMesh = _mesh;
            }

            if (_material == null)
            {
                Shader shader = Shader.Find("Hidden/Aetherin/Runtime Shader Output");
                if (shader != null)
                {
                    _material = new Material(shader) { name = $"{name} Runtime Shader Material", hideFlags = HideFlags.DontSave };
                    _meshRenderer.sharedMaterial = _material;
                    _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    _meshRenderer.receiveShadows = false;
                }
            }

            // ShaderRenderer initializes a native DirectX compiler in Awake. Creating it
            // while a layer is added in Edit Mode can block the Unity Editor, so defer the
            // component entirely until the player is running.
            if (!Application.isPlaying)
            {
                _runtimeRenderer = GetComponent<ShaderRenderer>();
                if (_runtimeRenderer != null) _runtimeRenderer.enabled = false;
                return;
            }

            _runtimeRenderer ??= GetComponent<ShaderRenderer>() ?? gameObject.AddComponent<ShaderRenderer>();
            _runtimeRenderer.enabled = true;
            // UnityRuntimeShader queues this path through GL.IssuePluginEvent at the end
            // of the frame. Do not call BlitNow from Update: it accesses D3D11 directly
            // on the main thread and can race Unity's threaded graphics device.
            _runtimeRenderer.RenderEveryFrame = true;
        }

        private void CompileIfNeeded()
        {
            string code = _params.ShaderCode ?? RuntimeShaderLayerParams.DefaultShaderCode;
            int codeHash = code.GetHashCode();
            if (_compileAttempted && codeHash == _shaderCodeHash) return;

            _shaderCodeHash = codeHash;
            _compileAttempted = true;
            _params.CompileMessage = "Compiling...";
            _isShaderCompiled = _runtimeRenderer.CompileShaderFromString(code, out string error);
            _params.LastCompileSucceeded = _isShaderCompiled;
            _params.CompileMessage = _isShaderCompiled ? "Compiled" : error ?? "Unknown shader compilation error";
            if (!_isShaderCompiled)
                Debug.LogError($"[RuntimeShaderLayer] Shader compilation failed on '{name}': {error}", this);
        }

        private void EnsureRuntimeTexture(Vector2Int resolution)
        {
            resolution.x = Mathf.Max(1, resolution.x);
            resolution.y = Mathf.Max(1, resolution.y);
            if (_runtimeTexture != null && _runtimeResolution == resolution) return;

            var texture = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32)
            {
                name = $"{name} Runtime Shader Output", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            texture.Create();
            RenderTexture previousTexture = _runtimeTexture;
            _runtimeTexture = texture;
            _runtimeResolution = resolution;
            _runtimeRenderer.TargetTexture = texture;
            _material.SetTexture(MainTexId, texture);
            DestroyResource(previousTexture);
        }

        private void ApplyTransform(in ModulationContext context)
        {
            Vector3 position = _params.Position.Evaluate(context);
            Vector3 rotation = _params.Rotation.Evaluate(context);
            Vector3 scale = _params.Scale.Evaluate(context);
            Vector3 anchor = _params.Anchor.Evaluate(context);
            Vector2 size = _params.Size.Evaluate(context);
            Quaternion orientation = Quaternion.Euler(rotation);

            if (!_params.ScreenSpace)
            {
                transform.localPosition = position - orientation * Vector3.Scale(anchor, scale);
                transform.localRotation = orientation;
                transform.localScale = new Vector3(Mathf.Max(0f, size.x) * scale.x, Mathf.Max(0f, size.y) * scale.y, scale.z);
                return;
            }

            Camera camera = GetComponentInParent<CameraStage>()?.StageCamera;
            if (camera == null) return;
            float depth = camera.orthographic ? Mathf.Max(camera.nearClipPlane + .01f, 1f) : Mathf.Max(camera.nearClipPlane + .01f, 10f);
            float halfHeight = camera.orthographic ? camera.orthographicSize : depth * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            Vector3 cameraPosition = new(position.x * halfHeight, position.y * halfHeight, depth + position.z * halfHeight);
            transform.SetPositionAndRotation(camera.transform.TransformPoint(cameraPosition), camera.transform.rotation * orientation);
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            Vector3 desiredScale = new(Mathf.Max(0f, size.x) * scale.x * halfHeight, Mathf.Max(0f, size.y) * scale.y * halfHeight, scale.z * halfHeight);
            transform.localScale = new Vector3(DivideByParentScale(desiredScale.x, parentScale.x), DivideByParentScale(desiredScale.y, parentScale.y), DivideByParentScale(desiredScale.z, parentScale.z));
        }

        private void ApplyAppearance(in ModulationContext context)
        {
            if (_material == null) return;
            _material.SetFloat(OpacityId, Mathf.Clamp01(_params.Opacity.Evaluate(context)));
            _material.SetFloat(AlphaClipId, _params.BlendMode == LayerBlendMode.Opaque ? 1f : 0f);
            LayerMaterialUtility.ApplyBlendMode(_material, _params.BlendMode);
        }

        private RuntimeShaderConstant CreateConstantBuffer(in ModulationContext context, Vector2Int resolution)
        {
            ColorPalette palette = _deckStateProvider?.GetState(_stage != null ? _stage.Deck : StageDeck.Next).Palette ?? PaletteColorParameter.FallbackPalette;
            float time = (float)context.Time;
            return new RuntimeShaderConstant
            {
                Time = new Vector4(time, Time.unscaledDeltaTime, Mathf.Sin(time), Mathf.Cos(time)),
                Frame = new Vector4(Time.frameCount, Time.timeScale, Time.unscaledTime, Time.unscaledDeltaTime),
                Resolution = new Vector4(resolution.x, resolution.y, 1f / resolution.x, 1f / resolution.y),
                Audio = new Vector4(_audio?.InputVolume ?? 0f, _audio?.Kick ?? 0f, _audio?.SnareClap ?? 0f, _audio?.WasKick == true || _audio?.WasSnareClap == true ? 1f : 0f),
                Beat = new Vector4(_beat?.BeatPhase ?? 1f, _beat?.BeatCount ?? 0, _beat?.BeatInBar ?? 0, _beat?.WasBeat == true ? 1f : 0f),
                Bar = new Vector4(_beat?.BarPhase ?? 1f, _beat?.BarCount ?? 0, _beat?.BeatsPerBar ?? 4, _beat?.WasBar == true ? 1f : 0f),
                BackgroundColor1 = palette.BackgroundColor1.linear, BackgroundColor2 = palette.BackgroundColor2.linear,
                AccentColor1 = palette.AccentColor1.linear, AccentColor2 = palette.AccentColor2.linear,
                SubAccentColor1 = palette.SubAccentColor1.linear, SubAccentColor2 = palette.SubAccentColor2.linear,
                UserFloat = new Vector4(_params.UserFloat0.Evaluate(context), _params.UserFloat1.Evaluate(context), _params.UserFloat2.Evaluate(context), _params.UserFloat3.Evaluate(context)),
                UserVector0 = _params.UserVector0.Evaluate(context), UserVector1 = _params.UserVector1.Evaluate(context),
            };
        }

        private Vector2Int GetRequestedResolution(in ModulationContext context)
        {
            Vector2 size = _params.Size?.Evaluate(context) ?? Vector2.one;
            float pixelsPerUnit = Mathf.Max(1f, _params.PixelPerUnit);
            return new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(size.x) * pixelsPerUnit)),
                Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(size.y) * pixelsPerUnit)));
        }

        /// <summary>
        /// UnityRuntimeShader converts Texture2D inputs to a default (UNorm) RenderTexture.
        /// That conversion clamps the negative half of our RFloat waveform. Copying into an
        /// RFloat RenderTexture first preserves the -1..1 sample values and also makes the
        /// native plugin take its RenderTexture path directly.
        /// </summary>
        private Texture GetWaveformTexture()
        {
            Texture source = _audio?.WaveformTexture;
            if (source == null) return Texture2D.blackTexture;
            if (source is RenderTexture renderTexture) return renderTexture;

            int width = Mathf.Max(1, source.width);
            int height = Mathf.Max(1, source.height);
            if (_waveformTexture == null || _waveformTexture.width != width || _waveformTexture.height != height)
            {
                DestroyResource(_waveformTexture);
                _waveformTexture = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat)
                {
                    name = $"{name} Runtime Shader Waveform",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                _waveformTexture.Create();
            }

            Graphics.Blit(source, _waveformTexture);
            return _waveformTexture;
        }

        private static float DivideByParentScale(float value, float parentScale) => Mathf.Abs(parentScale) > .0001f ? value / parentScale : value;

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "Runtime Shader Quad", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) });
            mesh.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            DestroyResource(_material);
            DestroyResource(_mesh);
            DestroyResource(_runtimeTexture);
            DestroyResource(_waveformTexture);
        }

        private static void DestroyResource(Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RuntimeShaderConstant
        {
            public Vector4 Time;
            public Vector4 Frame;
            public Vector4 Resolution;
            public Vector4 Audio;
            public Vector4 Beat;
            public Vector4 Bar;
            public Vector4 BackgroundColor1;
            public Vector4 BackgroundColor2;
            public Vector4 AccentColor1;
            public Vector4 AccentColor2;
            public Vector4 SubAccentColor1;
            public Vector4 SubAccentColor2;
            public Vector4 UserFloat;
            public Vector4 UserVector0;
            public Vector4 UserVector1;
        }
    }
}
