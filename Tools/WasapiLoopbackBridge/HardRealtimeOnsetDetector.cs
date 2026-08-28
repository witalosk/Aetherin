using System;
using NAudio.Wave;

namespace Aetherin.WasapiLoopbackBridge
{
    internal struct PercussiveOnset
    {
        public long SampleIndex;
        public float Kick;
        public float SnareClap;
    }

    /// <summary>
    /// 時間領域の候補時刻を、固定deadline内のスペクトルフラックスで確認する
    /// allocation-freeのオンライン打楽器オンセット検出器。
    /// </summary>
    internal sealed class HardRealtimeOnsetDetector
    {
        private const int FftSize = 1024;
        private const int HopSize = 256;
        private const float SpectralThreshold = 1.8f;

        public long TotalSamples { get; private set; }

        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly int _bitsPerSample;
        private readonly WaveFormatEncoding _encoding;
        private readonly long _deadlineSamples;
        private readonly long _candidateRefractorySamples;

        private readonly float[] _ring = new float[FftSize];
        private readonly float[] _real = new float[FftSize];
        private readonly float[] _imaginary = new float[FftSize];
        private readonly float[] _previousMagnitude = new float[FftSize / 2];
        private int _ringWrite;
        private int _samplesSinceFft;

        private float _previousInput;
        private float _previousHighPass;
        private float _fastEnvelope;
        private float _slowEnvelope;
        private float _previousFastEnvelope;
        private float _derivativeEnvelope;
        private float _derivativeMean;
        private float _derivativeVariance = 0.0000000001f;
        private long _lastCandidateSample = long.MinValue / 2;
        private long _candidateSample;
        private bool _candidatePending;

        private AdaptiveState _kickLow;
        private AdaptiveState _kickPunch;
        private AdaptiveState _snareBody;
        private AdaptiveState _snareLowNoise;
        private AdaptiveState _snareHighNoise;
        private bool _spectrumInitialized;

        public HardRealtimeOnsetDetector(WaveFormat format)
        {
            _sampleRate = format.SampleRate;
            _channels = format.Channels;
            _bitsPerSample = format.BitsPerSample;
            _encoding = format.Encoding;
            _deadlineSamples = Math.Max(1, (long)(_sampleRate * 0.030));
            _candidateRefractorySamples = Math.Max(1, (long)(_sampleRate * 0.045));
        }

        public int Process(byte[] buffer, int byteCount, PercussiveOnset[] output)
        {
            int bytesPerSample = _bitsPerSample / 8;
            int frameSize = bytesPerSample * _channels;
            int outputCount = 0;

            for (int offset = 0; offset + frameSize <= byteCount; offset += frameSize)
            {
                float mono = 0f;
                for (int channel = 0; channel < _channels; channel++)
                    mono += ReadSample(buffer, offset + channel * bytesPerSample);
                mono /= _channels;

                ProcessTimeDomain(mono);
                _ring[_ringWrite] = mono;
                _ringWrite = (_ringWrite + 1) & (FftSize - 1);
                TotalSamples++;
                _samplesSinceFft++;

                if (TotalSamples >= FftSize && _samplesSinceFft >= HopSize)
                {
                    _samplesSinceFft = 0;
                    PercussiveOnset onset;
                    if (AnalyzeSpectrum(out onset) && outputCount < output.Length)
                        output[outputCount++] = onset;
                }

                if (_candidatePending && TotalSamples - _candidateSample > _deadlineSamples)
                    _candidatePending = false;
            }

            return outputCount;
        }

        private float ReadSample(byte[] buffer, int offset)
        {
            if (_encoding == WaveFormatEncoding.IeeeFloat && _bitsPerSample == 32)
                return BitConverter.ToSingle(buffer, offset);
            if (_bitsPerSample == 16)
                return BitConverter.ToInt16(buffer, offset) / 32768f;
            return 0f;
        }

        private void ProcessTimeDomain(float sample)
        {
            float dt = 1f / _sampleRate;
            float rc = 1f / (2f * (float)Math.PI * 25f);
            float highPassAlpha = rc / (rc + dt);
            float highPass = highPassAlpha * (_previousHighPass + sample - _previousInput);
            _previousInput = sample;
            _previousHighPass = highPass;

            float energy = highPass * highPass;
            _fastEnvelope += (energy - _fastEnvelope) * (1f - (float)Math.Exp(-dt / 0.0015f));
            _slowEnvelope += (energy - _slowEnvelope) * (1f - (float)Math.Exp(-dt / 0.035f));
            float derivative = Math.Max(0f, _fastEnvelope - _previousFastEnvelope);
            _previousFastEnvelope = _fastEnvelope;
            _derivativeEnvelope += (derivative - _derivativeEnvelope) * (1f - (float)Math.Exp(-dt / 0.001f));

            float standardDeviation = (float)Math.Sqrt(Math.Max(0.0000000001f, _derivativeVariance));
            float threshold = _derivativeMean + standardDeviation * 3f;
            bool enoughEnergy = _fastEnvelope > Math.Max(0.0000001f, _slowEnvelope * 1.35f);
            bool outsideRefractory = TotalSamples - _lastCandidateSample >= _candidateRefractorySamples;

            if (enoughEnergy && outsideRefractory && _derivativeEnvelope > threshold)
            {
                _candidateSample = TotalSamples;
                _lastCandidateSample = TotalSamples;
                _candidatePending = true;
            }

            float adaptation = 1f - (float)Math.Exp(-dt / 0.25f);
            if (_candidatePending) adaptation *= 0.1f;
            float difference = _derivativeEnvelope - _derivativeMean;
            _derivativeMean += difference * adaptation;
            _derivativeVariance += (difference * difference - _derivativeVariance) * adaptation;
        }

