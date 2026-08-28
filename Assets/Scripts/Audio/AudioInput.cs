using System;
using System.Collections.Generic;
using Lasp;
using RosettaUI;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// LASPからモノラル波形を取得し、SpectrumAnalyzerのFFT結果とともに公開する。
    /// 入力デバイスが見つかった時点でSpectrumAnalyzerを生成する。
    /// Analyzerより先に接続状態を確認し、切断中にLASPがnullストリームへアクセスするのを防ぐ。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class AudioInput : MonoBehaviour, IAudioInput, ISaveAndUiTarget
    {
        private const int GraphWidth = 360;
        private const int WaveformGraphHeight = 96;
        private const int SpectrumGraphHeight = 128;
        private const float GraphUpdateInterval = 1f / 30f;

        private static readonly Color32 GraphBackground = new(8, 12, 18, 255);
        private static readonly Color32 GraphGrid = new(34, 45, 58, 255);
        private static readonly Color32 WaveformColor = new(63, 220, 255, 255);
        private static readonly Color32 SpectrumColor = new(255, 174, 58, 255);
        private static readonly Color32 SpectrumFillColor = new(72, 46, 18, 255);

        public bool IsConnected => _stream != null && _stream.IsValid;
        public int SampleRate => IsConnected ? _stream.SampleRate : 0;
        public int Channel => _activeChannel;
        public float RmsLevel { get; private set; }
        public float PeakLevel { get; private set; }
        public ReadOnlySpan<float> Waveform => _waveform;
        public ReadOnlySpan<float> Spectrum => _spectrum;
        public ReadOnlySpan<float> LogSpectrum => _logSpectrum;
        public IParams Params => _params;

        [SerializeField] private AudioInputParams _params = new();

        private SpectrumAnalyzer _analyzer;
        private InputStream _stream;
        private int _activeChannel;
        private float[] _waveform = Array.Empty<float>();
        private float[] _spectrum = Array.Empty<float>();
        private float[] _logSpectrum = Array.Empty<float>();

        private bool _configuredUseDefaultDevice;
        private string _configuredDeviceId;
        private int _configuredResolution;
        private bool _hasWarnedRestartRequired;
        private bool _isAnalyzerConfigured;
        private readonly List<string> _deviceNames = new();
        private readonly List<string> _deviceIds = new();
        private int _deviceListVersion;
        private Texture2D _waveformTexture;
        private Texture2D _spectrumTexture;
        private Color32[] _waveformPixels;
        private Color32[] _spectrumPixels;
        private float _nextGraphUpdateTime;

        private void Awake()
        {
            // 旧構成で既に付いているAnalyzerも、入力確認が終わるまでは動かさない。
            _analyzer = GetComponent<SpectrumAnalyzer>();
            if (_analyzer != null) _analyzer.enabled = false;

            CacheInputConfiguration();
            TryConnect();
        }

        private void OnEnable()
        {
            if (_stream == null || !_stream.IsValid) TryConnect();
        }

        private void OnDisable()
        {
            if (_analyzer != null) _analyzer.enabled = false;
        }

        private void OnDestroy()
        {
            if (_waveformTexture != null) Destroy(_waveformTexture);
            if (_spectrumTexture != null) Destroy(_spectrumTexture);
        }

        private void Update()
        {
            if (_stream == null || !_stream.IsValid)
            {
                if (_analyzer != null) _analyzer.enabled = false;
                ClearOutput();
                if (!TryConnect()) return;
            }

            ApplyRuntimeSettings();
            CopyWaveform();
            if (_analyzer != null) CopySpectrum();
            UpdateMonitorGraphs();
        }

        public float GetFrequency(int spectrumIndex)
        {
            if (SampleRate == 0 || spectrumIndex < 0 || spectrumIndex >= _spectrum.Length) return 0f;
            return spectrumIndex * SampleRate / (2f * _spectrum.Length);
        }

        private void CacheInputConfiguration()
        {
            _configuredUseDefaultDevice = _params.UseDefaultDevice;
            _configuredDeviceId = _params.DeviceId;
            _configuredResolution = ValidateResolution(_params.SpectrumResolution);
            _params.SpectrumResolution = _configuredResolution;
        }

        private bool TryConnect()
        {
            _stream = GetInputStream();
            if (_stream == null || !_stream.IsValid) return false;

            UpdateActiveChannel();
            CreateAndConfigureAnalyzer();
            return true;
        }

        private void CreateAndConfigureAnalyzer()
        {
            if (_analyzer == null) _analyzer = gameObject.AddComponent<SpectrumAnalyzer>();
            _analyzer.enabled = false;

            if (!_isAnalyzerConfigured)
            {
                if (_configuredUseDefaultDevice)
                    _analyzer.useDefaultDevice = true;
                else
                    _analyzer.deviceID = _configuredDeviceId;

                _analyzer.resolution = _configuredResolution;
                _isAnalyzerConfigured = true;
            }

            ApplyAnalyzerSettings();
            _analyzer.enabled = true;
        }

        private InputStream GetInputStream() => _configuredUseDefaultDevice
            ? AudioSystem.GetDefaultInputStream()
            : AudioSystem.GetInputStream(_configuredDeviceId);

        private void ApplyRuntimeSettings()
        {
            UpdateActiveChannel();
            ApplyAnalyzerSettings();

            bool inputConfigurationChanged =
                _params.UseDefaultDevice != _configuredUseDefaultDevice ||
                _params.DeviceId != _configuredDeviceId ||
                ValidateResolution(_params.SpectrumResolution) != _configuredResolution;

            if (inputConfigurationChanged && !_hasWarnedRestartRequired)
            {
                Debug.LogWarning(
                    "Audio input device or spectrum resolution changed. Restart the scene to apply it.",
                    this);
                _hasWarnedRestartRequired = true;
            }
            else if (!inputConfigurationChanged)
            {
                _hasWarnedRestartRequired = false;
            }
        }

        private void SelectDevice(int index)
        {
            if (index < 0 || index >= _deviceIds.Count) return;

            string deviceId = _deviceIds[index];
            _params.UseDefaultDevice = string.IsNullOrEmpty(deviceId);
            _params.DeviceId = deviceId;
            ReconfigureInput();
        }

        private void ReconfigureInput()
        {
            if (_analyzer != null)
            {
                _analyzer.enabled = false;
                Destroy(_analyzer);
                _analyzer = null;
            }

            _isAnalyzerConfigured = false;
            _stream = null;
            ClearOutput();
            CacheInputConfiguration();
            TryConnect();
        }

        private void ApplyAnalyzerSettings()
        {
            if (_analyzer == null) return;

            _analyzer.channel = _activeChannel;
            _analyzer.autoGain = _params.AutoGain;
            _analyzer.gain = _params.Gain;
            _analyzer.dynamicRange = Mathf.Clamp(_params.DynamicRange, 1f, 120f);
        }

        private void UpdateActiveChannel()
        {
            int maxChannel = IsConnected ? Mathf.Max(0, _stream.ChannelCount - 1) : 0;
            _activeChannel = Mathf.Clamp(_params.Channel, 0, maxChannel);
        }

        private void CopyWaveform()
        {
            var source = _stream.GetChannelDataSlice(_activeChannel);
            EnsureSize(ref _waveform, source.Length);

            double squareSum = 0;
            float peak = 0f;
            for (int i = 0; i < source.Length; i++)
            {
                float sample = source[i];
                _waveform[i] = sample;
                squareSum += sample * sample;
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }

            RmsLevel = source.Length > 0 ? Mathf.Sqrt((float)(squareSum / source.Length)) : 0f;
            PeakLevel = peak;
        }

        private void CopySpectrum()
        {
            CopySpan(_analyzer.spectrumSpan, ref _spectrum);
            CopySpan(_analyzer.logSpectrumSpan, ref _logSpectrum);
        }

        private static void CopySpan(ReadOnlySpan<float> source, ref float[] destination)
        {
            EnsureSize(ref destination, source.Length);
            source.CopyTo(destination);
        }

        private static void EnsureSize(ref float[] buffer, int size)
        {
            if (buffer.Length != size) buffer = new float[size];
        }

        private void ClearOutput()
        {
            bool hadOutput = _waveform.Length > 0 || _spectrum.Length > 0 || _logSpectrum.Length > 0;
            _waveform = Array.Empty<float>();
            _spectrum = Array.Empty<float>();
            _logSpectrum = Array.Empty<float>();
            RmsLevel = 0f;
            PeakLevel = 0f;
            if (hadOutput) ClearMonitorGraphs();
        }

        private static int ValidateResolution(int resolution)
        {
            resolution = Mathf.Max(2, resolution);
            return Mathf.ClosestPowerOfTwo(resolution);
        }

        public Element AdditiveUi()
        {
            InitializeMonitorGraphs();

            return UI.Fold("Audio Monitor",
                UI.Row(
                    UI.DynamicElementOnStatusChanged(
                        () => _deviceListVersion,
                        _ => CreateDeviceDropdown()),
                    UI.Button("Refresh", () => _deviceListVersion++)
                ),
                UI.Label(() => IsConnected
                    ? $"Connected : {SampleRate} Hz / channel {Channel + 1}"
                    : "Audio input is not connected"),
                UI.SliderReadOnly("RMS", () => RmsLevel, 0f, 1f),
                UI.SliderReadOnly("Peak", () => PeakLevel, 0f, 1f),
                UI.Label(() => $"Waveform ({_waveform.Length} samples)"),
                UI.Image(() => _waveformTexture)
                    .SetWidth(GraphWidth)
                    .SetHeight(WaveformGraphHeight),
                UI.Label(() => $"Spectrum ({_spectrum.Length} bins / 0–{SampleRate / 2f:F0} Hz, log scale)"),
                UI.Image(() => _spectrumTexture)
                    .SetWidth(GraphWidth)
                    .SetHeight(SpectrumGraphHeight)
            ).SetWidth(400f);
        }

        private void InitializeMonitorGraphs()
        {
            if (_waveformTexture != null) return;

            _waveformPixels = new Color32[GraphWidth * WaveformGraphHeight];
            _spectrumPixels = new Color32[GraphWidth * SpectrumGraphHeight];
            _waveformTexture = CreateGraphTexture(WaveformGraphHeight, "Audio Waveform Monitor");
            _spectrumTexture = CreateGraphTexture(SpectrumGraphHeight, "Audio Spectrum Monitor");
            ClearMonitorGraphs();
        }

        private static Texture2D CreateGraphTexture(int height, string textureName)
        {
            return new Texture2D(GraphWidth, height, TextureFormat.RGBA32, false, true)
            {
                name = textureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
        }

        private void UpdateMonitorGraphs()
        {
            if (_waveformTexture == null || Time.unscaledTime < _nextGraphUpdateTime) return;
            _nextGraphUpdateTime = Time.unscaledTime + GraphUpdateInterval;

            DrawWaveform();
            DrawSpectrum();
        }

        private void DrawWaveform()
        {
            FillPixels(_waveformPixels, GraphBackground);
            DrawHorizontalGrid(_waveformPixels, WaveformGraphHeight, 4);

            if (_waveform.Length > 0)
            {
                int previousY = SampleToY(_waveform[0], WaveformGraphHeight);
                for (int x = 1; x < GraphWidth; x++)
                {
                    int sampleIndex = x * (_waveform.Length - 1) / (GraphWidth - 1);
                    int y = SampleToY(_waveform[sampleIndex], WaveformGraphHeight);
                    DrawLine(_waveformPixels, WaveformGraphHeight, x - 1, previousY, x, y, WaveformColor);
                    previousY = y;
                }
            }

            _waveformTexture.SetPixels32(_waveformPixels);
            _waveformTexture.Apply(false);
        }

        private void DrawSpectrum()
        {
            FillPixels(_spectrumPixels, GraphBackground);
            DrawHorizontalGrid(_spectrumPixels, SpectrumGraphHeight, 4);

            if (_logSpectrum.Length > 0)
            {
                int previousY = 0;
                for (int x = 0; x < GraphWidth; x++)
                {
                    int from = x * _logSpectrum.Length / GraphWidth;
                    int to = Mathf.Max(from + 1, (x + 1) * _logSpectrum.Length / GraphWidth);
                    float value = 0f;
                    for (int i = from; i < to && i < _logSpectrum.Length; i++)
                        value = Mathf.Max(value, _logSpectrum[i]);

                    int y = Mathf.RoundToInt(Mathf.Clamp01(value) * (SpectrumGraphHeight - 1));
                    DrawVerticalLine(_spectrumPixels, SpectrumGraphHeight, x, 0, y, SpectrumFillColor);
                    if (x > 0)
                        DrawLine(_spectrumPixels, SpectrumGraphHeight, x - 1, previousY, x, y, SpectrumColor);
                    previousY = y;
                }
            }

            _spectrumTexture.SetPixels32(_spectrumPixels);
            _spectrumTexture.Apply(false);
        }

        private void ClearMonitorGraphs()
        {
            if (_waveformTexture == null) return;

            FillPixels(_waveformPixels, GraphBackground);
            FillPixels(_spectrumPixels, GraphBackground);
            DrawHorizontalGrid(_waveformPixels, WaveformGraphHeight, 4);
            DrawHorizontalGrid(_spectrumPixels, SpectrumGraphHeight, 4);
            _waveformTexture.SetPixels32(_waveformPixels);
            _spectrumTexture.SetPixels32(_spectrumPixels);
            _waveformTexture.Apply(false);
            _spectrumTexture.Apply(false);
        }

        private static int SampleToY(float sample, int height) =>
            Mathf.RoundToInt((Mathf.Clamp(sample, -1f, 1f) * 0.5f + 0.5f) * (height - 1));

        private static void FillPixels(Color32[] pixels, Color32 color)
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        }

        private static void DrawHorizontalGrid(Color32[] pixels, int height, int divisionCount)
        {
            for (int division = 0; division <= divisionCount; division++)
            {
                int y = division * (height - 1) / divisionCount;
                DrawVerticalOrHorizontalLine(pixels, height, 0, y, GraphWidth - 1, y, GraphGrid);
            }
        }

        private static void DrawVerticalLine(
            Color32[] pixels, int height, int x, int fromY, int toY, Color32 color) =>
            DrawVerticalOrHorizontalLine(pixels, height, x, fromY, x, toY, color);

        private static void DrawVerticalOrHorizontalLine(
            Color32[] pixels, int height, int fromX, int fromY, int toX, int toY, Color32 color)
        {
            if (fromX == toX)
            {
                for (int y = Mathf.Max(0, fromY); y <= Mathf.Min(height - 1, toY); y++)
                    pixels[y * GraphWidth + fromX] = color;
                return;
            }

            for (int x = Mathf.Max(0, fromX); x <= Mathf.Min(GraphWidth - 1, toX); x++)
                pixels[fromY * GraphWidth + x] = color;
        }

        private static void DrawLine(
            Color32[] pixels, int height, int x0, int y0, int x1, int y1, Color32 color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int error = dx - dy;

            while (true)
            {
                if (x0 >= 0 && x0 < GraphWidth && y0 >= 0 && y0 < height)
                    pixels[y0 * GraphWidth + x0] = color;
                if (x0 == x1 && y0 == y1) break;

                int doubleError = error * 2;
                if (doubleError > -dy)
                {
                    error -= dy;
                    x0 += sx;
                }
                if (doubleError < dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        private Element CreateDeviceDropdown()
        {
            RefreshDeviceList();
            return UI.Dropdown("Device", GetSelectedDeviceIndex, SelectDevice, _deviceNames)
                .SetWidth(310f);
        }

        private void RefreshDeviceList()
        {
            _deviceNames.Clear();
            _deviceIds.Clear();
            _deviceNames.Add("System Default");
            _deviceIds.Add(string.Empty);

            try
            {
                foreach (DeviceDescriptor device in AudioSystem.InputDevices)
                {
                    if (!device.IsValid) continue;

                    _deviceNames.Add($"{device.Name} ({device.ChannelCount} ch / {device.SampleRate} Hz)");
                    _deviceIds.Add(device.ID);
                }

                if (!_params.UseDefaultDevice && !_deviceIds.Contains(_params.DeviceId))
                {
                    _deviceNames.Add($"Unavailable ({_params.DeviceId})");
                    _deviceIds.Add(_params.DeviceId);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to enumerate audio input devices.\n{exception}", this);
            }
        }

        private int GetSelectedDeviceIndex()
        {
            if (_params.UseDefaultDevice) return 0;

            int index = _deviceIds.IndexOf(_params.DeviceId);
            return index >= 0 ? index : 0;
        }
    }
}
