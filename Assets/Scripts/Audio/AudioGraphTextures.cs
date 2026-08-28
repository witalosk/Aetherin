using System;
using UnityEngine;

namespace Aetherin
{
    internal sealed class AudioGraphTextures : IDisposable
    {
        public const int Width = 360;
        public const int WaveformHeight = 96;
        public const int SpectrumHeight = 128;

        public Texture WaveformTexture => _waveformTexture;
        public Texture SpectrumTexture => _spectrumTexture;

        private static readonly Color32 Background = new(8, 12, 18, 255);
        private static readonly Color32 Grid = new(34, 45, 58, 255);
        private static readonly Color32 WaveColor = new(63, 220, 255, 255);
        private static readonly Color32 SpectrumColor = new(255, 174, 58, 255);
        private static readonly Color32 SpectrumFill = new(72, 46, 18, 255);

        private readonly Texture2D _waveformTexture;
        private readonly Texture2D _spectrumTexture;
        private readonly Color32[] _waveformPixels = new Color32[Width * WaveformHeight];
        private readonly Color32[] _spectrumPixels = new Color32[Width * SpectrumHeight];

        public AudioGraphTextures()
        {
            _waveformTexture = CreateTexture(WaveformHeight, "WASAPI Waveform Monitor");
            _spectrumTexture = CreateTexture(SpectrumHeight, "WASAPI Spectrum Monitor");
            Clear();
        }

        public void Update(float[] waveform, float[] logSpectrum)
        {
            DrawWaveform(waveform);
            DrawSpectrum(logSpectrum);
        }

        public void Clear()
        {
            Prepare(_waveformPixels, WaveformHeight);
            Prepare(_spectrumPixels, SpectrumHeight);
            Upload(_waveformTexture, _waveformPixels);
            Upload(_spectrumTexture, _spectrumPixels);
        }

        public void Dispose()
        {
            if (_waveformTexture != null) UnityEngine.Object.Destroy(_waveformTexture);
            if (_spectrumTexture != null) UnityEngine.Object.Destroy(_spectrumTexture);
        }

        private void DrawWaveform(float[] waveform)
        {
            Prepare(_waveformPixels, WaveformHeight);
            if (waveform.Length > 0)
            {
                int previousY = ToWaveY(waveform[0]);
                for (int x = 1; x < Width; x++)
                {
                    int index = x * (waveform.Length - 1) / (Width - 1);
                    int y = ToWaveY(waveform[index]);
                    DrawLine(_waveformPixels, WaveformHeight, x - 1, previousY, x, y, WaveColor);
                    previousY = y;
                }
            }
            Upload(_waveformTexture, _waveformPixels);
        }

        private void DrawSpectrum(float[] spectrum)
        {
            Prepare(_spectrumPixels, SpectrumHeight);
            int previousY = 0;
            for (int x = 0; x < Width && spectrum.Length > 0; x++)
            {
                int from = x * spectrum.Length / Width;
                int to = Mathf.Max(from + 1, (x + 1) * spectrum.Length / Width);
                float value = 0f;
                for (int i = from; i < to && i < spectrum.Length; i++) value = Mathf.Max(value, spectrum[i]);

                int y = Mathf.RoundToInt(Mathf.Clamp01(value) * (SpectrumHeight - 1));
                DrawLine(_spectrumPixels, SpectrumHeight, x, 0, x, y, SpectrumFill);
                if (x > 0) DrawLine(_spectrumPixels, SpectrumHeight, x - 1, previousY, x, y, SpectrumColor);
                previousY = y;
            }
            Upload(_spectrumTexture, _spectrumPixels);
        }

        private static Texture2D CreateTexture(int height, string name) =>
            new(Width, height, TextureFormat.RGBA32, false, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

        private static void Prepare(Color32[] pixels, int height)
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Background;
            for (int division = 0; division <= 4; division++)
            {
                int y = division * (height - 1) / 4;
                for (int x = 0; x < Width; x++) pixels[y * Width + x] = Grid;
            }
        }

        private static void Upload(Texture2D texture, Color32[] pixels)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private static int ToWaveY(float value) =>
            Mathf.RoundToInt((Mathf.Clamp(value, -1f, 1f) * 0.5f + 0.5f) * (WaveformHeight - 1));

        private static void DrawLine(Color32[] pixels, int height, int x0, int y0, int x1, int y1, Color32 color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int error = dx - dy;

            while (true)
            {
                if (x0 >= 0 && x0 < Width && y0 >= 0 && y0 < height) pixels[y0 * Width + x0] = color;
                if (x0 == x1 && y0 == y1) return;
                int twice = error * 2;
                if (twice > -dy) { error -= dy; x0 += sx; }
                if (twice < dx) { error += dx; y0 += sy; }
            }
        }
    }
}
