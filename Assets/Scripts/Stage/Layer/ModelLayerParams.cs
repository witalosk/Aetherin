using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    public enum ModelLayerRenderMode
    {
        Surface,
        Wireframe,
        SurfaceAndWireframe,
    }

    [Serializable]
    public sealed class ModelLayerParams : StageLayerParams
    {
        [Tooltip("CameraStageのModel Libraryに登録したキー")]
        public string ModelKey;
        public ModelLayerRenderMode RenderMode;
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public PaletteColorParameter Color = new();
        public PaletteColorParameter WireColor = new();
        public FloatParameter AnimationSpeed = new(1f);
        public bool PlayAnimation = true;

        [NonSerialized] public Func<IReadOnlyList<string>> GetAvailableModelKeys;
    }
}