        private bool AnalyzeSpectrum(out PercussiveOnset onset)
        {
            onset = new PercussiveOnset();
            for (int i = 0; i < FftSize; i++)
            {
                int ringIndex = (_ringWrite + i) & (FftSize - 1);
                float window = 0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * i / (FftSize - 1));
                _real[i] = _ring[ringIndex] * window;
                _imaginary[i] = 0f;
            }
            Fft(_real, _imaginary);

            float kickLow = BandFlux(35f, 80f);
            float kickPunch = BandFlux(80f, 180f);
            float snareBody = BandFlux(180f, 520f);
            float snareLowNoise = BandFlux(700f, 2800f);
            float snareHighNoise = BandFlux(2800f, 9000f);

            if (!_spectrumInitialized)
            {
                _kickLow.Initialize(kickLow);
                _kickPunch.Initialize(kickPunch);
                _snareBody.Initialize(snareBody);
                _snareLowNoise.Initialize(snareLowNoise);
                _snareHighNoise.Initialize(snareHighNoise);
                _spectrumInitialized = true;
                StoreMagnitude();
                return false;
            }

            float frameSeconds = HopSize / (float)_sampleRate;
            float kickLowScore = _kickLow.Evaluate(kickLow, SpectralThreshold, frameSeconds);
            float kickPunchScore = _kickPunch.Evaluate(kickPunch, SpectralThreshold, frameSeconds);
            float bodyScore = _snareBody.Evaluate(snareBody, SpectralThreshold, frameSeconds);
            float lowNoiseScore = _snareLowNoise.Evaluate(snareLowNoise, SpectralThreshold, frameSeconds);
            float highNoiseScore = _snareHighNoise.Evaluate(snareHighNoise, SpectralThreshold, frameSeconds);
            StoreMagnitude();

            float kickCoherence = (float)Math.Sqrt(kickLowScore * kickPunchScore);
            float kick = Clamp01(Math.Max(kickLowScore, kickPunchScore * 0.9f) * 0.7f + kickCoherence * 0.3f);
            float broadNoise = Math.Min(lowNoiseScore, highNoiseScore) * 0.55f +
                               (float)Math.Sqrt(lowNoiseScore * highNoiseScore) * 0.45f;
            float snare = Clamp01(broadNoise * 0.85f + Math.Min(broadNoise, bodyScore) * 0.3f);
            snare *= 1f - kick * 0.4f;

            if (!_candidatePending || (kick < 0.12f && snare < 0.12f)) return false;

            onset.SampleIndex = _candidateSample;
            onset.Kick = kick;
            onset.SnareClap = snare;
            _candidatePending = false;
            return true;
        }

        private float BandFlux(float minFrequency, float maxFrequency)
        {
            int from = Math.Max(1, (int)(minFrequency * FftSize / _sampleRate));
            int to = Math.Min(FftSize / 2, Math.Max(from + 1, (int)Math.Ceiling(maxFrequency * FftSize / _sampleRate)));
            float flux = 0f;
            for (int i = from; i < to; i++)
            {
                float magnitude = 2f * (float)Math.Sqrt(_real[i] * _real[i] + _imaginary[i] * _imaginary[i]) / FftSize;
                flux += Math.Max(0f, magnitude - _previousMagnitude[i]);
            }
            return flux / (to - from);
        }

        private void StoreMagnitude()
        {
            for (int i = 0; i < _previousMagnitude.Length; i++)
                _previousMagnitude[i] = 2f * (float)Math.Sqrt(_real[i] * _real[i] + _imaginary[i] * _imaginary[i]) / FftSize;
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
                float value = real[i]; real[i] = real[j]; real[j] = value;
                value = imaginary[i]; imaginary[i] = imaginary[j]; imaginary[j] = value;
            }

            for (int length = 2; length <= n; length <<= 1)
            {
                float angle = -2f * (float)Math.PI / length;
                float lengthReal = (float)Math.Cos(angle);
                float lengthImaginary = (float)Math.Sin(angle);
                for (int start = 0; start < n; start += length)
                {
                    float waveReal = 1f;
                    float waveImaginary = 0f;
                    for (int offset = 0; offset < length / 2; offset++)
                    {
                        int even = start + offset;
                        int odd = even + length / 2;
                        float oddReal = real[odd] * waveReal - imaginary[odd] * waveImaginary;
                        float oddImaginary = real[odd] * waveImaginary + imaginary[odd] * waveReal;
                        real[odd] = real[even] - oddReal;
                        imaginary[odd] = imaginary[even] - oddImaginary;
                        real[even] += oddReal;
                        imaginary[even] += oddImaginary;
                        float nextReal = waveReal * lengthReal - waveImaginary * lengthImaginary;
                        waveImaginary = waveReal * lengthImaginary + waveImaginary * lengthReal;
                        waveReal = nextReal;
                    }
                }
            }
        }

        private static float Clamp01(float value) { return Math.Max(0f, Math.Min(1f, value)); }

        private struct AdaptiveState
        {
            private float _mean;
            private float _variance;

            public void Initialize(float value)
            {
                _mean = value;
                _variance = 0.0000000001f;
            }

            public float Evaluate(float value, float threshold, float frameSeconds)
            {
                float deviation = (value - _mean) / (float)Math.Sqrt(Math.Max(0.0000000001f, _variance));
                float score = Clamp01((deviation - threshold) / 3f);
                float adaptation = 1f - (float)Math.Exp(-frameSeconds / 0.9f);
                if (score > 0.1f) adaptation *= 0.15f;
                float difference = value - _mean;
                _mean += difference * adaptation;
                _variance += (difference * difference - _variance) * adaptation;
                return score;
            }
        }
    }
}
