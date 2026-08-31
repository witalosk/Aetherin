using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using RosettaUI;
using UnityEngine;

namespace Aetherin
{
    /// <summary>Windowsの再生エンドポイントをWASAPI Loopbackで直接キャプチャする。</summary>
    public sealed class WasapiLoopbackInput : MonoBehaviour, IAudioInput, IPercussiveOnsetSource, ISaveAndUiTarget
    {
        private const int RingCapacity = 32768;
        private const float GraphUpdateInterval = 1f / 30f;
        private const long SilenceTimeoutTicks = TimeSpan.TicksPerMillisecond * 250;

        public bool IsConnected { get; private set; }
        public int SampleRate { get; private set; }
        public int Channel => 0;
        public float RmsLevel { get; private set; }
        public float PeakLevel { get; private set; }
        public ReadOnlySpan<float> Waveform => _waveform;
        public ReadOnlySpan<float> Spectrum => _spectrum;
        public ReadOnlySpan<float> LogSpectrum => _logSpectrum;
        public bool IsHardRealtimeOnsetAvailable => Volatile.Read(ref _bridgeOnsetAvailable);
        public int KickOnsetSequence => Volatile.Read(ref _kickOnsetSequence);
        public int SnareClapOnsetSequence => Volatile.Read(ref _snareOnsetSequence);
        public float LatestKickStrength => Volatile.Read(ref _latestKickStrength);
        public float LatestSnareClapStrength => Volatile.Read(ref _latestSnareStrength);
        public long LatestKickSampleIndex => Interlocked.Read(ref _latestKickSampleIndex);
        public long LatestSnareClapSampleIndex => Interlocked.Read(ref _latestSnareSampleIndex);
        public IParams Params => _params;
        public string Category => UiCategory.Settings;

        [SerializeField] private WasapiLoopbackInputParams _params = new();

        private readonly object _sampleLock = new();
        private readonly float[] _ring = new float[RingCapacity];
        private int _ringWriteIndex;
        private int _ringCount;
        private long _lastDataTicks;
        private string _captureError;
        private int _kickOnsetSequence;
        private int _snareOnsetSequence;
        private float _latestKickStrength;
        private float _latestSnareStrength;
        private long _latestKickSampleIndex = -1;
        private long _latestSnareSampleIndex = -1;
        private bool _bridgeOnsetAvailable;

        private float[] _waveform = Array.Empty<float>();
        private float[] _fftReal = Array.Empty<float>();
        private float[] _fftImaginary = Array.Empty<float>();
        private float[] _spectrum = Array.Empty<float>();
        private float[] _logSpectrum = Array.Empty<float>();

        private readonly List<string> _deviceNames = new();
        private readonly List<string> _deviceIds = new();
        private int _deviceListVersion;
        private AudioGraphTextures _graphs;
        private float _nextGraphUpdateTime;
        private bool _hasAnalysis;
        private Coroutine _restartCoroutine;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private WasapiCapture _capture;
        private MMDevice _device;
        private System.Diagnostics.Process _bridgeProcess;
        private Thread _bridgeReaderThread;
        private volatile bool _bridgeStopping;
        private WaveFormat _bridgeFormat;
#endif

        private void OnEnable() => StartCapture();
        private void OnDisable()
        {
            if (_restartCoroutine != null)
            {
                StopCoroutine(_restartCoroutine);
                _restartCoroutine = null;
            }
            StopCapture();
        }

        private void OnDestroy()
        {
            StopCapture();
            _graphs?.Dispose();
        }

        private void Update()
        {
            if (!IsConnected) return;

            if (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastDataTicks) > SilenceTimeoutTicks)
            {
                ClearAnalysis();
                return;
            }

            SnapshotSamples();
            AnalyzeWaveform();
            AnalyzeSpectrum();
            _hasAnalysis = true;

            if (_graphs != null && Time.unscaledTime >= _nextGraphUpdateTime)
            {
                _nextGraphUpdateTime = Time.unscaledTime + GraphUpdateInterval;
                _graphs.Update(_waveform, _logSpectrum);
            }
        }

