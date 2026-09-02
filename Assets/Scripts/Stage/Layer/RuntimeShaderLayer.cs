using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityRuntimeShader;
using UnitySimpleContainer;

namespace Aetherin
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class RuntimeShaderLayer : StageLayer
    {
        private static readonly int AetherinTimeId = Shader.PropertyToID("_AetherinTime");
        private static readonly int AetherinFrameId = Shader.PropertyToID("_AetherinFrame");
        private static readonly int AetherinResolutionId = Shader.PropertyToID("_AetherinResolution");
        private static readonly int AetherinQuadId = Shader.PropertyToID("_AetherinQuad");
        private static readonly int AetherinAudioId = Shader.PropertyToID("_AetherinAudio");
        private static readonly int AetherinBeatId = Shader.PropertyToID("_AetherinBeat");
        private static readonly int AetherinBarId = Shader.PropertyToID("_AetherinBar");
        private static readonly int WaveformTexId = Shader.PropertyToID("_AetherinWaveformTex");
        private static readonly int SpectrumTexId = Shader.PropertyToID("_AetherinSpectrumTex");
        private static readonly int OpacityId = Shader.PropertyToID("_AetherinOpacity");
        private static readonly int[] UserFloatIds = CreatePropertyIds("_UserFloat");
        private static readonly int[] UserVectorIds = CreatePropertyIds("_UserVector");

        [SerializeField] private RuntimeShaderLayerParams _params = new();

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _material;
        private ShaderRenderer _runtimeRenderer;
        private RenderTexture _runtimeTexture;
        private Vector2Int _runtimeResolution;
        private bool _hasCompiled;
        private bool _compileTaskRunning;
        private IAudioFeatureProvider _audio;
        private IBeatManager _beat;
        private IDeckStateProvider _deckStateProvider;
        private StageBase _stage;

        public override IParams Params => _params;
        protected override StageLayerParams LayerParams => _params;
        protected override Renderer LayerRenderer => RendererComponent;

        private MeshRenderer RendererComponent =>
            _meshRenderer != null ? _meshRenderer : _meshRenderer = GetComponent<MeshRenderer>();

        [Inject]
        private void Construct(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckStateProvider) =>
            Initialize(audio, beat, deckStateProvider);

        public void Initialize(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckStateProvider)
        {
            _audio = audio;
            _beat = beat;
            _deckStateProvider = deckStateProvider;
            _stage = GetComponentInParent<StageBase>();
            EnsureResources();
        }

        private void Awake() => InitializeLayer();
        private void OnEnable() => InitializeLayer();

        private void InitializeLayer()
        {
            _params ??= new RuntimeShaderLayerParams();
            _params.EnsureInitialized();
            _params.CompileRequested = CompileRuntimeShader;
            _stage = GetComponentInParent<StageBase>();
            EnsureResources();
            ApplyLayerState();
        }

        private void Update()
        {
            _params.EnsureInitialized();
            EnsureResources();
            if (_material == null) return;

            bool runtime = Application.isPlaying;
            double time = runtime ? Time.timeAsDouble : 0d;
            var context = new ModulationContext(time, runtime ? _audio : null, runtime ? _beat : null,
                runtime && (_stage == null || _stage.Deck == StageDeck.Next));

            Vector3 position = _params.Position.Evaluate(context);
            Vector3 rotation = _params.Rotation.Evaluate(context);
            Vector3 scale = _params.Scale.Evaluate(context);
            Vector3 anchor = _params.Anchor.Evaluate(context);
            Vector2 size = _params.Size.Evaluate(context);
            size.x = Mathf.Max(0f, size.x);
            size.y = Mathf.Max(0f, size.y);
            Quaternion orientation = Quaternion.Euler(
                Mathf.Repeat(rotation.x, 360f), Mathf.Repeat(rotation.y, 360f), Mathf.Repeat(rotation.z, 360f));
            transform.localPosition = position - orientation * Vector3.Scale(anchor, scale);
            transform.localRotation = orientation;
            transform.localScale = new Vector3(size.x * scale.x, size.y * scale.y, scale.z);

            float opacity = Mathf.Clamp01(_params.Opacity.Evaluate(context));
            Vector2Int resolution = GetResolution();
            float width = Mathf.Max(1, resolution.x);
            float height = Mathf.Max(1, resolution.y);
            _material.SetVector(AetherinTimeId, new Vector4((float)time, Time.deltaTime, Mathf.Sin((float)time), Mathf.Cos((float)time)));
            _material.SetVector(AetherinFrameId, new Vector4(Time.frameCount, Time.timeScale, Time.unscaledTime, Time.unscaledDeltaTime));
            _material.SetVector(AetherinResolutionId, new Vector4(width, height, 1f / width, 1f / height));
            _material.SetVector(AetherinQuadId, new Vector4(size.x, size.y, size.y > 0f ? size.x / size.y : 0f, opacity));
            _material.SetVector(AetherinAudioId, new Vector4(_audio?.InputVolume ?? 0f, _audio?.Kick ?? 0f,
                _audio?.SnareClap ?? 0f, (_audio?.WasKick ?? false) || (_audio?.WasSnareClap ?? false) ? 1f : 0f));
            _material.SetVector(AetherinBeatId, new Vector4(_beat?.BeatPhase ?? 1f, _beat?.BeatCount ?? 0,
                _beat?.BeatInBar ?? 0, _beat?.WasBeat == true ? 1f : 0f));
            _material.SetVector(AetherinBarId, new Vector4(_beat?.BarPhase ?? 1f, _beat?.BarCount ?? 0,
                _beat?.BeatsPerBar ?? 4, _beat?.WasBar == true ? 1f : 0f));
            _material.SetTexture(WaveformTexId, _audio?.WaveformTexture ?? Texture2D.blackTexture);
            _material.SetTexture(SpectrumTexId, _audio?.SpectrumTexture ?? Texture2D.blackTexture);
            _material.SetFloat(OpacityId, opacity);
            SetUserParameters(context);
            RenderRuntimeShader(context, resolution);

            ColorPalette palette = _deckStateProvider?.GetState(_stage != null ? _stage.Deck : StageDeck.Next).Palette;
            palette?.ApplyToMaterial(_material);
            LayerMaterialUtility.ApplyBlendMode(_material, _params.BlendMode);
            ApplyLayerState();
        }

        private void EnsureResources()
        {
            if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
            if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
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
                    _material = new Material(shader)
                    {
                        name = $"{name} Runtime Shader Material",
                        hideFlags = HideFlags.DontSave,
                    };
                    _meshRenderer.sharedMaterial = _material;
                    _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    _meshRenderer.receiveShadows = false;
                }
            }

            if (_runtimeRenderer == null)
            {
                _runtimeRenderer = GetComponent<ShaderRenderer>() ?? gameObject.AddComponent<ShaderRenderer>();
                _runtimeRenderer.RenderEveryFrame = false;
            }

            EnsureRuntimeTexture(GetResolution());
            if (!_hasCompiled) CompileRuntimeShader();
        }

        private void EnsureRuntimeTexture(Vector2Int resolution)
        {
            resolution.x = Mathf.Max(1, resolution.x);
            resolution.y = Mathf.Max(1, resolution.y);
            if (_runtimeTexture != null && _runtimeResolution == resolution) return;

            var next = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32)
            {
                name = $"{name} Runtime Shader Output",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            next.Create();
            _runtimeRenderer.TargetTexture = next;
            _material?.SetTexture("_MainTex", next);

            if (_runtimeTexture != null) DestroyResource(_runtimeTexture);
            _runtimeTexture = next;
            _runtimeResolution = resolution;
        }

        private async void CompileRuntimeShader()
        {
            if (_runtimeRenderer == null)
            {
                _params.CompileMessage = "Renderer is not initialized";
                _params.LastCompileSucceeded = false;
                return;
            }

            if (_compileTaskRunning)
            {
                _params.CompileMessage = "Compile is already running";
                return;
            }

            _compileTaskRunning = true;
            // ネイティブコンパイラと描画は同じ内部状態を使うため、並行実行させない。
            _hasCompiled = false;
            _params.CompileMessage = "Compiling...";
            string code = _params.ShaderCode;
            var compileTask = Task.Run(() =>
            {
                try
                {
                    bool succeeded = _runtimeRenderer.CompileShaderFromString(code, out string error);
                    return (Succeeded: succeeded, Error: error, Exception: (System.Exception)null);
                }
                catch (System.Exception exception)
                {
                    return (Succeeded: false, Error: (string)null, Exception: exception);
                }
            });

            if (await Task.WhenAny(compileTask, Task.Delay(5000)) != compileTask)
                _params.CompileMessage = "Compile timeout (compiler is still running)";

            var result = await compileTask;
            _compileTaskRunning = false;
            if (result.Exception != null)
            {
                _params.LastCompileSucceeded = false;
                _params.CompileMessage = result.Exception.Message;
                return;
            }

            _hasCompiled = result.Succeeded;
            _params.LastCompileSucceeded = result.Succeeded;
            _params.CompileMessage = result.Succeeded ? "Compiled" : result.Error;
        }

        private void RenderRuntimeShader(in ModulationContext context, Vector2Int resolution)
        {
            if (_runtimeRenderer == null || !_hasCompiled) return;
            EnsureRuntimeTexture(resolution);

            var globals = new RuntimeShaderGlobals
            {
                Time = new Vector4((float)context.Time, Time.deltaTime,
                    Mathf.Sin((float)context.Time), Mathf.Cos((float)context.Time)),
                Frame = new Vector4(Time.frameCount, Time.timeScale, Time.unscaledTime, Time.unscaledDeltaTime),
                Resolution = new Vector4(resolution.x, resolution.y,
                    1f / Mathf.Max(1, resolution.x), 1f / Mathf.Max(1, resolution.y)),
                Audio = new Vector4(_audio?.InputVolume ?? 0f, _audio?.Kick ?? 0f,
                    _audio?.SnareClap ?? 0f, (_audio?.WasKick ?? false) || (_audio?.WasSnareClap ?? false) ? 1f : 0f),
                Beat = new Vector4(_beat?.BeatPhase ?? 1f, _beat?.BeatCount ?? 0,
                    _beat?.BeatInBar ?? 0, _beat?.WasBeat == true ? 1f : 0f),
                Bar = new Vector4(_beat?.BarPhase ?? 1f, _beat?.BarCount ?? 0,
                    _beat?.BeatsPerBar ?? 4, _beat?.WasBar == true ? 1f : 0f),
                UserFloat = new Vector4(_params.UserFloat0.Evaluate(context), _params.UserFloat1.Evaluate(context),
                    _params.UserFloat2.Evaluate(context), _params.UserFloat3.Evaluate(context)),
                UserVector0 = _params.UserVector0.Evaluate(context),
                UserVector1 = _params.UserVector1.Evaluate(context),
                UserVector2 = _params.UserVector2.Evaluate(context),
                UserVector3 = _params.UserVector3.Evaluate(context),
            };
            _runtimeRenderer.SetConstantBuffer(0, globals);
            _runtimeRenderer.SetTexture(0, _audio?.WaveformTexture ?? Texture2D.blackTexture);
            _runtimeRenderer.SetTexture(1, _audio?.SpectrumTexture ?? Texture2D.blackTexture);
            _runtimeRenderer.BlitNow();
        }

        private void SetUserParameters(in ModulationContext context)
        {
            FloatParameter[] floats = { _params.UserFloat0, _params.UserFloat1, _params.UserFloat2, _params.UserFloat3 };
            Vector3Parameter[] vectors = { _params.UserVector0, _params.UserVector1, _params.UserVector2, _params.UserVector3 };
            for (int i = 0; i < 4; i++)
            {
                _material.SetFloat(UserFloatIds[i], floats[i].Evaluate(context));
                _material.SetVector(UserVectorIds[i], vectors[i].Evaluate(context));
            }
        }

        private Vector2Int GetResolution()
        {
            RenderTexture texture = _stage != null ? _stage.OutputTexture : null;
            return texture != null ? new Vector2Int(texture.width, texture.height) : new Vector2Int(Screen.width, Screen.height);
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "Runtime Shader Quad", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) });
            mesh.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int[] CreatePropertyIds(string prefix) => new[]
        {
            Shader.PropertyToID(prefix + "0"), Shader.PropertyToID(prefix + "1"),
            Shader.PropertyToID(prefix + "2"), Shader.PropertyToID(prefix + "3"),
        };

        private void OnDestroy()
        {
            if (_params != null) _params.CompileRequested = null;
            DestroyResource(_material);
            DestroyResource(_mesh);
            DestroyResource(_runtimeTexture);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RuntimeShaderGlobals
        {
            public Vector4 Time;
            public Vector4 Frame;
            public Vector4 Resolution;
            public Vector4 Audio;
            public Vector4 Beat;
            public Vector4 Bar;
            public Vector4 UserFloat;
            public Vector4 UserVector0;
            public Vector4 UserVector1;
            public Vector4 UserVector2;
            public Vector4 UserVector3;
        }

        private static void DestroyResource(Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
    }
}
