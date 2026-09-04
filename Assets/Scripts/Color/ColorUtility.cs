using UnityEngine;

namespace Aetherin
{
    public static class ColorUtility
    {
        public static string ToHtmlStringRGB(Color color) =>
            UnityEngine.ColorUtility.ToHtmlStringRGB(color);

        public static Color Invert(Color color) => new(
            1f - color.r,
            1f - color.g,
            1f - color.b,
            color.a);

        public static Color InvertHslLightness(Color color)
        {
            RgbToHsl(color, out float hue, out float saturation, out float lightness);
            Color result = HslToRgb(hue, saturation, 1f - lightness);
            result.a = color.a;
            return result;
        }

        private static void RgbToHsl(Color color, out float hue, out float saturation, out float lightness)
        {
            float r = Mathf.Clamp01(color.r);
            float g = Mathf.Clamp01(color.g);
            float b = Mathf.Clamp01(color.b);
            float max = Mathf.Max(r, g, b);
            float min = Mathf.Min(r, g, b);
            float delta = max - min;

            lightness = (max + min) * 0.5f;
            if (Mathf.Approximately(delta, 0f))
            {
                hue = 0f;
                saturation = 0f;
                return;
            }

            saturation = delta / (1f - Mathf.Abs(2f * lightness - 1f));
            if (Mathf.Approximately(max, r)) hue = (g - b) / delta;
            else if (Mathf.Approximately(max, g)) hue = (b - r) / delta + 2f;
            else hue = (r - g) / delta + 4f;
            hue = Mathf.Repeat(hue / 6f, 1f);
        }

        private static Color HslToRgb(float hue, float saturation, float lightness)
        {
            float chroma = (1f - Mathf.Abs(2f * lightness - 1f)) * saturation;
            float huePrime = Mathf.Repeat(hue, 1f) * 6f;
            float x = chroma * (1f - Mathf.Abs(huePrime % 2f - 1f));
            float match = lightness - chroma * 0.5f;

            Color rgb = huePrime switch
            {
                < 1f => new Color(chroma, x, 0f),
                < 2f => new Color(x, chroma, 0f),
                < 3f => new Color(0f, chroma, x),
                < 4f => new Color(0f, x, chroma),
                < 5f => new Color(x, 0f, chroma),
                _ => new Color(chroma, 0f, x),
            };
            rgb.r += match;
            rgb.g += match;
            rgb.b += match;
            return rgb;
        }
    }
}