        public float GetFrequency(int spectrumIndex)
        {
            if (SampleRate == 0 || spectrumIndex < 0 || spectrumIndex >= _spectrum.Length) return 0f;
            return spectrumIndex * SampleRate / (2f * _spectrum.Length);
        }

        private void StartCapture()
        {
            StopCapture();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                using MMDeviceEnumerator enumerator = new();
                _device = string.IsNullOrEmpty(_params.DeviceId)
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    : enumerator.GetDevice(_params.DeviceId);

                if (TryStartBridge(out Exception bridgeException))
                {
                    _captureError = null;
                    IsConnected = true;
                    Interlocked.Exchange(ref _lastDataTicks, DateTime.UtcNow.Ticks);
                    return;
                }

                WaveFormat mixFormat = _device.AudioClient.MixFormat;
                int channels = Mathf.Clamp(mixFormat.Channels, 1, 2);

                // Unity/MonoのCOMマーシャリングではWAVEFORMATEXTENSIBLEが一部の
                // WASAPIドライバーに拒否されることがある。通常のWAVEFORMATEXを
                // 明示し、NAudioの共有モード変換に任せる。
                // NAudio標準のWasapiLoopbackCaptureはLoopbackに加えて
                // AutoConvertPcm/SrcDefaultQualityも常に付ける。ドライバーによっては
                // この組み合わせをAUDCLNT_E_UNSUPPORTED_FORMATとして拒否するため、
                // まず変換なしのLoopback + デバイスMixFormatで初期化する。
                if (!TryStartCapture(null, false, out Exception directException))
                {
                    WaveFormat floatFormat = WaveFormat.CreateIeeeFloatWaveFormat(mixFormat.SampleRate, channels);
                    if (!TryStartCapture(floatFormat, true, out Exception floatException))
                    {
                        // Float形式を受け付けない古いドライバー向けのフォールバック。
                        WaveFormat pcmFormat = new(mixFormat.SampleRate, 16, channels);
                        if (!TryStartCapture(pcmFormat, true, out Exception pcmException))
                        {
                            throw new InvalidOperationException(
                                $"Device: {_device.FriendlyName}\n" +
                                $"Mix format: {DescribeFormat(mixFormat)}\n" +
                                $"External bridge: {FormatException(bridgeException)}\n" +
                                $"Direct loopback: {FormatException(directException)}\n" +
                                $"Requested: {DescribeFormat(floatFormat)} ({FormatException(floatException)})\n" +
                                $"Fallback: {DescribeFormat(pcmFormat)} ({FormatException(pcmException)})",
                                pcmException);
                        }
                    }
                }

                _captureError = null;
                IsConnected = true;
                Interlocked.Exchange(ref _lastDataTicks, DateTime.UtcNow.Ticks);
            }
            catch (Exception exception)
            {
                _captureError = exception.Message;
                IsConnected = false;
                Debug.LogError($"Failed to start WASAPI loopback capture.\n{exception}", this);
                StopCapture();
            }
#else
            _captureError = "WASAPI loopback is only available on Windows.";
#endif
        }

        private void StopCapture()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            StopBridge();
            IsConnected = false;
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                try { _capture.StopRecording(); }
                catch { /* already stopped */ }
                _capture.Dispose();
                _capture = null;
            }
            _device?.Dispose();
            _device = null;
