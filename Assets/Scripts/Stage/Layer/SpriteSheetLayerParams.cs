using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    /// <summary>スプライトのRGBをそのまま使うか、明度をマスクとして使うかを選ぶ。</summary>
    public enum SpriteSheetColorMode
    {
        Texture,
        AccentMask,
    }

    [Serializable]
    public sealed class SpriteSheetLayerParams : StageLayerParams
    {
        [Tooltip("Texture Library に登録したキー")]
        public string SpriteSheetKey;
        [Min(1)] public int FrameCount = 1;
        [Min(1)] public int RowCount = 1;
        [Tooltip("縦方向のアニメーション行。0が画像の最上段です")]
        public IntParameter AnimationRow = new(0);
        [Tooltip("有効時はFPSでフレームを進めます。無効時はCurrent Frameを直接使用します")]
        public bool PlayAnimation = true;
        [Min(0f)] public FloatParameter FramesPerSecond = new(12f);
        [Tooltip("Play Animationが無効なときに表示する横方向のフレーム")]
        public IntParameter CurrentFrame = new(0);
        public bool Loop = true;
        [Min(0)] public int StartFrame;
        public bool PreserveAspect = true;
        public SpriteSheetColorMode ColorMode;

        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public Vector2Parameter Size = new(new Vector2(2f, 2f));
        public PaletteColorParameter Color = new() { ColorReference = PaletteColorReference.Custom, CustomColor = UnityEngine.Color.white };
        [Tooltip("Accent Mask時に白い部分へ適用する色")]
        public PaletteColorParameter AccentColor = new();

        [NonSerialized] public Func<IReadOnlyList<string>> GetAvailableSpriteSheetKeys;

        public void EnsureInitialized()
        {
            SpriteSheetKey ??= string.Empty;
            Opacity ??= new FloatParameter(1f);
            FramesPerSecond ??= new FloatParameter(12f);
            AnimationRow ??= new IntParameter(0);
            CurrentFrame ??= new IntParameter(0);
            Position ??= new Vector3Parameter();
            Rotation ??= new Vector3Parameter();
            Scale ??= new Vector3Parameter(Vector3.one);
            Anchor ??= new Vector3Parameter();
            Size ??= new Vector2Parameter(new Vector2(2f, 2f));
            Color ??= new PaletteColorParameter { ColorReference = PaletteColorReference.Custom, CustomColor = UnityEngine.Color.white };
            AccentColor ??= new PaletteColorParameter();
            Color.EnsureInitialized();
            AccentColor.EnsureInitialized();
            FrameCount = Mathf.Max(1, FrameCount);
            RowCount = Mathf.Max(1, RowCount);
            StartFrame = Mathf.Max(0, StartFrame);
        }
    }
}
