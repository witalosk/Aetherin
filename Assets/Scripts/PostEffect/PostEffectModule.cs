using System;
using UnityEngine;

namespace Aetherin
{
    public enum PostEffectType
    {
        ChromaticAberration,
        PreviousFrameBlend,
        DomainWarp,
        ScreenShake,
        Kaleidoscope,
        Pixelate,
        Scanline,
        Posterize,
        Invert,
        CrossFilter,
        LedDisplay,
        HorizontalFold,
        HashInvertBlocks,
        Grid,
        Noise,
        BlockGlitch,
        HsvLevels,
        Shutter,
        HandDrawn,
        LightLeak,
        Lut,
        RuntimeShader,
    }

    public enum ShutterMode
    {
        TopBottom,
        LeftRight,
        Circle,
    }

    public enum PostEffectControlMode
    {
        Fader,
        OutputPad,
    }

    public enum PostEffectEditMode
    {
        Next,
        Immediate,
    }

    public enum VolumeDepthOfFieldMode
    {
        Gaussian,
        Bokeh,
    }

    /// <summary>
    /// URP Volumeに渡す、デッキごとのポストエフェクト設定。
    /// SSRなどProfileにもともと存在する成分はここでは触らない。
    /// </summary>
    [Serializable]
    public sealed class DeckVolumeEffects
    {
        public bool BloomEnabled = true;
        [Tooltip("Bloomの有効/無効を切り替えるMIDIパッド")]
        public MidiBinding BloomToggleButton = new();
        public FloatParameter BloomIntensity = new(1f);
        public FloatParameter BloomThreshold = new(0.9f);
        public FloatParameter BloomScatter = new(0.5f);

        public VolumeDepthOfFieldMode DepthOfFieldMode = VolumeDepthOfFieldMode.Bokeh;

        public void EnsureInitialized()
        {
            BloomToggleButton ??= new MidiBinding();
            BloomIntensity ??= new FloatParameter(1f);
            BloomThreshold ??= new FloatParameter(0.9f);
            BloomScatter ??= new FloatParameter(0.5f);
        }
    }

    /// <summary>
    /// リスト内で並べ替え・保存できるポストエフェクト1段分。
    /// パラメータは型ごとにUIで必要なものだけ表示する。
    /// </summary>
    [Serializable]
    public sealed class PostEffectModule
    {
        public bool Enabled = true;
        public PostEffectType Type;
        [Range(0f, 1f)] public FloatParameter Strength = new(1f);

        [Tooltip("エフェクトごとの主パラメータ")]
        public FloatParameter Amount = new(0.02f);
        [Tooltip("エフェクトごとのスケール／分割数")]
        public FloatParameter Scale = new(4f);
        [Tooltip("時間変化の速さ")]
        public FloatParameter Speed = new(1f);
        [Tooltip("エフェクトごとの補助パラメータ")]
        public FloatParameter Secondary = new(0.5f);

        [Header("Cross Filter")]
        [Tooltip("光条を生成する最低輝度")]
        public FloatParameter CrossFilterThreshold = new(1f);
        [Tooltip("抽出した高輝度成分の露出")]
        public FloatParameter CrossFilterExposure = new(1f);
        [Tooltip("放射状に生成する光条の本数")]
        public IntParameter CrossFilterLineCount = new(4);
        [Tooltip("8タップ再帰ブラーの反復回数")]
        public IntParameter CrossFilterPassCount = new(3);
        [Tooltip("光条のサンプル間隔")]
        public FloatParameter CrossFilterSampleLength = new(1f);
        [Tooltip("光条の減衰率")]
        public FloatParameter CrossFilterAttenuation = new(0.85f);
        [Tooltip("光条全体の回転角（度）")]
        public FloatParameter CrossFilterRotation = new(0f);

        [Tooltip("HSV Levels: 色相の回転（-1..1 が1周）")] public FloatParameter Hue = new(0f);
        [Tooltip("HSV Levels: 彩度の倍率")] public FloatParameter Saturation = new(1f);
        [Tooltip("HSV Levels: 明度の倍率")] public FloatParameter Value = new(1f);
        [Tooltip("HSV Levels: 入力の黒点")] public FloatParameter BlackLevel = new(0f);
        [Tooltip("HSV Levels: 入力の白点")] public FloatParameter WhiteLevel = new(1f);
        [Tooltip("HSV Levels: ガンマ")] public FloatParameter Gamma = new(1f);
        [Tooltip("Shutter: 閉じる方向")] public ShutterMode ShutterMode;
        [Tooltip("Hand Drawn: 揺れを量子化するフレームレート")] public FloatParameter HandDrawnFrameRate = new(8f);
        [Tooltip("Light Leak: 横位置。0で左、1で右。Beat Accumulator の PingPong で左右交互にできます")]
        public FloatParameter LightLeakPosition = new(0.5f);
        [Tooltip("Light Leak: 色。パレット参照またはカスタム色を指定できます")]
        public PaletteColorParameter LightLeakColor = new()
        {
            ColorReference = PaletteColorReference.Custom,
            CustomColor = new Color(1f, 0.32f, 0.06f, 1f),
        };
        [Tooltip("Lut Library に登録したLUTテクスチャのファイル名（拡張子なし）")]
        public string LutKey;
        [Tooltip("LUT一覧のインデックス。Modulation後の値は有効範囲へClampされます")]
        public IntParameter LutIndex = new(0);
        [Tooltip("元映像とLUT適用結果の混合率")]
        public FloatParameter LutIntensity = new(1f);

