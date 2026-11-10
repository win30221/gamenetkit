using Rumblebound.Gameplay;
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rumblebound.Presentation
{
    public static class URPCameras
    {
        public static void Configure(Camera camera, bool shadows)
        {
            var data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Base;
            data.SetRenderer(0);
            data.renderShadows = shadows;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            camera.allowHDR = false;
        }

        public static void Render(Camera camera, RenderTexture target)
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException("Active URP does not support native camera capture");
            RenderPipeline.SubmitRenderRequest(camera, request);
        }
    }
}
