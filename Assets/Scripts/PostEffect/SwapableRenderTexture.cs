using System;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// 2枚のRenderTextureを読み取り側と書き込み側として交互に使用する。
    /// </summary>
    public sealed class SwapableRenderTexture : IDisposable
    {
        private RenderTexture _first;
        private RenderTexture _second;
        private bool _swapped;

        public RenderTexture Read => _swapped ? _second : _first;
        public RenderTexture Write => _swapped ? _first : _second;

        public bool Matches(int width, int height) =>
            _first != null && _second != null &&
            _first.width == width && _first.height == height &&
            _second.width == width && _second.height == height;

        public void Ensure(int width, int height, string name,
            RenderTextureFormat format = RenderTextureFormat.ARGB32)
        {
            if (Matches(width, height)) return;

            Dispose();
            _first = Create(width, height, $"{name} A", format);
            _second = Create(width, height, $"{name} B", format);
            _swapped = false;
        }

        /// <summary>
        /// inputが内部RTならそれをRead側に合わせ、Writeがinputと重ならないようにする。
        /// 外部Textureの場合は現在の向きを維持する。
        /// </summary>
        public void Reset(Texture input)
        {
            if (ReferenceEquals(input, _first)) _swapped = false;
            else if (ReferenceEquals(input, _second)) _swapped = true;
        }

        public void Swap() => _swapped = !_swapped;

        public void Dispose()
        {
            Release(_first);
            Release(_second);
            _first = null;
            _second = null;
            _swapped = false;
        }

        private static RenderTexture Create(int width, int height, string name,
            RenderTextureFormat format)
        {
            var texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.sRGB)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.Create();
            return texture;
        }

        private static void Release(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
