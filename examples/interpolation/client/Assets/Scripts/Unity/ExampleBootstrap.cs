using UnityEngine;

namespace Realtime.Examples.InterpolationLesson
{
    public static class ExampleBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartWorld()
        {
            if (Object.FindAnyObjectByType<ExampleClient>() != null) return;
            var root = new GameObject("InterpolationDemo");
            Object.DontDestroyOnLoad(root);
            var cameraObject = new GameObject("World Camera", typeof(Camera));
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.position = new Vector3(0, 19, -14);
            cameraObject.transform.LookAt(Vector3.zero);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.08f, 0.13f);
            camera.orthographic = true; camera.orthographicSize = 11.5f;
            camera.rect = new Rect(.4f, 0, .6f, 1);
            cameraObject.tag = "MainCamera";
            var lightObject = new GameObject("Sun", typeof(Light));
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);
            var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.6f);
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "World floor"; plane.transform.SetParent(root.transform, false);
            plane.transform.localScale = new Vector3(1.8f, 1, 1.8f);
            var floorMaterial = WorldPresentation.MakeMaterial(new Color(0.22f, 0.26f, 0.32f));
            plane.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            root.AddComponent<ExampleClient>();
        }
    }
}
