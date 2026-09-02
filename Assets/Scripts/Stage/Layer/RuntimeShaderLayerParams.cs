using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public sealed class RuntimeShaderLayerParams : StageLayerParams
    {
        [NonSerialized] public Shader Shader;
        public string ShaderName = "Aetherin/Runtime Shader Layer Example";

        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public Vector2Parameter Size = new(new Vector2(2f, 2f));

        public FloatParameter UserFloat0 = new(1f);
        public FloatParameter UserFloat1 = new(0f);
        public FloatParameter UserFloat2 = new(0f);
        public FloatParameter UserFloat3 = new(0f);
        public Vector3Parameter UserVector0 = new();
        public Vector3Parameter UserVector1 = new();
        public Vector3Parameter UserVector2 = new();
        public Vector3Parameter UserVector3 = new();

        public void EnsureInitialized()
        {
            Opacity ??= new FloatParameter(1f);
            ShaderName ??= string.Empty;
            Position ??= new Vector3Parameter();
            Rotation ??= new Vector3Parameter();
            Scale ??= new Vector3Parameter(Vector3.one);
            Anchor ??= new Vector3Parameter();
            Size ??= new Vector2Parameter(new Vector2(2f, 2f));
            UserFloat0 ??= new FloatParameter(1f);
            UserFloat1 ??= new FloatParameter();
            UserFloat2 ??= new FloatParameter();
            UserFloat3 ??= new FloatParameter();
            UserVector0 ??= new Vector3Parameter();
            UserVector1 ??= new Vector3Parameter();
            UserVector2 ??= new Vector3Parameter();
            UserVector3 ??= new Vector3Parameter();
        }
    }
}
