using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    public enum PrefabMaterialModulationType
    {
        EmissionColorIntensity,
        Float,
    }

    /// <summary>
    /// ModelLayerで生成するPrefab内のRendererへ、既存Modulationの値を適用する。
    /// MaterialPropertyBlockを使うため、共有Material assetや別デッキの見た目は変更しない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrefabMaterialModulator : MonoBehaviour
    {
        [SerializeField] private Renderer _targetRenderer;
        [SerializeField] private bool _includeChildren;
        [Tooltip("-1なら対象Rendererの全Material slotへ適用します")]
        [SerializeField] private int _materialSlot = -1;
        [Tooltip("空なら全Material。文字列を登録すると名前にいずれかを含むMaterialだけへ適用します")]
        [SerializeField] private List<string> _materialNameContains = new();
        [SerializeField] private PrefabMaterialModulationType _type;
        [Tooltip("Emissionでは通常 _EmissionColor、Floatでは対象Shaderのfloat property名を指定します")]
        [SerializeField] private string _propertyName = "_EmissionColor";
        [Tooltip("Emission ColorをMaterialから取得します。OFFなら下のColorを使います")]
        [SerializeField] private bool _useMaterialColor = true;
        [ColorUsage(true, true)]
        [SerializeField] private Color _color = Color.white;
        [Tooltip("Materialへ渡す値。Beat/Kick/Snare/LFOなど既存Modulationを設定できます")]
        [SerializeField] private FloatParameter _intensity = new(1f);

        private Renderer[] _renderers = Array.Empty<Renderer>();
        private MaterialPropertyBlock _propertyBlock;
        private int _propertyId;

        public Renderer TargetRenderer
        {
            get => _targetRenderer;
            set { _targetRenderer = value; RefreshTargets(); }
        }

        public bool IncludeChildren
        {
            get => _includeChildren;
            set { _includeChildren = value; RefreshTargets(); }
        }

        public int MaterialSlot
        {
            get => _materialSlot;
            set => _materialSlot = value;
        }

        public List<string> MaterialNameContains => _materialNameContains ??= new List<string>();

        public PrefabMaterialModulationType Type
        {
            get => _type;
            set => _type = value;
        }

        public string PropertyName
        {
            get => _propertyName;
            set { _propertyName = value; RefreshPropertyId(); }
        }

        public bool UseMaterialColor
        {
            get => _useMaterialColor;
            set => _useMaterialColor = value;
        }

        public Color Color
        {
            get => _color;
            set => _color = value;
        }

        public FloatParameter Intensity => _intensity ??= new FloatParameter(1f);

        internal void Initialize()
        {
            _intensity ??= new FloatParameter(1f);
            _propertyBlock ??= new MaterialPropertyBlock();
            RefreshPropertyId();
            RefreshTargets();
        }

        internal void Apply(in ModulationContext context)
        {
            if (!isActiveAndEnabled) return;
            if (_renderers == null || _renderers.Length == 0) Initialize();
            if (_propertyId == 0) return;

            float value = Mathf.Max(0f, Intensity.Evaluate(context));
            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null) continue;
                Material[] materials = renderer.sharedMaterials;
                if (_materialSlot >= 0)
                {
                    if (_materialSlot < materials.Length)
                        ApplyToSlot(renderer, materials[_materialSlot], _materialSlot, value);
                    continue;
                }

                for (int slot = 0; slot < materials.Length; slot++)
                    ApplyToSlot(renderer, materials[slot], slot, value);
            }
        }

        private void ApplyToSlot(Renderer renderer, Material material, int slot, float value)
        {
            if (material == null || !MatchesMaterial(material) || !material.HasProperty(_propertyId)) return;

            renderer.GetPropertyBlock(_propertyBlock, slot);
            if (_type == PrefabMaterialModulationType.EmissionColorIntensity)
            {
                Color baseColor = _useMaterialColor ? material.GetColor(_propertyId) : _color;
                Color output = baseColor * value;
                output.a = baseColor.a;
                _propertyBlock.SetColor(_propertyId, output);
            }
            else
            {
                _propertyBlock.SetFloat(_propertyId, value);
            }
            renderer.SetPropertyBlock(_propertyBlock, slot);
        }

        private bool MatchesMaterial(Material material)
        {
            if (_materialNameContains == null || _materialNameContains.Count == 0) return true;
            for (int i = 0; i < _materialNameContains.Count; i++)
            {
                string filter = _materialNameContains[i];
                if (!string.IsNullOrWhiteSpace(filter) &&
                    material.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private void Awake() => Initialize();

        private void OnValidate()
        {
            _materialSlot = Mathf.Max(-1, _materialSlot);
            Initialize();
        }

        private void RefreshTargets()
        {
            if (_includeChildren)
                _renderers = GetComponentsInChildren<Renderer>(true);
            else
            {
                Renderer renderer = _targetRenderer != null ? _targetRenderer : GetComponent<Renderer>();
                _renderers = renderer != null ? new[] { renderer } : Array.Empty<Renderer>();
            }
        }

        private void RefreshPropertyId()
        {
            _propertyId = string.IsNullOrWhiteSpace(_propertyName)
                ? 0
                : Shader.PropertyToID(_propertyName);
        }
    }
}
