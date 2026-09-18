using System.Collections.Generic;
using System.IO;
using Klak.Hap;
using UnityEngine;
using UnityEngine.Rendering;
using UnitySimpleContainer;

namespace Aetherin
{
    /// <summary>CameraWorkの切り替えに同期して、パス一覧のHAP動画を順番に再生する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class MovieLayer : StageLayer
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int LutTexId = Shader.PropertyToID("_LutTex");
        private static readonly int LutParamsId = Shader.PropertyToID("_LutParams");
        private static readonly int LutEnabledId = Shader.PropertyToID("_LutEnabled");
        private static readonly int LutIntensityId = Shader.PropertyToID("_LutIntensity");

        [SerializeField] private MovieLayerParams _params = new() { BlendMode = LayerBlendMode.Opaque };
        [SerializeField] private Shader _shader;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _material;
        private RenderTexture _videoTexture;
        private HapPlayer _player;
        private CameraStage _cameraStage;
        private IAudioFeatureProvider _audio;
        private IBeatManager _beat;
        private IDeckStateProvider _deckStateProvider;
        private StageBase _stage;
        private int _loadedCameraWork = int.MinValue;
        private string _loadedPath;
        private MoviePathMode _loadedPathMode;
        private int _stageTimeRevision = -1;
        private readonly List<string> _directoryMoviePaths = new();
        private int _directoryMovieIndex = -1;

        public override IParams Params => _params;
        protected override StageLayerParams LayerParams => _params;
        protected override Renderer LayerRenderer => _meshRenderer;

        [Inject]
        private void Construct(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckStateProvider) =>
            Initialize(audio, beat, deckStateProvider);

        public void Initialize(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckStateProvider)
        {
            _audio = audio;
            _beat = beat;
            _deckStateProvider = deckStateProvider;
            InitializeLayer();
        }

        private void Awake() => InitializeLayer();
        private void OnEnable() => InitializeLayer();

        private void InitializeLayer()
        {
            _params ??= new MovieLayerParams { BlendMode = LayerBlendMode.Opaque };
            _params.EnsureInitialized();
            _cameraStage = GetComponentInParent<CameraStage>();
            _stage = GetComponentInParent<StageBase>();
            _params.GetAvailableLutKeys = _cameraStage != null ? _cameraStage.GetLutKeys : null;
            EnsureLutKey();
            EnsureResources();
            ApplyLayerState();
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            _params.EnsureInitialized();
            EnsureLutKey();
            EnsureResources();
            SynchronizeMovie();
            if (_material == null) return;

            var context = CreateModulationContext(
                Application.isPlaying ? Time.unscaledTimeAsDouble : Time.realtimeSinceStartupAsDouble,
                Application.isPlaying ? _audio : null, Application.isPlaying ? _beat : null,
                Application.isPlaying && (_stage == null || (_deckStateProvider?.IsDeckEditable(_stage.Deck) ?? _stage.Deck == StageDeck.Next)));
            ApplyTransform(context);
            _material.SetTexture(MainTexId, _videoTexture != null ? _videoTexture : Texture2D.blackTexture);
            _material.SetColor(ColorId, new Color(1f, 1f, 1f, Mathf.Clamp01(_params.Opacity.Evaluate(context))));
            ApplyLut(context);
            LayerMaterialUtility.ApplyBlendMode(_material, _params.BlendMode);
        }

        private void EnsureLutKey()
        {
            if (!_params.LutEnabled || !string.IsNullOrWhiteSpace(_params.LutKey) || _cameraStage == null) return;
            var keys = _cameraStage.GetLutKeys();
            if (keys != null && keys.Count > 0) _params.LutKey = keys[0];
        }

