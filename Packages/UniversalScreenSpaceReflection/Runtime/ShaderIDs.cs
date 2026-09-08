using UnityEngine;

namespace UniversalScreenSpaceReflection
{
    internal static class ShaderIDs
    {
        public static readonly int _DepthPyramidMipLevelOffsets = Shader.PropertyToID("_DepthPyramidMipLevelOffsets");
        public static readonly int _SrcOffsetAndLimit = Shader.PropertyToID("_SrcOffsetAndLimit");
        public static readonly int _DstOffset = Shader.PropertyToID("_DstOffset");
        public static readonly int _DepthMipChain = Shader.PropertyToID("_DepthMipChain");

        public static readonly int _ShaderVariablesScreenSpaceReflection = Shader.PropertyToID("_ShaderVariablesScreenSpaceReflection");
        public static readonly int _DepthPyramidTexture = Shader.PropertyToID("_DepthPyramidTexture");
        public static readonly int _ColorPyramidTexture = Shader.PropertyToID("_ColorPyramidTexture");
        public static readonly int _SsrHitPointTexture = Shader.PropertyToID("_SsrHitPointTexture");
        public static readonly int _SSRAccumTexture = Shader.PropertyToID("_SSRAccumTexture");

        public static readonly int _CameraColorTexture = Shader.PropertyToID("_CameraColorTexture");
        public static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        public static readonly int _CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");
        public static readonly int _CopiedColorPyramidTexture = Shader.PropertyToID("_CopiedColorPyramidTexture");
        public static readonly int _ColorPyramidSource = Shader.PropertyToID("_ColorPyramidSource");
        public static readonly int _ColorPyramidDestination = Shader.PropertyToID("_ColorPyramidDestination");
        public static readonly int _ColorPyramidSourceSize = Shader.PropertyToID("_ColorPyramidSourceSize");
    }
}
