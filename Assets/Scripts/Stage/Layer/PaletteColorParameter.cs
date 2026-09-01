using System;
using UnityEngine;

namespace Aetherin
{
    public enum PaletteColorSource
    {
        BackgroundColor1,
        BackgroundColor2,
        AccentColor1,
        AccentColor2,
        SubAccentColor1,
        SubAccentColor2,
    }

    public enum PaletteColorMode
    {
        Single,
        Gradient,
    }

    /// <summary>
    /// ColorPaletteの色を参照する色指定
    /// 単色か、パレットの2色を結ぶグラデーション (シェイプ空間の線形グラデーション) かを選べる
    /// 向き・オフセット・強度などはFloatParameterとしてModulationでアニメーションできる
    /// </summary>
    [Serializable]
    public class PaletteColorParameter
    {
        public PaletteColorMode Mode;

        public PaletteColorSource Color = PaletteColorSource.AccentColor1;

        public PaletteColorSource GradientColorA = PaletteColorSource.AccentColor1;
        public PaletteColorSource GradientColorB = PaletteColorSource.AccentColor2;

        [Tooltip("グラデーションの向き (度)")]
        public FloatParameter GradientAngle = new(0f);

        [Tooltip("グラデーション中心のずらし (シェイプ空間)")]
        public FloatParameter GradientOffset = new(0f);

        [Tooltip("グラデーションが横切る幅 (シェイプ空間)")]
        public FloatParameter GradientScale = new(2f);

        [Tooltip("色に掛ける強度。1を超えるとHDRとして扱われる")]
        public FloatParameter Intensity = new(1f);

        public FloatParameter Alpha = new(1f);

        /// <summary> パレットが参照できない場合 (エディタ編集時など) のプレビュー用 </summary>
        public static readonly ColorPalette FallbackPalette = new()
        {
            Name = "Fallback",
            BackgroundColor1 = UnityEngine.Color.black,
            BackgroundColor2 = new Color(0.25f, 0.25f, 0.25f),
            AccentColor1 = UnityEngine.Color.white,
            AccentColor2 = new Color(0.7f, 0.7f, 0.7f),
            SubAccentColor1 = new Color(0.5f, 0.5f, 0.5f),
            SubAccentColor2 = new Color(0.3f, 0.3f, 0.3f),
        };

        public void EnsureInitialized()
        {
            GradientAngle ??= new FloatParameter(0f);
            GradientOffset ??= new FloatParameter(0f);
            GradientScale ??= new FloatParameter(2f);
            Intensity ??= new FloatParameter(1f);
            Alpha ??= new FloatParameter(1f);
        }

        public static Color Resolve(ColorPalette palette, PaletteColorSource source)
        {
            palette ??= FallbackPalette;
            return source switch
            {
                PaletteColorSource.BackgroundColor1 => palette.BackgroundColor1,
                PaletteColorSource.BackgroundColor2 => palette.BackgroundColor2,
                PaletteColorSource.AccentColor1 => palette.AccentColor1,
                PaletteColorSource.AccentColor2 => palette.AccentColor2,
                PaletteColorSource.SubAccentColor1 => palette.SubAccentColor1,
                PaletteColorSource.SubAccentColor2 => palette.SubAccentColor2,
                _ => palette.AccentColor1,
            };
        }
    }

    /// <summary>
    /// PaletteColorParameterを1フレーム分評価した結果
    /// </summary>
    public struct EvaluatedPaletteColor
    {
        public Color ColorA;
        public Color ColorB;
        public bool IsGradient;
        public float AngleDegrees;
        public float Offset;
        public float Scale;

        public static EvaluatedPaletteColor Evaluate(
            PaletteColorParameter parameter,
            ColorPalette palette,
            in ModulationContext context)
        {
            if (parameter == null)
            {
                return new EvaluatedPaletteColor { ColorA = UnityEngine.Color.white, ColorB = UnityEngine.Color.white };
            }

            float intensity = Mathf.Max(0f, parameter.Intensity?.Evaluate(context) ?? 1f);
            float alpha = Mathf.Clamp01(parameter.Alpha?.Evaluate(context) ?? 1f);

            var colorA = ToOutputColor(
                PaletteColorParameter.Resolve(palette,
                    parameter.Mode == PaletteColorMode.Single ? parameter.Color : parameter.GradientColorA),
                intensity, alpha);

            if (parameter.Mode != PaletteColorMode.Gradient)
            {
                return new EvaluatedPaletteColor { ColorA = colorA, ColorB = colorA };
            }

            return new EvaluatedPaletteColor
            {
                ColorA = colorA,
                ColorB = ToOutputColor(PaletteColorParameter.Resolve(palette, parameter.GradientColorB), intensity, alpha),
                IsGradient = true,
                AngleDegrees = parameter.GradientAngle?.Evaluate(context) ?? 0f,
                Offset = parameter.GradientOffset?.Evaluate(context) ?? 0f,
                Scale = Mathf.Max(0.0001f, parameter.GradientScale?.Evaluate(context) ?? 2f),
            };
        }

        private static Color ToOutputColor(Color color, float intensity, float alpha)
        {
            var result = color.linear * intensity;
            result.a = alpha;
            return result;
        }
    }
}
