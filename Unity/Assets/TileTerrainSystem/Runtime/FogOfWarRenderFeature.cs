using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace MooLucio.TileTerrain
{
    /// <summary>
    /// URP 17 / Unity 6 RenderGraph-based fog of war full-screen pass.
    /// Samples the per-cell mask from <see cref="FogOfWarManager"/> and blends
    /// fog over the scene color using scene depth to reconstruct world position.
    /// </summary>
    public class FogOfWarRenderFeature : ScriptableRendererFeature
    {
        [Tooltip("Material using the TileTerrain/FogOfWar shader.")]
        [FormerlySerializedAs("fogMaterial")]
        public Material FogMaterial;

        [Tooltip("When to inject the pass in URP's frame graph.")]
        [FormerlySerializedAs("injectionPoint")]
        public RenderPassEvent InjectionPoint = RenderPassEvent.AfterRenderingTransparents;

        private FogOfWarPass _pass;

        public override void Create()
        {
            _pass = new FogOfWarPass
            {
                renderPassEvent = InjectionPoint
            };
            // We need the camera depth texture for the shader to reconstruct world positions.
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            // We render to an intermediate texture, so URP must allocate the camera color as an RT.
            _pass.requiresIntermediateTexture = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (FogMaterial == null) return;
            _pass.renderPassEvent = InjectionPoint;
            _pass.Setup(FogMaterial);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass = null;
        }

        // ── Pass ───────────────────────────────────────────────────────────────
        private class FogOfWarPass : ScriptableRenderPass
        {
            private static readonly int MaskTexId       = Shader.PropertyToID("_MaskTex");
            private static readonly int FogColorId      = Shader.PropertyToID("_FogColor");
            private static readonly int ExploredColorId = Shader.PropertyToID("_ExploredColor");
            private static readonly int OutsideGridId   = Shader.PropertyToID("_OutsideGridFog");
            private static readonly int GridOffsetId    = Shader.PropertyToID("_GridOffset");
            private static readonly int GridWorldSizeId = Shader.PropertyToID("_GridWorldSize");
            private static readonly int FogBlurId       = Shader.PropertyToID("_FogBlur");

            private Material _material;

            public void Setup(Material m) => _material = m;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var mgr = FogOfWarManager.Instance;
                if (_material == null || mgr == null) return;
                if (mgr.GridData == null || mgr.MaskTexture == null) return;

                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData   = frameData.Get<UniversalCameraData>();
                var src          = resourceData.activeColorTexture;
                if (!src.IsValid()) return;

                // Allocate an intermediate color texture matching the camera target.
                var desc = cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples     = 1;
                var tmp = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, desc, "_FogOfWarTmp", clear: false);

                // Push per-frame material props.
                var grid = mgr.GridData;
                _material.SetTexture(MaskTexId,       mgr.MaskTexture);
                _material.SetColor  (FogColorId,      mgr.FogColor);
                _material.SetColor  (ExploredColorId, mgr.ExploredColor);
                _material.SetFloat  (OutsideGridId,   mgr.OutsideGridFog);
                _material.SetFloat  (FogBlurId,       mgr.MaskBlur);
                _material.SetVector (GridOffsetId,    new Vector4(-grid.Width * 0.5f, -grid.Height * 0.5f, 0f, 0f));
                _material.SetVector (GridWorldSizeId, new Vector4(grid.Width, grid.Height, 0f, 0f));

                // 1) src -> tmp with fog material (samples _BlitTexture which is bound to src by Blitter).
                var blitPara = new RenderGraphUtils.BlitMaterialParameters(
                    src, tmp, _material, shaderPass: 0);
                renderGraph.AddBlitPass(blitPara, "FogOfWar Apply");

                // 2) tmp -> src copy back. AddBlitPass (no material) routes through Blitter and
                //    handles MSAA mismatches via resolve-on-read / broadcast-on-write.
                renderGraph.AddBlitPass(tmp, src, Vector2.one, Vector2.zero, passName: "FogOfWar CopyBack");
            }
        }
    }
}
