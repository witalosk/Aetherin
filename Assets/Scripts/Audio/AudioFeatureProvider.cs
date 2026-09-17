using System;
using RosettaUI;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// IAudioInputから打楽器のオンセットと、シェーダー向けの1次元データTextureを生成する。
    /// Textureの横方向は、Waveformが時間、Spectrumが対数周波数軸に対応する。
    /// Spectrumの振幅は入力側でdBFS変換し、設定されたダイナミックレンジを0～1に正規化した値。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class AudioFeatureProvider : MonoBehaviour, IAudioFeatureProvider, ISaveAndUiTarget
    {
        private const float UiGraphUpdateInterval = 1f / 30f;
        private static readonly Color KickColor = new(1f, 0.42f, 0.08f);
        private static readonly Color SnareColor = new(0.25f, 0.8f, 1f);

        public float InputVolume => _audioInput?.RmsLevel ?? 0f;
        public float Kick => _kick;
        public float SnareClap => _snareClap;
        public bool WasKick => _lastKickFrame == Time.frameCount;
        public bool WasSnareClap => _lastSnareFrame == Time.frameCount;
        public long LastKickSampleIndex { get; private set; } = -1;
        public long LastSnareClapSampleIndex { get; private set; } = -1;
        public Texture WaveformTexture => _waveformTexture;
        public Texture SpectrumTexture => _spectrumTexture;
        public IParams Params => _params;
        public string Category => UiCategory.Settings;
        public bool FoldParams => true;

        [SerializeField] private MonoBehaviour _audioInputSource;
        [SerializeField] private AudioFeatureProviderParams _params = new();

        private IAudioInput _audioInput;
        private IPercussiveOnsetSource _onsetSource;
        private int _consumedKickSequence;
        private int _consumedSnareSequence;
        private Texture2D _waveformTexture;
        private Texture2D _spectrumTexture;
        private float[] _waveformData = Array.Empty<float>();
        private float[] _spectrumData = Array.Empty<float>();
        private float[] _previousSpectrum = Array.Empty<float>();

        private AdaptiveBandState _kickLowState;
        private AdaptiveBandState _kickPunchState;
        private AdaptiveBandState _snareBodyState;
        private AdaptiveBandState _snareLowNoiseState;
        private AdaptiveBandState _snareHighNoiseState;
        private float _kick;
        private float _snareClap;
        private float _kickCooldown;
        private float _snareCooldown;
        private int _lastKickFrame = -1;
        private int _lastSnareFrame = -1;
        private bool _analysisInitialized;
        private AudioGraphTextures _uiGraphs;
        private float[] _uiLogSpectrum = Array.Empty<float>();
        private float _nextUiGraphUpdateTime;
        private bool _uiGraphsCleared = true;

        public float GetSpectrumValueNormalized(float normalizedFrequency)
        {
            if (_spectrumData.Length == 0) return 0f;
            float index = Mathf.Clamp01(normalizedFrequency) * (_spectrumData.Length - 1);
            int from = Mathf.FloorToInt(index);
            int to = Mathf.Min(from + 1, _spectrumData.Length - 1);
            return Mathf.Lerp(_spectrumData[from], _spectrumData[to], index - from);
        }

        private void Awake()
        {
            ResolveAudioInput();
        }

        private void OnEnable()
        {
            ResolveAudioInput();
            ResetAnalysis();
        }

        private void OnDestroy()
        {
            DestroyTexture(ref _waveformTexture);
            DestroyTexture(ref _spectrumTexture);
            _uiGraphs?.Dispose();
        }

        private void Update()
        {
            if (_audioInput == null) ResolveAudioInput();
            if (_audioInput == null || !_audioInput.IsConnected)
            {
                DecayPulses(Time.unscaledDeltaTime);
                if (_uiGraphs != null && !_uiGraphsCleared)
                {
                    _uiGraphs.Clear();
                    _uiGraphsCleared = true;
                }
                return;
            }

            ReadOnlySpan<float> waveform = _audioInput.Waveform;
            ReadOnlySpan<float> spectrum = _audioInput.Spectrum;
            ReadOnlySpan<float> logSpectrum = _audioInput.LogSpectrum;
            UploadTexture(waveform, ref _waveformData, ref _waveformTexture, "Audio Waveform Data");
            UploadTexture(logSpectrum, ref _spectrumData, ref _spectrumTexture, "Audio Log Spectrum Data");
            if (_onsetSource?.IsHardRealtimeOnsetAvailable == true)
            {
                DecayPulses(Time.unscaledDeltaTime);
                ConsumeHardRealtimeOnsets();
            }
            else
            {
                AnalyzeOnsets(spectrum, Time.unscaledDeltaTime);
            }
            UpdateUiGraphs();
        }

        private void ResolveAudioInput()
        {
            _audioInput = _audioInputSource as IAudioInput;
            if (_audioInput != null)
            {
                ResolveOnsetSource();
                return;
            }

            MonoBehaviour[] components = GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour component in components)
            {
                if (component is IAudioInput input)
                {
                    _audioInput = input;
                    _audioInputSource = component;
                    ResolveOnsetSource();
                    return;
                }
            }

            WasapiLoopbackInput wasapi = FindFirstObjectByType<WasapiLoopbackInput>();
            if (wasapi != null)
            {
                _audioInput = wasapi;
                _audioInputSource = wasapi;
                ResolveOnsetSource();
            }
        }

        private void ResolveOnsetSource()
        {
            _onsetSource = _audioInput as IPercussiveOnsetSource;
            if (_onsetSource == null) return;
            _consumedKickSequence = _onsetSource.KickOnsetSequence;
            _consumedSnareSequence = _onsetSource.SnareClapOnsetSequence;
        }

        private void ConsumeHardRealtimeOnsets()
        {
            if (_onsetSource == null) return;

            int kickSequence = _onsetSource.KickOnsetSequence;
            if (kickSequence != _consumedKickSequence)
            {
                _consumedKickSequence = kickSequence;
                float strength = Mathf.Clamp01(_onsetSource.LatestKickStrength * _params.KickSensitivity);
                if (_kickCooldown <= 0f && strength >= _params.TriggerThreshold)
                {
                    _kick = Mathf.Max(_kick, strength);
                    _kickCooldown = _params.Cooldown;
                    _lastKickFrame = Time.frameCount;
                    LastKickSampleIndex = _onsetSource.LatestKickSampleIndex;
                }
            }

            int snareSequence = _onsetSource.SnareClapOnsetSequence;
            if (snareSequence != _consumedSnareSequence)
            {
                _consumedSnareSequence = snareSequence;
                float strength = Mathf.Clamp01(_onsetSource.LatestSnareClapStrength * _params.SnareSensitivity);
                if (_snareCooldown <= 0f && strength >= _params.TriggerThreshold)
                {
                    _snareClap = Mathf.Max(_snareClap, strength);
                    _snareCooldown = _params.Cooldown;
                    _lastSnareFrame = Time.frameCount;
                    LastSnareClapSampleIndex = _onsetSource.LatestSnareClapSampleIndex;
                }
            }
        }

        private void AnalyzeOnsets(ReadOnlySpan<float> spectrum, float deltaTime)
        {
            if (spectrum.Length == 0 || _audioInput.SampleRate <= 0)
            {
                DecayPulses(deltaTime);
                return;
            }

            if (_previousSpectrum.Length != spectrum.Length)
            {
                _previousSpectrum = new float[spectrum.Length];
                spectrum.CopyTo(_previousSpectrum);
                _analysisInitialized = false;
            }

            float kickSplit = Mathf.Clamp(
                _params.KickSplitFrequency,
                _params.KickMinFrequency + 1f,
                _params.KickMaxFrequency - 1f);
            float snareBodyMax = Mathf.Max(_params.SnareMinFrequency + 1f, _params.SnareBodyMaxFrequency);
            float noiseMin = Mathf.Max(snareBodyMax, _params.SnareNoiseMinFrequency);
            float noiseSplit = Mathf.Clamp(
                _params.SnareNoiseSplitFrequency,
                noiseMin + 1f,
                _params.SnareMaxFrequency - 1f);

            GetBandFeatures(spectrum, _params.KickMinFrequency, kickSplit,
                out float kickLowEnergy, out float kickLowFlux);
            GetBandFeatures(spectrum, kickSplit, _params.KickMaxFrequency,
                out float kickPunchEnergy, out float kickPunchFlux);
            GetBandFeatures(spectrum, _params.SnareMinFrequency, snareBodyMax,
                out float snareBodyEnergy, out float snareBodyFlux);
            GetBandFeatures(spectrum, noiseMin, noiseSplit,
                out float snareLowNoiseEnergy, out float snareLowNoiseFlux);
            GetBandFeatures(spectrum, noiseSplit, _params.SnareMaxFrequency,
                out float snareHighNoiseEnergy, out float snareHighNoiseFlux);

            if (!_analysisInitialized)
            {
                _kickLowState.Initialize(kickLowFlux, kickLowEnergy);
                _kickPunchState.Initialize(kickPunchFlux, kickPunchEnergy);
                _snareBodyState.Initialize(snareBodyFlux, snareBodyEnergy);
                _snareLowNoiseState.Initialize(snareLowNoiseFlux, snareLowNoiseEnergy);
                _snareHighNoiseState.Initialize(snareHighNoiseFlux, snareHighNoiseEnergy);
                _analysisInitialized = true;
                spectrum.CopyTo(_previousSpectrum);
                DecayPulses(deltaTime);
                return;
            }

            float threshold = Mathf.Max(0.5f, _params.FluxThreshold);
            float adaptationTime = Mathf.Max(0.01f, _params.AdaptationTime);
            float longTermAdaptationTime = Mathf.Max(adaptationTime, _params.LongTermAdaptationTime);
            float longTermWeight = Mathf.Clamp01(_params.LongTermWeight);
            float kickLow = _kickLowState.Evaluate(kickLowFlux, kickLowEnergy, threshold,
                adaptationTime, longTermAdaptationTime, longTermWeight, deltaTime);
            float kickPunch = _kickPunchState.Evaluate(kickPunchFlux, kickPunchEnergy, threshold,
                adaptationTime, longTermAdaptationTime, longTermWeight, deltaTime);
            float snareBody = _snareBodyState.Evaluate(snareBodyFlux, snareBodyEnergy, threshold,
                adaptationTime, longTermAdaptationTime, longTermWeight, deltaTime);
            float snareLowNoise = _snareLowNoiseState.Evaluate(
                snareLowNoiseFlux, snareLowNoiseEnergy, threshold,
                adaptationTime, longTermAdaptationTime, longTermWeight, deltaTime);
            float snareHighNoise = _snareHighNoiseState.Evaluate(
                snareHighNoiseFlux, snareHighNoiseEnergy, threshold,
                adaptationTime, longTermAdaptationTime, longTermWeight, deltaTime);

            // サブとパンチの両方が動くほどKickらしい。片方だけでも完全には捨てない。
            float kickCoherence = Mathf.Sqrt(kickLow * kickPunch);
            float kickTransient = Mathf.Clamp01(
                (Mathf.Max(kickLow, kickPunch * 0.9f) * 0.7f + kickCoherence * 0.3f) *
                _params.KickSensitivity);

            // 広いノイズ帯域が同時に動くものをSnare/Clapとする。
            // 狭帯域に寄りやすいハイハットやメロディのアタックを抑制できる。
            float noiseMinimum = Mathf.Min(snareLowNoise, snareHighNoise);
            float noiseCoherence = Mathf.Sqrt(snareLowNoise * snareHighNoise);
            float broadNoise = noiseMinimum * 0.55f + noiseCoherence * 0.45f;
            float snareTransient = Mathf.Clamp01(
                (broadNoise * 0.85f + Mathf.Min(broadNoise, snareBody) * 0.3f) *
                _params.SnareSensitivity);

            snareTransient *= 1f - _params.KickToSnareRejection * kickTransient;

            ResolveCompetingClassification(ref kickTransient, ref snareTransient);

            if (_audioInput.RmsLevel < _params.NoiseGate)
            {
                kickTransient = 0f;
                snareTransient = 0f;
            }

            UpdatePulse(ref _kick, kickTransient, ref _kickCooldown, ref _lastKickFrame, deltaTime);
            UpdatePulse(ref _snareClap, snareTransient, ref _snareCooldown, ref _lastSnareFrame, deltaTime);

            spectrum.CopyTo(_previousSpectrum);
        }

        private void ResolveCompetingClassification(ref float kick, ref float snare)
        {
            if (kick < _params.TriggerThreshold || snare < _params.TriggerThreshold) return;

            float difference = kick - snare;
            float margin = Mathf.Max(0f, _params.ClassificationMargin);
            if (Mathf.Abs(difference) >= margin)
            {
                // 十分に差があるときは、弱い方を帯域間クロストークとして抑える。
                if (difference >= 0f) snare = 0f;
                else kick = 0f;
            }
        }

        private void GetBandFeatures(
            ReadOnlySpan<float> spectrum,
            float minFrequency,
            float maxFrequency,
            out float energy,
            out float flux)
        {
            int sampleRate = _audioInput.SampleRate;
            int from = Mathf.Clamp(Mathf.FloorToInt(minFrequency * 2f * spectrum.Length / sampleRate), 0, spectrum.Length - 1);
            int to = Mathf.Clamp(Mathf.CeilToInt(maxFrequency * 2f * spectrum.Length / sampleRate), from + 1, spectrum.Length);

            energy = 0f;
            flux = 0f;
            for (int i = from; i < to; i++)
            {
                float value = spectrum[i];
                energy += value;
                flux += Mathf.Max(0f, value - _previousSpectrum[i]);
            }

            float inverseCount = 1f / (to - from);
            energy *= inverseCount;
            flux *= inverseCount;
        }

        private struct AdaptiveBandState
        {
            public float LongMean => _longFluxMean;
            public float LongStandardDeviation =>
                Mathf.Sqrt(Mathf.Max(0.000001f, _longFluxVariance));
            public float LongDeviation { get; private set; }

            private float _fluxMean;
            private float _fluxVariance;
            private float _energyMean;
            private float _longFluxMean;
            private float _longFluxVariance;
            private float _longEnergyMean;

            public void Initialize(float flux, float energy)
            {
                _fluxMean = flux;
                _fluxVariance = 0.000001f;
                _energyMean = energy;
                _longFluxMean = flux;
                _longFluxVariance = 0.000001f;
                _longEnergyMean = energy;
                LongDeviation = 0f;
            }

            public float Evaluate(
                float flux,
                float energy,
                float deviationThreshold,
                float adaptationTime,
                float longTermAdaptationTime,
                float longTermWeight,
                float deltaTime)
            {
                float shortDeviation = (flux - _fluxMean) /
                    Mathf.Sqrt(Mathf.Max(0.000001f, _fluxVariance));
                float longDeviation = (flux - _longFluxMean) /
                    Mathf.Sqrt(Mathf.Max(0.000001f, _longFluxVariance));
                LongDeviation = longDeviation;
                float deviation = Mathf.Lerp(shortDeviation, longDeviation, longTermWeight);
                float fluxScore = Mathf.Clamp01((deviation - deviationThreshold) / 3f);
                float shortEnergyRise = Mathf.Clamp01(
                    Mathf.Max(0f, energy - _energyMean * 1.06f) /
                    Mathf.Max(0.025f, _energyMean * 0.35f));
                float longEnergyRise = Mathf.Clamp01(
                    Mathf.Max(0f, energy - _longEnergyMean * 1.06f) /
                    Mathf.Max(0.025f, _longEnergyMean * 0.35f));
                float energyRise = Mathf.Lerp(shortEnergyRise, longEnergyRise, longTermWeight);
                float score = Mathf.Clamp01(fluxScore * 0.8f + energyRise * 0.2f);

                float adaptation = 1f - Mathf.Exp(-deltaTime / adaptationTime);
                float longAdaptation = 1f - Mathf.Exp(-deltaTime / longTermAdaptationTime);
                // 強いオンセットを通常値として即座に学習しないよう、発火中だけ追従を遅くする。
                if (score > 0.1f)
                {
                    adaptation *= 0.15f;
                    longAdaptation *= 0.05f;
                }

                float difference = flux - _fluxMean;
                _fluxMean = Mathf.Lerp(_fluxMean, flux, adaptation);
                _fluxVariance = Mathf.Lerp(_fluxVariance, difference * difference, adaptation);
                _energyMean = Mathf.Lerp(_energyMean, energy, adaptation);

                float longDifference = flux - _longFluxMean;
                _longFluxMean = Mathf.Lerp(_longFluxMean, flux, longAdaptation);
                _longFluxVariance = Mathf.Lerp(
                    _longFluxVariance, longDifference * longDifference, longAdaptation);
                _longEnergyMean = Mathf.Lerp(_longEnergyMean, energy, longAdaptation);
                return score;
            }
        }

        private void UpdatePulse(
            ref float value,
            float transient,
            ref float cooldown,
            ref int lastTriggerFrame,
            float deltaTime)
        {
            cooldown = Mathf.Max(0f, cooldown - deltaTime);
            float release = Mathf.Exp(-deltaTime / Mathf.Max(0.005f, _params.ReleaseTime));
            value *= release;

            if (cooldown <= 0f && transient >= _params.TriggerThreshold)
            {
                value = Mathf.Max(value, Mathf.Clamp01(transient));
                cooldown = _params.Cooldown;
                lastTriggerFrame = Time.frameCount;
            }
        }

        private void DecayPulses(float deltaTime)
        {
            UpdatePulse(ref _kick, 0f, ref _kickCooldown, ref _lastKickFrame, deltaTime);
            UpdatePulse(ref _snareClap, 0f, ref _snareCooldown, ref _lastSnareFrame, deltaTime);
        }

        private static void UploadTexture(
            ReadOnlySpan<float> source,
            ref float[] data,
            ref Texture2D texture,
            string textureName)
        {
            if (source.Length == 0) return;
            if (data.Length != source.Length) data = new float[source.Length];
            source.CopyTo(data);

            if (texture == null || texture.width != source.Length)
            {
                DestroyTexture(ref texture);
                texture = new Texture2D(source.Length, 1, TextureFormat.RFloat, false, true)
                {
                    name = textureName,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
            }

            texture.SetPixelData(data, 0);
            texture.Apply(false, false);
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null) Destroy(texture);
            texture = null;
        }

        private void ResetAnalysis()
        {
            _kick = 0f;
            _snareClap = 0f;
            _kickCooldown = 0f;
            _snareCooldown = 0f;
            _lastKickFrame = -1;
            _lastSnareFrame = -1;
            LastKickSampleIndex = -1;
            LastSnareClapSampleIndex = -1;
            _kickLowState = default;
            _kickPunchState = default;
            _snareBodyState = default;
            _snareLowNoiseState = default;
            _snareHighNoiseState = default;
            _analysisInitialized = false;
        }

        private void UpdateUiGraphs()
        {
            if (_uiGraphs == null || Time.unscaledTime < _nextUiGraphUpdateTime) return;

            if (_uiLogSpectrum.Length != _spectrumData.Length) _uiLogSpectrum = new float[_spectrumData.Length];
            _spectrumData.CopyTo(_uiLogSpectrum, 0);
            _uiGraphs.Update(_waveformData, _uiLogSpectrum);
            _uiGraphsCleared = false;
            _nextUiGraphUpdateTime = Time.unscaledTime + UiGraphUpdateInterval;
        }

        public Element AdditiveUi()
        {
            _uiGraphs ??= new AudioGraphTextures();

            return UI.Fold("Audio Feature Monitor",
                UI.Row(
                    CreatePulseElement("KICK", () => Kick, () => WasKick, KickColor),
                    CreatePulseElement("SNARE / CLAP", () => SnareClap, () => WasSnareClap, SnareColor)),
                UI.Label(() => _onsetSource?.IsHardRealtimeOnsetAvailable == true
                    ? "Hybrid onset: 10 ms block / 30 ms deadline"
                    : "Hybrid onset: fallback (Unity spectral detector)"),
                UI.Label(() => $"Kick sample: {LastKickSampleIndex}   Snare/Clap sample: {LastSnareClapSampleIndex}"),
                UI.SliderReadOnly("Kick", () => Kick, 0f, 1f),
                UI.SliderReadOnly("Snare / Clap", () => SnareClap, 0f, 1f),
                UI.Fold("10 s adaptive statistics",
                    CreateLongTermStatisticsElement("Kick low 35-80 Hz", PercussiveBand.KickLow),
                    CreateLongTermStatisticsElement("Kick punch 80-180 Hz", PercussiveBand.KickPunch),
                    CreateLongTermStatisticsElement("Snare body 180-520 Hz", PercussiveBand.SnareBody),
                    CreateLongTermStatisticsElement("Snare noise 0.7-2.8 kHz", PercussiveBand.SnareLowNoise),
                    CreateLongTermStatisticsElement("Snare noise 2.8-9 kHz", PercussiveBand.SnareHighNoise),
                    UI.Label(() => _onsetSource?.IsHardRealtimeOnsetAvailable == true
                        ? "History: 10.0 s   Weight: 0.40   (Bridge fixed)"
                        : $"History: {_params.LongTermAdaptationTime:F1} s   Weight: {_params.LongTermWeight:F2}   (Fallback)"),
                    UI.Label("Start: 10 s / weight 0.40 / threshold 1.8 / margin 0.08")),
                UI.Label(() => _waveformTexture == null
                    ? "Waveform: unavailable"
                    : $"Waveform ({_waveformTexture.width} samples / RFloat)"),
                UI.Image(() => _uiGraphs.WaveformTexture)
                    .SetWidth(AudioGraphTextures.Width)
                    .SetHeight(AudioGraphTextures.WaveformHeight),
                UI.Label(() => _spectrumTexture == null
                    ? "Spectrum: unavailable"
                    : $"Spectrum ({_spectrumTexture.width} bins / RFloat / log frequency / dB amplitude)"),
                UI.Image(() => _uiGraphs.SpectrumTexture)
                    .SetWidth(AudioGraphTextures.Width)
                    .SetHeight(AudioGraphTextures.SpectrumHeight)
            ).SetWidth(400f);
        }

        private Element CreateLongTermStatisticsElement(string label, PercussiveBand band)
        {
            return UI.Label(() =>
            {
                GetLongTermStatistics(band, out float mean, out float standardDeviation, out float deviation);
                return $"{label}\nmean {mean:E3}   sigma {standardDeviation:E3}   now {deviation:+0.00;-0.00;0.00} sigma";
            });
        }

        private void GetLongTermStatistics(
            PercussiveBand band,
            out float mean,
            out float standardDeviation,
            out float deviation)
        {
            if (_onsetSource?.IsHardRealtimeOnsetAvailable == true)
            {
                mean = _onsetSource.GetLongTermMean(band);
                standardDeviation = _onsetSource.GetLongTermStandardDeviation(band);
                deviation = _onsetSource.GetLongTermDeviation(band);
                return;
            }

            AdaptiveBandState state = band switch
            {
                PercussiveBand.KickLow => _kickLowState,
                PercussiveBand.KickPunch => _kickPunchState,
                PercussiveBand.SnareBody => _snareBodyState,
                PercussiveBand.SnareLowNoise => _snareLowNoiseState,
                PercussiveBand.SnareHighNoise => _snareHighNoiseState,
                _ => default
            };
            mean = state.LongMean;
            standardDeviation = state.LongStandardDeviation;
            deviation = state.LongDeviation;
        }

        private static Element CreatePulseElement(
            string label,
            Func<float> readValue,
            Func<bool> readTrigger,
            Color color)
        {
            return UI.Label(() => $"<b>{label}</b>\n{readValue():F2}")
                .SetWidth(150f)
                .SetHeight(54f)
                .RegisterUpdateCallback(element =>
                {
                    float brightness = Mathf.Lerp(0.08f, 1f, Mathf.Clamp01(readValue()));
                    Color litColor = readTrigger() ? Color.white : color;
                    element.SetBackgroundColor(Color.Lerp(Color.black, litColor, brightness));
                });
        }
    }
}
