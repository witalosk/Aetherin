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
        Bloom,
    }

    public enum PostEffectControlMode
    {
        Fader,
        OutputPad,
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

        public bool DepthOfFieldEnabled = true;
        [Tooltip("Depth Of Fieldの有効/無効を切り替えるMIDIパッド")]
        public MidiBinding DepthOfFieldToggleButton = new();
        public VolumeDepthOfFieldMode DepthOfFieldMode = VolumeDepthOfFieldMode.Bokeh;
        public FloatParameter FocusDistance = new(10f);
        public FloatParameter Aperture = new(5.6f);
        public FloatParameter FocalLength = new(50f);

        public void EnsureInitialized()
        {
            BloomToggleButton ??= new MidiBinding();
            BloomIntensity ??= new FloatParameter(1f);
            BloomThreshold ??= new FloatParameter(0.9f);
            BloomScatter ??= new FloatParameter(0.5f);
            DepthOfFieldToggleButton ??= new MidiBinding();
            FocusDistance ??= new FloatParameter(10f);
            Aperture ??= new FloatParameter(5.6f);
            FocalLength ??= new FloatParameter(50f);
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
        [Tooltip("押している間だけ最終OutputへこのDeckを適用するPad")]
        public MidiBinding OutputPad = new();
        [HideInInspector] public float CurrentFaderValue = 1f;
        public System.Collections.Generic.List<PostEffectModule> Modules = new();

        public void EnsureInitialized()
        {
            Strength ??= new FloatParameter(1f);
            Fader ??= new MidiCcBinding();
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
        [HideInInspector]
        public PostEffectStack Current = new();
        public PostEffectStack Next = new();
        [HideInInspector]
        public DeckVolumeEffects CurrentVolume = new();
        public DeckVolumeEffects NextVolume = new();
    }
}