#endif
            ClearAnalysis();
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private bool TryStartBridge(out Exception exception)
        {
            var ready = new ManualResetEventSlim(false);
            Exception readerException = null;
            try
            {
                string executablePath = Path.Combine(
                    Application.streamingAssetsPath,
                    "WasapiLoopbackBridge",
                    "WasapiLoopbackBridge.exe");
                if (!File.Exists(executablePath))
                    throw new FileNotFoundException("WASAPI loopback bridge was not found.", executablePath);

                string encodedDeviceId = Convert.ToBase64String(Encoding.UTF8.GetBytes(_device.ID));
                _bridgeProcess = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = executablePath,
                        Arguments = encodedDeviceId,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                if (!_bridgeProcess.Start())
                    throw new InvalidOperationException("Failed to launch WASAPI loopback bridge.");

                _bridgeStopping = false;
                _bridgeReaderThread = new Thread(() => BridgeReaderLoop(ready, value => readerException = value))
                {
                    IsBackground = true,
                    Name = "Aetherin WASAPI bridge reader"
                };
                _bridgeReaderThread.Start();

                if (!ready.Wait(TimeSpan.FromSeconds(3)))
                    throw new TimeoutException("WASAPI loopback bridge did not respond within 3 seconds.");
                if (readerException != null) throw readerException;
                if (_bridgeFormat == null)
                    throw new InvalidDataException("WASAPI loopback bridge returned no audio format.");

                SampleRate = _bridgeFormat.SampleRate;
                Volatile.Write(ref _bridgeOnsetAvailable, true);
                exception = null;
                return true;
            }
            catch (Exception bridgeException)
            {
                exception = bridgeException;
                StopBridge();
                return false;
            }
            finally
            {
                ready.Dispose();
            }
        }

        private void BridgeReaderLoop(ManualResetEventSlim ready, Action<Exception> reportStartupError)
        {
            bool startupComplete = false;
            try
            {
                using (var reader = new BinaryReader(_bridgeProcess.StandardOutput.BaseStream))
                {
                    const int magic = 0x324C4541;
                    if (reader.ReadInt32() != magic)
                        throw new InvalidDataException("Invalid WASAPI loopback bridge header.");

                    int sampleRate = reader.ReadInt32();
                    int channels = reader.ReadInt32();
                    int bitsPerSample = reader.ReadInt32();
                    var encoding = (WaveFormatEncoding)reader.ReadInt32();
                    _bridgeFormat = encoding == WaveFormatEncoding.IeeeFloat
                        ? WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels)
                        : new WaveFormat(sampleRate, bitsPerSample, channels);

                    startupComplete = true;
                    ready.Set();

                    while (!_bridgeStopping)
                    {
                        byte packetType = reader.ReadByte();
                        switch (packetType)
                        {
                            case 1:
                            {
                                reader.ReadInt64(); // packet先頭のサンプル位置（将来の同期用）
                                int byteCount = reader.ReadInt32();
                                if (byteCount <= 0 || byteCount > 16 * 1024 * 1024)
                                    throw new InvalidDataException($"Invalid bridge packet size: {byteCount}");
                                byte[] buffer = reader.ReadBytes(byteCount);
                                if (buffer.Length != byteCount) throw new EndOfStreamException();
                                ConsumeSamples(buffer, byteCount, _bridgeFormat);
                                break;
                            }
                            case 2:
                                ReceiveBridgeOnset(reader.ReadInt64(), reader.ReadSingle(), reader.ReadSingle());
                                break;
                            default:
                                throw new InvalidDataException($"Unknown bridge packet type: {packetType}");
                        }
                    }
                }
            }
            catch (Exception readerException)
            {
                if (!startupComplete)
                {
                    string stderr = ReadBridgeError();
                    reportStartupError(new InvalidOperationException(
                        string.IsNullOrWhiteSpace(stderr) ? readerException.Message : stderr,
                        readerException));
                    ready.Set();
                }
                else if (!_bridgeStopping)
                {
                    _captureError = $"WASAPI bridge stopped: {readerException.Message}";
                    IsConnected = false;
                }
            }
        }

        private void ReceiveBridgeOnset(long sampleIndex, float kick, float snareClap)
        {
            if (kick > 0f)
            {
                Volatile.Write(ref _latestKickStrength, kick);
                Interlocked.Exchange(ref _latestKickSampleIndex, sampleIndex);
                Interlocked.Increment(ref _kickOnsetSequence);
            }
            if (snareClap > 0f)
            {
                Volatile.Write(ref _latestSnareStrength, snareClap);
                Interlocked.Exchange(ref _latestSnareSampleIndex, sampleIndex);
                Interlocked.Increment(ref _snareOnsetSequence);
            }
        }

        private string ReadBridgeError()
        {
            try { return _bridgeProcess?.StandardError.ReadToEnd(); }
            catch { return string.Empty; }
        }

        private void StopBridge()
        {
            _bridgeStopping = true;
            Volatile.Write(ref _bridgeOnsetAvailable, false);
            if (_bridgeProcess != null)
            {
                try
                {
                    if (!_bridgeProcess.HasExited) _bridgeProcess.Kill();
                }
                catch { /* already exited */ }
            }

            if (_bridgeReaderThread != null &&
                _bridgeReaderThread != Thread.CurrentThread &&
                _bridgeReaderThread.IsAlive)
            {
                _bridgeReaderThread.Join(1000);
            }

            _bridgeReaderThread = null;
            _bridgeProcess?.Dispose();
            _bridgeProcess = null;
            _bridgeFormat = null;
        }

        private bool TryStartCapture(WaveFormat format, bool useFormatConversion, out Exception exception)
        {
            try
            {
                _capture = useFormatConversion
                    ? new WasapiLoopbackCapture(_device)
                    : new DirectWasapiLoopbackCapture(_device);
                if (format != null) _capture.WaveFormat = format;
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                SampleRate = _capture.WaveFormat.SampleRate;
                exception = null;
                return true;
            }
            catch (Exception startException)
            {
                exception = startException;
                if (_capture != null)
                {
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.RecordingStopped -= OnRecordingStopped;
                    _capture.Dispose();
                    _capture = null;
                }
                return false;
            }
        }

        private static string DescribeFormat(WaveFormat format) =>
            $"{format.Encoding}, {format.SampleRate} Hz, {format.Channels} ch, {format.BitsPerSample} bit";

        private static string FormatException(Exception exception) =>
            $"0x{exception.HResult:X8}: {exception.Message}";

        /// <summary>
        /// NAudio標準実装が付加する共有モードの自動変換フラグを使わず、
        /// Windowsの再生MixFormatをそのままLoopbackキャプチャする。
        /// </summary>
        private sealed class DirectWasapiLoopbackCapture : WasapiCapture
        {
            public DirectWasapiLoopbackCapture(MMDevice device) : base(device, false, 100) { }

            protected override AudioClientStreamFlags GetAudioClientStreamFlags() =>
                AudioClientStreamFlags.Loopback;
        }

        private void OnDataAvailable(object sender, WaveInEventArgs args)
        {
            WaveFormat format = _capture?.WaveFormat;
            ConsumeSamples(args.Buffer, args.BytesRecorded, format);
        }

        private void ConsumeSamples(byte[] buffer, int bytesRecorded, WaveFormat format)
        {
            if (format == null || format.Channels <= 0) return;

            int bytesPerSample = format.BitsPerSample / 8;
            int frameSize = bytesPerSample * format.Channels;
            if (bytesPerSample <= 0 || frameSize <= 0) return;

            lock (_sampleLock)
            {
                for (int offset = 0; offset + frameSize <= bytesRecorded; offset += frameSize)
                {
                    float mono = 0f;
                    for (int channel = 0; channel < format.Channels; channel++)
                        mono += ReadSample(buffer, offset + channel * bytesPerSample, format.BitsPerSample);
                    mono /= format.Channels;

                    _ring[_ringWriteIndex] = mono;
                    _ringWriteIndex = (_ringWriteIndex + 1) % RingCapacity;
                    _ringCount = Math.Min(_ringCount + 1, RingCapacity);
                }
            }
            Interlocked.Exchange(ref _lastDataTicks, DateTime.UtcNow.Ticks);
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs args)
        {
            IsConnected = false;
            if (args.Exception != null) _captureError = args.Exception.Message;
        }

        private static float ReadSample(byte[] buffer, int offset, int bitsPerSample)
        {
            return bitsPerSample switch
            {
                32 => BitConverter.ToSingle(buffer, offset),
                16 => BitConverter.ToInt16(buffer, offset) / 32768f,
                _ => 0f
            };
        }
