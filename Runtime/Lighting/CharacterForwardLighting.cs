using System.Collections.Generic;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Character forward lighting.
    /// </summary>
    internal class CharacterForwardLighting : ScriptableRenderPass
    {
        // Public Variables

        // Private Variables
        private FilteringSettings m_FilteringSettings;
        private RenderStateBlock m_RenderStateBlock;
        private List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>
        {
            new ShaderTagId("CharacterForward"),
            new ShaderTagId("CharacterOutline")
        };
        // Constants

        // Statics

        public CharacterForwardLighting(RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference)
        {
            base.profilingSampler = new ProfilingSampler(nameof(CharacterForwardLighting));
            base.renderPassEvent = evt;

            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);

            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            m_RenderStateBlock.stencilState = DeferredLights.OverwriteStencil(stencilState, (int)StencilUsage.MaterialMask);
            m_RenderStateBlock.stencilReference = stencilReference | (int)StencilUsage.MaterialUnlit;
            m_RenderStateBlock.mask = RenderStateMask.Stencil;

        }

        internal class PassData
        {
            internal TextureHandle albedoHdl;
            internal TextureHandle depthHdl;

            // Sky Ambient
            internal BufferHandle ambientProbe;
            // Sky Reflect
            internal TextureHandle reflectProbe;

            internal UniversalCameraData cameraData;

            internal uint batchLayerMask;
            internal RendererListHandle rendererList;
        }
        internal void InitRendererLists(
            RenderGraph renderGraph,
            UniversalRenderingData renderingData,
            UniversalCameraData cameraData,
            UniversalLightData lightData,
            ref PassData passData)
        {
            ref Camera camera = ref cameraData.camera;
            var sortFlags = cameraData.defaultOpaqueSortFlags;
            if (cameraData.renderer.useDepthPriming && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth))
                sortFlags = SortingCriteria.SortingLayer | SortingCriteria.RenderQueue | SortingCriteria.OptimizeStateChanges;

            var filterSettings = m_FilteringSettings;
            filterSettings.batchLayerMask = passData.batchLayerMask;
            filterSettings.layerMask = -1;
#if UNITY_EDITOR
            // When rendering the preview camera, we want the layer mask to be forced to Everything
            if (cameraData.isPreviewCamera)
            {
                filterSettings.layerMask = -1;
            }
#endif
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList, renderingData, cameraData, lightData, sortFlags);
            if (cameraData.renderer.useDepthPriming && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth))
            {
                m_RenderStateBlock.depthState = new DepthState(false, CompareFunction.Equal);
                m_RenderStateBlock.mask |= RenderStateMask.Depth;
            }
            else if (m_RenderStateBlock.depthState.compareFunction == CompareFunction.Equal)
            {
                m_RenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                m_RenderStateBlock.mask |= RenderStateMask.Depth;
            }

            RenderingUtils.CreateRendererListWithRenderStateBlock(renderGraph, ref renderingData.cullResults, drawSettings, filterSettings, m_RenderStateBlock, ref passData.rendererList);
        }


        static void ExecutePass(RasterCommandBuffer cmd, PassData data, bool yFlip)
        {
            // scaleBias.x = flipSign
            // scaleBias.y = scale
            // scaleBias.z = bias
            // scaleBias.w = unused
            float flipSign = yFlip ? -1.0f : 1.0f;
            Vector4 scaleBias = (flipSign < 0.0f)
                ? new Vector4(flipSign, 1.0f, -1.0f, 1.0f)
                : new Vector4(flipSign, 0.0f, 1.0f, 1.0f);
            cmd.SetGlobalVector(ShaderPropertyId.scaleBiasRt, scaleBias);
            cmd.SetGlobalFloat(ShaderPropertyId.alphaToMaskAvailable, 0);

            cmd.SetGlobalBuffer("_AmbientProbeData", data.ambientProbe);
            cmd.SetGlobalTexture("_SkyTexture", data.reflectProbe);


            cmd.DrawRendererList(data.rendererList);
        }

        internal void Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle colorTarget, TextureHandle depthTarget, uint batchLayerMask = uint.MaxValue)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Character Forward Lighting", out var passData, base.profilingSampler))
            {
                // Access resources
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                passData.cameraData = cameraData;
                passData.batchLayerMask = batchLayerMask;

                // Sky Environment
                passData.ambientProbe = resourceData.skyAmbientProbe;
                passData.reflectProbe = resourceData.skyReflectionProbe;

                InitRendererLists(renderGraph, renderingData, cameraData, lightData, ref passData);

                TextureHandle mainShadowsTexture = resourceData.mainShadowsTexture;
                TextureHandle additionalShadowsTexture = resourceData.additionalShadowsTexture;


                builder.UseBuffer(passData.ambientProbe);
                builder.UseTexture(passData.reflectProbe);

                if (colorTarget.IsValid())
                {
                    passData.albedoHdl = colorTarget;
                    builder.SetRenderAttachment(colorTarget, 0, AccessFlags.Write);
                }
                if (depthTarget.IsValid())
                {
                    passData.depthHdl = depthTarget;
                    builder.SetRenderAttachmentDepth(depthTarget, AccessFlags.Write);
                }
                if (mainShadowsTexture.IsValid())
                    builder.UseTexture(mainShadowsTexture, AccessFlags.Read);
                if (additionalShadowsTexture.IsValid())
                    builder.UseTexture(additionalShadowsTexture, AccessFlags.Read);

                builder.UseRendererList(passData.rendererList);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    bool yFlip = data.cameraData.IsRenderTargetProjectionMatrixFlipped(data.albedoHdl, data.depthHdl);

                    ExecutePass(context.cmd, data, yFlip);
                });
            }

        }


        public override void OnCameraCleanup(CommandBuffer cmd)
        {

        }
    }


}