        [Tooltip("ランタイムポストエフェクトの t3 (_PreviousFrameTexture) に1フレーム前の出力を渡します")]
        public bool RuntimeProvidePreviousFrameTexture;
        public FloatParameter RuntimeUserFloat0 = new(1f);
        public FloatParameter RuntimeUserFloat1 = new(0f);
        public FloatParameter RuntimeUserFloat2 = new(0f);
        public FloatParameter RuntimeUserFloat3 = new(0f);
        public Vector3Parameter RuntimeUserVector0 = new();
        public Vector3Parameter RuntimeUserVector1 = new();
        [TextArea(12, 40)] public string RuntimeShaderCode = RuntimeShaderPostEffectRenderer.DefaultShaderCode;

        [NonSerialized] public string RuntimeCompileMessage = "Play Modeでコンパイルされます";
        [NonSerialized] public bool RuntimeLastCompileSucceeded;

        [NonSerialized] public Func<System.Collections.Generic.IReadOnlyList<string>> GetAvailableLutKeys;
        [NonSerialized] private bool _lutIndexInitialized;

        public void InitializeLutIndex(System.Collections.Generic.IReadOnlyList<string> keys)
        {
            if (_lutIndexInitialized || keys == null || keys.Count == 0) return;
            _lutIndexInitialized = true;
            int savedIndex = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i] != LutKey) continue;
                savedIndex = i;
                break;
            }
            if (!string.IsNullOrWhiteSpace(LutKey)) LutIndex.BaseValue = savedIndex;
            LutKey = keys[Mathf.Clamp(LutIndex.BaseValue, 0, keys.Count - 1)];
        }

        public void EnsureInitialized()
        {
            Strength ??= new FloatParameter(1f); Amount ??= new FloatParameter(0.02f);
            Scale ??= new FloatParameter(4f); Speed ??= new FloatParameter(1f);
            Secondary ??= new FloatParameter(0.5f); Hue ??= new FloatParameter(0f);
            CrossFilterThreshold ??= new FloatParameter(1f);
            CrossFilterExposure ??= new FloatParameter(1f);
            CrossFilterLineCount ??= new IntParameter(4);
            CrossFilterPassCount ??= new IntParameter(3);
            CrossFilterSampleLength ??= new FloatParameter(1f);
            CrossFilterAttenuation ??= new FloatParameter(0.85f);
            CrossFilterRotation ??= new FloatParameter(0f);
            Saturation ??= new FloatParameter(1f); Value ??= new FloatParameter(1f);
            BlackLevel ??= new FloatParameter(0f); WhiteLevel ??= new FloatParameter(1f);
            Gamma ??= new FloatParameter(1f);
            HandDrawnFrameRate ??= new FloatParameter(8f);
            LightLeakPosition ??= new FloatParameter(0.5f);
            LightLeakColor ??= new PaletteColorParameter
            {
                ColorReference = PaletteColorReference.Custom,
                CustomColor = new Color(1f, 0.32f, 0.06f, 1f),
            };
            LightLeakColor.EnsureInitialized();
            LutKey ??= string.Empty;
            LutIndex ??= new IntParameter(0);
            LutIntensity ??= new FloatParameter(1f);
            RuntimeUserFloat0 ??= new FloatParameter(1f);
            RuntimeUserFloat1 ??= new FloatParameter();
            RuntimeUserFloat2 ??= new FloatParameter();
            RuntimeUserFloat3 ??= new FloatParameter();
            RuntimeUserVector0 ??= new Vector3Parameter();
            RuntimeUserVector1 ??= new Vector3Parameter();
            RuntimeShaderCode ??= RuntimeShaderPostEffectRenderer.DefaultShaderCode;
            RuntimeCompileMessage ??= string.Empty;
        }
    }

    [Serializable]
    public sealed class PostEffectDeck
    {
        public string Name = "Deck";
        public bool Enabled = true;
        [Range(0f, 1f)] public FloatParameter Strength = new(1f);
        [Tooltip("Faderは従来のCurrent / Next経路、Output Padは押下中だけ最終Outputへ適用")]
        public PostEffectControlMode ControlMode;
        [Tooltip("このDeck全体の強度を0..1で操作するフェーダー")]
        public MidiCcBinding Fader = new();
        [Tooltip("通常Deckの有効/無効を切り替えるMIDIパッド")]
        public MidiBinding ToggleButton = new();
        [Tooltip("押している間だけ最終OutputへこのDeckを適用するPad")]
        public MidiBinding OutputPad = new();
        [HideInInspector] public float CurrentFaderValue = 0f;
        public System.Collections.Generic.List<PostEffectModule> Modules = new();

        public void EnsureInitialized()
        {
            Strength ??= new FloatParameter(1f);
            Fader ??= new MidiCcBinding();
            ToggleButton ??= new MidiBinding();
            OutputPad ??= new MidiBinding();
            Modules ??= new System.Collections.Generic.List<PostEffectModule>();
        }
    }

    [Serializable]
    public sealed class PostEffectStack
    {
        public System.Collections.Generic.List<PostEffectDeck> Decks = new();
    }

    [Serializable]
    public sealed class PostEffectManagerParams : IParams
    {
        [Tooltip("Nextは次のデッキだけを編集し、ImmediateはCurrentの出力にも即時反映します")]
        public PostEffectEditMode EditMode;
        [HideInInspector]
        public PostEffectStack Current = new();
        public PostEffectStack Next = new();
        [HideInInspector]
        public DeckVolumeEffects CurrentVolume = new();
        public DeckVolumeEffects NextVolume = new();
    }
}