#endif

        private void SnapshotSamples()
        {
            int fftSize = ValidatePowerOfTwo(_params.FftSize, 256, 8192);
            int waveformSize = Mathf.Clamp(_params.WaveformSamples, 128, RingCapacity);
            EnsureSize(ref _fftReal, fftSize);
            EnsureSize(ref _fftImaginary, fftSize);
            EnsureSize(ref _waveform, waveformSize);

            lock (_sampleLock)
            {
                CopyLatest(_fftReal, fftSize);
                CopyLatest(_waveform, waveformSize);
            }
        }

        private void CopyLatest(float[] destination, int count)
        {
            Array.Clear(destination, 0, destination.Length);
            int available = Mathf.Min(count, _ringCount);
            int destinationOffset = count - available;
            int sourceIndex = (_ringWriteIndex - available + RingCapacity) % RingCapacity;
            for (int i = 0; i < available; i++) destination[destinationOffset + i] = _ring[(sourceIndex + i) % RingCapacity];
        }

        private void AnalyzeWaveform()
        {
            double squareSum = 0;
            float peak = 0f;
            foreach (float sample in _waveform)
            {
                squareSum += sample * sample;
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            RmsLevel = _waveform.Length > 0 ? Mathf.Sqrt((float)(squareSum / _waveform.Length)) : 0f;
            PeakLevel = peak;
        }

        private void AnalyzeSpectrum()
        {
            int size = _fftReal.Length;
            EnsureSize(ref _fftImaginary, size);
            Array.Clear(_fftImaginary, 0, size);
            for (int i = 0; i < size; i++)
                _fftReal[i] *= 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / (size - 1));

            Fft(_fftReal, _fftImaginary);
            int binCount = size / 2;
            EnsureSize(ref _spectrum, binCount);
            EnsureSize(ref _logSpectrum, binCount);

            float dynamicRange = Mathf.Clamp(_params.DynamicRange, 20f, 120f);
            for (int i = 0; i < binCount; i++)
            {
                float magnitude = 2f * Mathf.Sqrt(_fftReal[i] * _fftReal[i] + _fftImaginary[i] * _fftImaginary[i]) / size;
                float db = 20f * Mathf.Log10(Mathf.Max(magnitude, 1e-7f)) + _params.Gain;
                _spectrum[i] = Mathf.Clamp01((db + dynamicRange) / dynamicRange);
            }

            float minBin = Mathf.Max(1f, 20f * size / Mathf.Max(1, SampleRate));
            float maxBin = binCount - 1;
            for (int i = 0; i < binCount; i++)
            {
                float t = binCount > 1 ? i / (float)(binCount - 1) : 0f;
                int source = Mathf.Clamp(Mathf.RoundToInt(Mathf.Exp(Mathf.Lerp(Mathf.Log(minBin), Mathf.Log(maxBin), t))), 0, binCount - 1);
                _logSpectrum[i] = _spectrum[source];
            }
        }

        private static void Fft(float[] real, float[] imaginary)
        {
            int n = real.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i >= j) continue;
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }

            for (int length = 2; length <= n; length <<= 1)
            {
                float angle = -2f * Mathf.PI / length;
                float wLengthReal = Mathf.Cos(angle);
                float wLengthImaginary = Mathf.Sin(angle);
                for (int start = 0; start < n; start += length)
                {
                    float wReal = 1f;
                    float wImaginary = 0f;
                    for (int offset = 0; offset < length / 2; offset++)
                    {
                        int even = start + offset;
                        int odd = even + length / 2;
                        float oddReal = real[odd] * wReal - imaginary[odd] * wImaginary;
                        float oddImaginary = real[odd] * wImaginary + imaginary[odd] * wReal;
                        real[odd] = real[even] - oddReal;
                        imaginary[odd] = imaginary[even] - oddImaginary;
                        real[even] += oddReal;
                        imaginary[even] += oddImaginary;
                        float nextReal = wReal * wLengthReal - wImaginary * wLengthImaginary;
                        wImaginary = wReal * wLengthImaginary + wImaginary * wLengthReal;
                        wReal = nextReal;
                    }
                }
            }
        }

        private void ClearAnalysis()
        {
            if (!_hasAnalysis) return;

            _hasAnalysis = false;
            RmsLevel = 0f;
            PeakLevel = 0f;
            if (_waveform.Length > 0) Array.Clear(_waveform, 0, _waveform.Length);
            if (_spectrum.Length > 0) Array.Clear(_spectrum, 0, _spectrum.Length);
            if (_logSpectrum.Length > 0) Array.Clear(_logSpectrum, 0, _logSpectrum.Length);
            _graphs?.Clear();
        }

        private static int ValidatePowerOfTwo(int value, int min, int max) => Mathf.Clamp(Mathf.ClosestPowerOfTwo(value), min, max);
        private static void EnsureSize(ref float[] array, int size) { if (array.Length != size) array = new float[size]; }

        public Element AdditiveUi()
        {
            _graphs ??= new AudioGraphTextures();
            return UI.Fold("WASAPI Loopback Monitor",
                UI.Row(
                    UI.DynamicElementOnStatusChanged(() => _deviceListVersion, _ => CreateDeviceDropdown()),
                    UI.Button("Refresh", () => _deviceListVersion++)),
                UI.Label(() => IsConnected
                    ? $"Connected : {SampleRate} Hz / system output"
                    : $"Not connected{(string.IsNullOrEmpty(_captureError) ? "" : " : " + _captureError)}"),
                UI.SliderReadOnly("RMS", () => RmsLevel, 0f, 1f),
                UI.SliderReadOnly("Peak", () => PeakLevel, 0f, 1f),
                UI.Label(() => $"Waveform ({_waveform.Length} samples)"),
                UI.Image(() => _graphs.WaveformTexture).SetWidth(AudioGraphTextures.Width).SetHeight(AudioGraphTextures.WaveformHeight),
                UI.Label(() => $"Spectrum ({_spectrum.Length} bins / 0–{SampleRate / 2f:F0} Hz, log scale)"),
                UI.Image(() => _graphs.SpectrumTexture).SetWidth(AudioGraphTextures.Width).SetHeight(AudioGraphTextures.SpectrumHeight)
            ).SetWidth(400f);
        }

        private Element CreateDeviceDropdown()
        {
            RefreshDeviceList();
            return UI.Dropdown("Output", GetSelectedDeviceIndex, SelectDevice, _deviceNames).SetWidth(310f);
        }

        private void RefreshDeviceList()
        {
            _deviceNames.Clear();
            _deviceIds.Clear();
            _deviceNames.Add("System Default Output");
            _deviceIds.Add(string.Empty);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                using MMDeviceEnumerator enumerator = new();
                foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    _deviceNames.Add(device.FriendlyName);
                    _deviceIds.Add(device.ID);
                    device.Dispose();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to enumerate Windows output devices.\n{exception}", this);
            }
#endif
            if (!string.IsNullOrEmpty(_params.DeviceId) && !_deviceIds.Contains(_params.DeviceId))
            {
                _deviceNames.Add($"Unavailable ({_params.DeviceId})");
                _deviceIds.Add(_params.DeviceId);
            }
        }

        private int GetSelectedDeviceIndex()
        {
            int index = _deviceIds.IndexOf(_params.DeviceId);
            return index >= 0 ? index : 0;
        }

        private void SelectDevice(int index)
        {
            if (index < 0 || index >= _deviceIds.Count) return;
            string deviceId = _deviceIds[index];
            if (_params.DeviceId == deviceId && IsConnected) return;

            _params.DeviceId = deviceId;
            if (_restartCoroutine != null) StopCoroutine(_restartCoroutine);
            _restartCoroutine = StartCoroutine(RestartCaptureAfterRelease());
        }

        private IEnumerator RestartCaptureAfterRelease()
        {
            StopCapture();

            // WasapiCaptureの停止処理は別スレッドで完了するため、COMエンドポイントが
            // 解放される前に再初期化しないよう少し待つ。
            yield return new WaitForSecondsRealtime(0.1f);

            _restartCoroutine = null;
            if (isActiveAndEnabled) StartCapture();
        }
    }
}
