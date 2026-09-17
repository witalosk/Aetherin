using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    public enum MoviePathMode
    {
        LocalFileSystem,
        StreamingAssets,
    }

    [Serializable]
    public sealed class MovieLayerParams : StageLayerParams
    {
        [Tooltip("カメラワーク番号に対応するHAP動画パス。末尾まで進むと先頭へ戻ります")]
        public List<string> VideoPaths = new();
        public MoviePathMode PathMode = MoviePathMode.LocalFileSystem;
        public bool Loop = true;
        public float PlaybackSpeed = 1f;
        public bool PreserveAspect = true;
        public bool LutEnabled;
        [Tooltip("Lut Library に登録したLUTテクスチャのファイル名（拡張子なし）")]
        public string LutKey;
        [Tooltip("元映像とLUT適用結果の混合率")]
        public FloatParameter LutIntensity = new(1f);

        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public Vector2Parameter Size = new(new Vector2(2f, 2f));

        [NonSerialized] public Func<IReadOnlyList<string>> GetAvailableLutKeys;

        public void EnsureInitialized()
        {
            VideoPaths ??= new List<string>();
            LutKey ??= string.Empty;
            LutIntensity ??= new FloatParameter(1f);
            Opacity ??= new FloatParameter(1f);
            Position ??= new Vector3Parameter();
            Rotation ??= new Vector3Parameter();
            Scale ??= new Vector3Parameter(Vector3.one);
            Anchor ??= new Vector3Parameter();
            Size ??= new Vector2Parameter(new Vector2(2f, 2f));
        }
    }
}
