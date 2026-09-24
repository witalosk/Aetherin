using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Aetherin
{
    public sealed class CameraStageHistoryRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader _shader;

        private Material _material;
        private HistoryPass _historyPass;
        private CopyPass _copyPass;

        public override void Create()
        {
            CoreUtils.Destroy(_material);
            _material = _shader != null ? CoreUtils.CreateEngineMaterial(_shader) : null;
            _historyPass = new HistoryPass(_material);
            _copyPass = new CopyPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null || !IsHistoryCamera(renderingData.cameraData.camera)) return;
            renderer.EnqueuePass(_historyPass);
            renderer.EnqueuePass(_copyPass);
        }

        private static bool IsHistoryCamera(Camera camera)
        {
            if (camera == null || camera.cameraType != CameraType.Game) return false;
            CameraStage stage = camera.GetComponentInParent<CameraStage>();
            return stage != null && stage.BackgroundMode == CameraStageBackgroundMode.DontClear &&
                   stage.HistoryHandle != null && camera.targetTexture == stage.OutputTexture;
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _historyPass = null;
            _copyPass = null;
        }

        private sealed class HistoryPass : ScriptableRenderPass
        {
            private static readonly int HistoryTexId = Shader.PropertyToID("_CameraStageHistoryTex");

            private readonly Material _material;

            private sealed class PassData
            {
                public TextureHandle History;
                public Material Material;
            }

            public HistoryPass(Material material)
            {
                _material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (!IsHistoryCamera(cameraData.camera)) return;

                CameraStage stage = cameraData.camera.GetComponentInParent<CameraStage>();
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                TextureHandle history = renderGraph.ImportTexture(stage.HistoryHandle);
                TextureHandle depth = resources.activeDepthTexture;
                if (!history.IsValid() || !resources.activeColorTexture.IsValid() ||
                    !depth.IsValid()) return;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                           "CameraStage DontClear History", out PassData passData))
                {
                    passData.History = history;
                    passData.Material = _material;
                    builder.UseTexture(history, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalTexture(HistoryTexId, data.History);
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 0,
                            MeshTopology.Triangles, 3);
                    });
                }
            }
        }

        private sealed class CopyPass : ScriptableRenderPass
        {
            private sealed class PassData
            {
                public TextureHandle Source;
            }

            public CopyPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (!IsHistoryCamera(cameraData.camera)) return;

                CameraStage stage = cameraData.camera.GetComponentInParent<CameraStage>();
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                TextureHandle source = resources.activeColorTexture;
                TextureHandle history = renderGraph.ImportTexture(stage.HistoryHandle);
                if (!source.IsValid() || !history.IsValid()) return;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                           "CameraStage Store DontClear History", out PassData passData))
                {
                    passData.Source = source;
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(history, 0, AccessFlags.Write);
                    // The result is read next frame, outside this frame's render graph.
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, data.Source, new Vector4(1f, 1f, 0f, 0f), 0f, false));
                }
            }
        }
    }
}
