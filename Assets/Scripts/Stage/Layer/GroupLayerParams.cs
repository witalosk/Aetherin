using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public sealed class GroupLayerParams : StageLayerParams
    {
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
    }
}
