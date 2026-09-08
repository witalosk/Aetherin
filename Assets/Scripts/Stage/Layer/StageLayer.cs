using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aetherin
{
    public enum LayerBlendMode
    {
        Opaque,
        Transparent,
        Additive,
    }

    public interface IStageLayer
    {
        bool Visible { get; set; }
        int Order { get; set; }
        Renderer Renderer { get; }
    }

    [Serializable]
    public class StageLayerParams : IParams
    {
        public bool Visible = true;

        public FloatParameter Opacity = new(1f);
        public LayerBlendMode BlendMode = LayerBlendMode.Opaque;

        [Tooltip("CameraStage 内での描画順。大きい値ほど手前に描画されます")]
        public int Order;
    }

    public static class LayerMaterialUtility
    {
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        public static void ApplyBlendMode(Material material, LayerBlendMode mode)
        {
            if (material == null) return;

            BlendMode srcBlend;
            BlendMode dstBlend;
            bool zWrite;
            bool opaque = mode == LayerBlendMode.Opaque;

            switch (mode)
            {
                case LayerBlendMode.Opaque:
                    srcBlend = BlendMode.One;
                    dstBlend = BlendMode.Zero;
                    zWrite = true;
                    break;
                case LayerBlendMode.Additive:
                    srcBlend = BlendMode.SrcAlpha;
                    dstBlend = BlendMode.One;
                    zWrite = false;
                    break;
                default:
                    srcBlend = BlendMode.SrcAlpha;
                    dstBlend = BlendMode.OneMinusSrcAlpha;
                    zWrite = false;
                    break;
            }

            material.SetFloat(SrcBlendId, (float)srcBlend);
            material.SetFloat(DstBlendId, (float)dstBlend);
            material.SetFloat(ZWriteId, zWrite ? 1f : 0f);
            material.renderQueue = opaque ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", opaque ? "Opaque" : "Transparent");
        }
    }

    /// <summary>
    /// CameraStage のカメラへ直接描画するレイヤーの基底クラス。
    /// 非表示でも Update を止めず、アニメーションの時間を進められるよう Renderer だけを無効化する。
    /// </summary>
    public abstract class StageLayer : MonoBehaviour, IStageLayer, ISaveTarget
    {
        private bool _elapsedTimeActive;
        private double _elapsedTimeStart;
        public abstract IParams Params { get; }
        public bool Visible
        {
            get => LayerParams.Visible;
            set
            {
                LayerParams.Visible = value;
                ApplyLayerState();
            }
        }

        public int Order
        {
            get => LayerParams.Order;
            set
            {
                LayerParams.Order = value;
                ApplyLayerState();
            }
        }

        public Renderer Renderer => LayerRenderer;

        protected abstract StageLayerParams LayerParams { get; }
        /// <summary>通常Rendererを持たないGPU/VFXレイヤーではnullでよい。</summary>
        protected virtual Renderer LayerRenderer => null;

        protected virtual void LateUpdate()
        {
            ApplyLayerState();
        }

        protected virtual void OnValidate()
        {
            ApplyLayerState();
        }

        protected virtual void ApplyLayerState()
        {
            var renderer = LayerRenderer;
            var layerParams = LayerParams;
            if (layerParams == null) return;

            bool effectiveVisible = layerParams.Visible;
            Transform ancestor = transform.parent;
            while (effectiveVisible && ancestor != null)
            {
                var group = ancestor.GetComponent<GroupLayer>();
                if (group != null && !group.Visible) effectiveVisible = false;
                ancestor = ancestor.parent;
            }

            if (renderer != null)
            {
                renderer.forceRenderingOff = !effectiveVisible;
                renderer.sortingOrder = layerParams.Order;
            }

            ApplyCustomLayerState(effectiveVisible, layerParams.Order);
        }

        // GroupLayer uses this to propagate a visibility change immediately to
        // descendants, including renderer-less GPU/VFX layers.
        internal void RefreshLayerState() => ApplyLayerState();

        /// <summary>
        /// レイヤーが表示状態になった時点からの時間を持つContextを作る。
        /// 非表示中は停止し、再表示でElapsed Timeが0へ戻る。
        /// </summary>
        protected ModulationContext CreateModulationContext(
            double time, IAudioFeatureProvider audio, IBeatManager beat, bool allowMidi)
        {
            bool active = IsEffectivelyVisible();
            if (active && (!_elapsedTimeActive || time < _elapsedTimeStart)) _elapsedTimeStart = time;
            _elapsedTimeActive = active;
            return new ModulationContext(time, audio, beat, allowMidi,
                elapsedTime: active ? Math.Max(0d, time - _elapsedTimeStart) : 0d);
        }

        private bool IsEffectivelyVisible()
        {
            if (LayerParams == null || !LayerParams.Visible) return false;
            Transform ancestor = transform.parent;
            while (ancestor != null)
            {
                var group = ancestor.GetComponent<GroupLayer>();
                if (group != null && !group.Visible) return false;
                ancestor = ancestor.parent;
            }
            return true;
        }

        protected virtual void ApplyCustomLayerState(bool visible, int order) { }
    }
}
