using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Aetherin
{
    public enum TextSelectorBasedOn
    {
        Characters,
        CharactersExcludingSpaces,
        Words,
        Lines,
    }

    public enum TextSelectorShape
    {
        Square,
        RampUp,
        RampDown,
        Triangle,
        Smooth,
    }

    public enum TextLayoutMode
    {
        Linear,
        Circle,
        Arc,
    }

    public enum TextSource
    {
        Manual,
        TextManager,
        LocalClock,
        Timecode,
        BeatCounter,
        InputVolume,
        Fps,
        TextManagerSelection,
    }

    [Serializable]
    public sealed class TextRangeSelectorParams
    {
        public TextSelectorBasedOn BasedOn;
        public TextSelectorShape Shape = TextSelectorShape.Square;
        public FloatParameter Start = new(0f);
        public FloatParameter End = new(100f);
        public FloatParameter Offset = new(0f);
        [Range(0f, 100f)] public FloatParameter Smoothness = new(100f);
        public bool RandomizeOrder;
        public int RandomSeed;
    }

    [Serializable]
    public sealed class TextAnimatorParams
    {
        public bool Enabled = true;
        public string Name = "Animator";
        public TextRangeSelectorParams Selector = new();
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        [Range(0f, 1f)] public FloatParameter Opacity = new(1f);
        public PaletteColorParameter Color = new();
        [Range(0f, 1f)] public FloatParameter ColorAmount = new(0f);

        [Tooltip("文字ごとにLFO / Beat / Barなどの位相をずらす量。1で1周期です")]
        public FloatParameter AnimationPhaseOffset = new(0f);

        public void EnsureInitialized()
        {
            Selector ??= new TextRangeSelectorParams();
            Position ??= new Vector3Parameter();
            Rotation ??= new Vector3Parameter();
            Scale ??= new Vector3Parameter(Vector3.one);
            Opacity ??= new FloatParameter(1f);
            Color ??= new PaletteColorParameter();
            Color.EnsureInitialized();
            ColorAmount ??= new FloatParameter(0f);
            AnimationPhaseOffset ??= new FloatParameter(0f);
        }
    }

    [Serializable]
    public sealed class TextLayerParams : StageLayerParams
    {
        public bool ScreenSpace;

        public TextSource Source;
        [TextArea] public string Text = "Aetherin";
        [Tooltip("TextManagerに登録したテキストリストのキー")]
        public string TextManagerKey = "default";
        [Tooltip("TextManagerのリストから選ぶindex。Modulationで切り替えられます")]
        public IntParameter TextManagerIndex = new(0);
        [Tooltip("Local Clock用のDateTime書式")]
        public string ClockFormat = "HH:mm:ss";
        [Tooltip("Timecodeの小数秒桁数")]
        [Range(0, 3)] public int TimecodeDecimals = 2;
        [Tooltip("CameraStageのFont Asset Libraryに登録したキー")]
        public string FontAssetKey;
        [Tooltip("OSにインストールされたフォントファミリー名")]
        public string FontFamily = "Arial";
        public string FontStyle = "Regular";

        public FloatParameter FontSize = new(1f);
        public FloatParameter CharacterSpacing = new(0f);
        public FloatParameter WordSpacing = new(0f);
        public FloatParameter LineSpacing = new(0f);
        public TextAlignmentOptions Alignment = TextAlignmentOptions.Center;

        public TextLayoutMode Layout;
        public FloatParameter PathRadius = new(3f);
        public FloatParameter PathStartAngle = new(90f);
        public FloatParameter PathEndAngle = new(-90f);
        public bool PathClockwise = true;
        public bool OrientToPath = true;
        public FloatParameter PathRotationOffset = new(0f);

        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public PaletteColorParameter Color = new();

        public List<TextAnimatorParams> Animators = new()
        {
            new TextAnimatorParams
            {
                Name = "Animator 1",
                Selector = new TextRangeSelectorParams
                {
                    Shape = TextSelectorShape.RampUp,
                    Start = new FloatParameter(0f),
                    End = new FloatParameter(100f),
                },
            },
        };

        [NonSerialized] public Func<IReadOnlyList<string>> GetAvailableFontAssetKeys;
        [NonSerialized] public Func<IReadOnlyList<string>> GetAvailableTextManagerKeys;

        public void EnsureInitialized()
        {
            Text ??= string.Empty;
            TextManagerKey ??= string.Empty;
            TextManagerIndex ??= new IntParameter(0);
            ClockFormat ??= "HH:mm:ss";
            FontFamily ??= "Arial";
            FontStyle ??= "Regular";
            FontSize ??= new FloatParameter(1f);
            CharacterSpacing ??= new FloatParameter(0f);
            WordSpacing ??= new FloatParameter(0f);
            LineSpacing ??= new FloatParameter(0f);
            PathRadius ??= new FloatParameter(3f);
            PathStartAngle ??= new FloatParameter(90f);
            PathEndAngle ??= new FloatParameter(-90f);
            PathRotationOffset ??= new FloatParameter(0f);
            Position ??= new Vector3Parameter();
            Rotation ??= new Vector3Parameter();
            Scale ??= new Vector3Parameter(Vector3.one);
            Anchor ??= new Vector3Parameter();
            Color ??= new PaletteColorParameter();
            Color.EnsureInitialized();
            Animators ??= new List<TextAnimatorParams>();
            foreach (var animator in Animators) animator?.EnsureInitialized();
        }
    }
}
