using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.VFX;

namespace Aetherin
{
    /// <summary>
    /// CameraStage が利用するシーン共通アセットを解決する窓口。
    /// SceneContainer が IStageAssetCatalog として自動バインドできるよう、
    /// シーン内には原則このコンポーネントを 1 つ配置する。
    /// </summary>
    public interface IStageAssetCatalog
    {
        GameObject ResolveModel(string key);
        IReadOnlyList<string> GetModelKeys();
        Texture2D ResolveTexture(string key);
        IReadOnlyList<string> GetTextureKeys();
        Texture2D ResolveLut(string key);
        IReadOnlyList<string> GetLutKeys();
        VisualEffectAsset ResolveVfxGraph(string key);
        IReadOnlyList<string> GetVfxGraphKeys();
        TMP_FontAsset ResolveFontAsset(string key);
        IReadOnlyList<string> GetFontAssetKeys();
    }

    /// <summary>
    /// Texture / LUT / Model / VFX / Font の各ライブラリを明示的に束ねる。
    /// 未設定の項目だけを旧シーン互換として遅延探索するため、CameraStage は
    /// 個別ライブラリの存在場所を知る必要がない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageAssetCatalog : MonoBehaviour, IStageAssetCatalog
    {
        [SerializeField] private ModelLayerLibrary _modelLibrary;
        [SerializeField] private TextureLibrary _textureLibrary;
        [SerializeField] private LutLibrary _lutLibrary;
        [SerializeField] private VfxGraphLibrary _vfxGraphLibrary;
        [SerializeField] private FontAssetLibrary _fontAssetLibrary;

        private static readonly IStageAssetCatalog FallbackCatalog = new FallbackStageAssetCatalog();

        /// <summary>
        /// DI でカタログが渡されない旧シーン向けの入口。探索と互換処理はここに閉じ込める。
        /// </summary>
        public static IStageAssetCatalog FindBestAvailable(IStageAssetCatalog preferred = null)
        {
            if (preferred != null) return preferred;

            StageAssetCatalog catalog = FindFirstObjectByType<StageAssetCatalog>(FindObjectsInactive.Include);
            return catalog != null ? catalog : FallbackCatalog;
        }

        public GameObject ResolveModel(string key) => ResolveLibrary(ref _modelLibrary)?.Resolve(key);
        public IReadOnlyList<string> GetModelKeys() => ResolveLibrary(ref _modelLibrary)?.GetKeys() ?? Array.Empty<string>();

        public Texture2D ResolveTexture(string key) => ResolveLibrary(ref _textureLibrary)?.Resolve(key);
        public IReadOnlyList<string> GetTextureKeys() => ResolveLibrary(ref _textureLibrary)?.GetKeys() ?? Array.Empty<string>();

        public Texture2D ResolveLut(string key) => ResolveLibrary(ref _lutLibrary)?.Resolve(key);
        public IReadOnlyList<string> GetLutKeys() => ResolveLibrary(ref _lutLibrary)?.GetKeys() ?? Array.Empty<string>();

        public VisualEffectAsset ResolveVfxGraph(string key) => ResolveLibrary(ref _vfxGraphLibrary)?.Resolve(key);
        public IReadOnlyList<string> GetVfxGraphKeys() => ResolveLibrary(ref _vfxGraphLibrary)?.GetKeys() ?? Array.Empty<string>();

        public TMP_FontAsset ResolveFontAsset(string key) => ResolveLibrary(ref _fontAssetLibrary)?.Resolve(key);
        public IReadOnlyList<string> GetFontAssetKeys() => ResolveLibrary(ref _fontAssetLibrary)?.GetKeys() ?? Array.Empty<string>();

        private static T ResolveLibrary<T>(ref T library) where T : UnityEngine.Object
        {
            if (library == null)
                library = FindFirstObjectByType<T>(FindObjectsInactive.Include);
            return library;
        }

        /// <summary>
        /// StageAssetCatalog 導入前のシーンを開いた場合に限り利用する。
        /// 個別ライブラリの探索をこの互換実装へ集約する。
        /// </summary>
        private sealed class FallbackStageAssetCatalog : IStageAssetCatalog
        {
            private ModelLayerLibrary _modelLibrary;
            private TextureLibrary _textureLibrary;
            private LutLibrary _lutLibrary;
            private VfxGraphLibrary _vfxGraphLibrary;
            private FontAssetLibrary _fontAssetLibrary;

            public GameObject ResolveModel(string key) => ResolveLibrary(ref _modelLibrary)?.Resolve(key);
            public IReadOnlyList<string> GetModelKeys() => ResolveLibrary(ref _modelLibrary)?.GetKeys() ?? Array.Empty<string>();

            public Texture2D ResolveTexture(string key) => ResolveLibrary(ref _textureLibrary)?.Resolve(key);
            public IReadOnlyList<string> GetTextureKeys() => ResolveLibrary(ref _textureLibrary)?.GetKeys() ?? Array.Empty<string>();

            public Texture2D ResolveLut(string key) => ResolveLibrary(ref _lutLibrary)?.Resolve(key);
            public IReadOnlyList<string> GetLutKeys() => ResolveLibrary(ref _lutLibrary)?.GetKeys() ?? Array.Empty<string>();

            public VisualEffectAsset ResolveVfxGraph(string key) => ResolveLibrary(ref _vfxGraphLibrary)?.Resolve(key);
            public IReadOnlyList<string> GetVfxGraphKeys() => ResolveLibrary(ref _vfxGraphLibrary)?.GetKeys() ?? Array.Empty<string>();

            public TMP_FontAsset ResolveFontAsset(string key) => ResolveLibrary(ref _fontAssetLibrary)?.Resolve(key);
            public IReadOnlyList<string> GetFontAssetKeys() => ResolveLibrary(ref _fontAssetLibrary)?.GetKeys() ?? Array.Empty<string>();
        }
    }
}
