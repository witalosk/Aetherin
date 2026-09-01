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
    public sealed class PostEffectStack
    {
        public bool Enabled = true;
        [Range(0f, 1f)] public FloatParameter Strength = new(1f);
        public System.Collections.Generic.List<PostEffectModule> Modules = new();
    }

    [Serializable]
    public sealed class PostEffectManagerParams
    {
        public PostEffectStack Output = new();
        public PostEffectStack Current = new();
        public PostEffectStack Next = new();
    }
}