        private void ApplyLut(in ModulationContext context)
        {
            Texture2D lut = _params.LutEnabled ? _cameraStage?.ResolveLut(_params.LutKey) : null;
            bool horizontal = lut != null && lut.width == lut.height * lut.height;
            bool vertical = lut != null && lut.height == lut.width * lut.width;
            if (!horizontal && !vertical)
            {
                _material.SetFloat(LutEnabledId, 0f);
                return;
            }

            float size = horizontal ? lut.height : lut.width;
            _material.SetTexture(LutTexId, lut);
            _material.SetVector(LutParamsId, new Vector4(size, vertical ? 1f : 0f, 1f / lut.width, 1f / lut.height));
            _material.SetFloat(LutIntensityId, Mathf.Clamp01(_params.LutIntensity.Evaluate(context)));
            _material.SetFloat(LutEnabledId, 1f);
        }

        protected override void OnValidate()
        {
            _loadedCameraWork = int.MinValue;
            InitializeLayer();
            base.OnValidate();
        }

        private void EnsureResources()
        {
            _meshFilter ??= GetComponent<MeshFilter>();
            _meshRenderer ??= GetComponent<MeshRenderer>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Movie Quad", hideFlags = HideFlags.DontSave };
                _mesh.SetVertices(new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) });
                _mesh.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
                _mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                _mesh.RecalculateBounds();
                _meshFilter.sharedMesh = _mesh;
            }
            if (_material != null) return;
            _shader ??= Shader.Find("Aetherin/Movie Billboard");
            if (_shader == null) return;
            _material = new Material(_shader) { name = $"{name} Movie Material", hideFlags = HideFlags.DontSave };
            _meshRenderer.sharedMaterial = _material;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
        }

        private void SynchronizeMovie()
        {
            if (!Application.isPlaying || _cameraStage == null) return;
            int cameraWork = _cameraStage.CurrentCameraWork;
            string path = ResolvePath(cameraWork);
            bool sourceUnchanged = _loadedPath == path && _loadedPathMode == _params.PathMode;
            if (sourceUnchanged && _loadedCameraWork != cameraWork && IsDirectoryPlaylist)
            {
                _loadedCameraWork = cameraWork;
                AdvanceDirectoryMovie();
                return;
            }
            if (_loadedCameraWork == cameraWork && _loadedPath == path && _loadedPathMode == _params.PathMode)
            {
                if (_player != null)
                {
                    _player.loop = !IsDirectoryPlaylist || _params.Loop && _directoryMoviePaths.Count == 1;
                    StageBase stage = GetComponentInParent<StageBase>();
                    _player.speed = _params.PlaybackSpeed * (stage != null ? stage.StageTimeSpeed : 1f);
                    if (stage != null && _stageTimeRevision != stage.StageTimeRevision)
                    {
                        _stageTimeRevision = stage.StageTimeRevision;
                        _player.time = 0f;
                    }

                    if (IsDirectoryPlaylist && _player.speed > 0f && _player.time >= _player.streamDuration)
                        AdvanceDirectoryMovie();
                }
                return;
            }
            _loadedCameraWork = cameraWork;
            _loadedPath = path;
            _loadedPathMode = _params.PathMode;
            OpenMoviePath(path);
        }

        private string ResolvePath(int cameraWork)
        {
            if (_params.VideoPaths == null || _params.VideoPaths.Count == 0) return null;
            int index = ((cameraWork % _params.VideoPaths.Count) + _params.VideoPaths.Count) % _params.VideoPaths.Count;
            return string.IsNullOrWhiteSpace(_params.VideoPaths[index]) ? null : _params.VideoPaths[index].Trim();
        }

        private bool IsDirectoryPlaylist => _directoryMovieIndex >= 0;

        private void OpenMoviePath(string path)
        {
            _directoryMoviePaths.Clear();
            _directoryMovieIndex = -1;
            if (string.IsNullOrEmpty(path))
            {
                OpenMovie(null, false);
                return;
            }

            string directoryPath = _params.PathMode == MoviePathMode.StreamingAssets
                ? Path.Combine(Application.streamingAssetsPath, path)
                : path;
            if (Directory.Exists(directoryPath))
            {
                foreach (string filePath in Directory.GetFiles(directoryPath, "*.mov", SearchOption.TopDirectoryOnly))
                    _directoryMoviePaths.Add(_params.PathMode == MoviePathMode.StreamingAssets
                        ? Path.Combine(path, Path.GetFileName(filePath))
                        : filePath);
                _directoryMoviePaths.Sort(System.StringComparer.OrdinalIgnoreCase);
                if (_directoryMoviePaths.Count > 0)
                {
                    _directoryMovieIndex = 0;
                    OpenMovie(_directoryMoviePaths[_directoryMovieIndex], true);
                    return;
                }

                OpenMovie(null, false);
                return;
            }

            OpenMovie(path, false);
        }

        private void AdvanceDirectoryMovie()
        {
            if (_directoryMoviePaths.Count == 0) return;
            if (_directoryMovieIndex + 1 >= _directoryMoviePaths.Count)
            {
                if (!_params.Loop) return;
                _directoryMovieIndex = 0;
            }
            else
            {
                _directoryMovieIndex++;
            }
            OpenMovie(_directoryMoviePaths[_directoryMovieIndex], true);
        }

        private void OpenMovie(string path, bool directoryPlaylist)
        {
            ReleasePlayer();
            if (string.IsNullOrEmpty(path)) return;

            _player = gameObject.AddComponent<HapPlayer>();
            _player.loop = !directoryPlaylist || _params.Loop && _directoryMoviePaths.Count == 1;
            StageBase stage = GetComponentInParent<StageBase>();
            _player.speed = _params.PlaybackSpeed * (stage != null ? stage.StageTimeSpeed : 1f);
            _stageTimeRevision = stage != null ? stage.StageTimeRevision : -1;
            _player.Open(path, _params.PathMode == MoviePathMode.StreamingAssets
                ? HapPlayer.PathMode.StreamingAssets
                : HapPlayer.PathMode.LocalFileSystem);
            if (!_player.isValid) return;

            _videoTexture = new RenderTexture(
                Mathf.Max(1, _player.frameWidth), Mathf.Max(1, _player.frameHeight), 0, RenderTextureFormat.ARGB32)
            {
                name = $"{name} Movie Texture",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _videoTexture.Create();
            _player.targetTexture = _videoTexture;
            _player.UpdateNow();
        }

        private void ApplyTransform(in ModulationContext context)
        {
            Vector3 position = _params.Position.Evaluate(context);
            Vector3 scale = _params.Scale.Evaluate(context);
            Vector3 anchor = _params.Anchor.Evaluate(context);
            Vector2 size = _params.Size.Evaluate(context);
            if (_params.PreserveAspect && _videoTexture != null && _videoTexture.height > 0)
                size.x = size.y * _videoTexture.width / _videoTexture.height;
            size.x = Mathf.Max(0f, size.x);
            size.y = Mathf.Max(0f, size.y);

            Camera camera = _cameraStage != null ? _cameraStage.StageCamera : null;
            Vector3 forward = camera != null ? camera.transform.position - transform.position : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < .000001f) forward = Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(forward.normalized, Vector3.up) *
                                  Quaternion.Euler(_params.Rotation.Evaluate(context));
            transform.localPosition = position - Quaternion.Inverse(transform.parent != null ? transform.parent.rotation : Quaternion.identity) *
                rotation * Vector3.Scale(anchor, new Vector3(size.x * scale.x, size.y * scale.y, scale.z));
            transform.rotation = rotation;
            transform.localScale = new Vector3(size.x * scale.x, size.y * scale.y, scale.z);
        }

        private void ReleasePlayer()
        {
            if (_player != null)
            {
                _player.enabled = false;
                _player.targetTexture = null;
                DestroyResource(_player);
                _player = null;
            }
            if (_videoTexture != null)
            {
                _videoTexture.Release();
                DestroyResource(_videoTexture);
                _videoTexture = null;
            }
        }

        private void OnDestroy()
        {
            ReleasePlayer();
            if (_material != null) DestroyResource(_material);
            if (_mesh != null) DestroyResource(_mesh);
        }

        private static void DestroyResource(Object resource)
        {
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
    }
}
