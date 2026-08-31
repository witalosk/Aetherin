using System;
using UnityEngine;

namespace Aetherin
{
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

        [Tooltip("CameraStage 内での描画順。大きい値ほど手前に描画されます")]
        public int Order;
    }

    /// <summary>
    /// CameraStage のカメラへ直接描画するレイヤーの基底クラス。
    /// 非表示でも Update を止めず、アニメーションの時間を進められるよう Renderer だけを無効化する。
    /// </summary>
    public abstract class StageLayer : MonoBehaviour, IStageLayer, ISaveTarget
    {
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
        protected abstract Renderer LayerRenderer { get; }

        protected virtual void LateUpdate()
        {
            ApplyLayerState();
        }

        protected virtual void OnValidate()
        {
            ApplyLayerState();
        }

        protected void ApplyLayerState()
        {
            var renderer = LayerRenderer;
            var layerParams = LayerParams;
            if (renderer == null || layerParams == null) return;

            renderer.forceRenderingOff = !layerParams.Visible;
            renderer.sortingOrder = layerParams.Order;
        }
    }
}
