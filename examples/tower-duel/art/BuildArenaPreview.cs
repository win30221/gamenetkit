using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Isolated art validation project; no existing game scene is opened or modified.
public static class BuildArenaPreview
{
    public static void Run()
    {
        const string root = "Assets/TwinbridgeArena";
        var output = Directory.GetParent(Application.dataPath).Parent.FullName;
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureUrp(root);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(root + "/TwinbridgeArena.fbx");
        if (model == null) throw new Exception("Arena FBX import failed");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var arena = new GameObject("TwinbridgeArena");
        var art = (GameObject)PrefabUtility.InstantiatePrefab(model, arena.transform);
        art.name = "Art";
        // FBX handedness/axis conversion reverses the imported ground layout.
        // Turn the art so its original ground X/Y matches Unity X/Z in layout.json.
        art.transform.localRotation = Quaternion.Euler(0,180,0);
        Directory.CreateDirectory(root + "/Materials");
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/hexagons_medieval.png");
        if (atlas == null) throw new Exception("Missing KayKit atlas");
        var materials = new System.Collections.Generic.Dictionary<string, Material>();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new Exception("URP Lit shader not found");
        long triangles=0; int renderers=0;
        foreach (var filter in arena.GetComponentsInChildren<MeshFilter>())
        {
            if (!filter.sharedMesh) throw new Exception("Missing mesh: " + filter.name);
            triangles += filter.sharedMesh.triangles.Length / 3;
        }
        foreach (var renderer in arena.GetComponentsInChildren<MeshRenderer>())
        {
            renderers++;
            var slots = renderer.sharedMaterials;
            for (int i=0; i<slots.Length; i++)
            {
                var source=slots[i];
                if (source == null) throw new Exception("Missing material: "+renderer.name);
                var name=source.name;
                if (!materials.TryGetValue(name,out var material))
                {
                    var path=root+"/Materials/"+name+".mat";
                    material=AssetDatabase.LoadAssetAtPath<Material>(path);
                    // Preserve source color/texture before replacing the shader. All
                    // inputs come from the original FBX, so rebuilding is repeatable.
                    var baseColor=source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.color : Color.white;
                    var mapName=source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                    var baseMap=source.HasProperty(mapName) ? source.GetTexture(mapName) : null;
                    var textureScale=source.HasProperty(mapName) ? source.GetTextureScale(mapName) : Vector2.one;
                    var textureOffset=source.HasProperty(mapName) ? source.GetTextureOffset(mapName) : Vector2.zero;
                    if (!material) {material=new Material(shader) {name=name}; AssetDatabase.CreateAsset(material,path);}
                    material.shader=shader;
                    material.SetColor("_BaseColor",baseColor);
                    material.SetTexture("_BaseMap",baseMap);
                    material.SetTextureScale("_BaseMap",textureScale);
                    material.SetTextureOffset("_BaseMap",textureOffset);
                    material.SetFloat("_Smoothness",0.08f);
                    material.SetFloat("_Metallic",0);
                    if (name.Contains("KayKit")) {material.SetColor("_BaseColor",Color.white); material.SetTexture("_BaseMap",atlas);}
                    EditorUtility.SetDirty(material);
                    materials.Add(name,material);
                }
                slots[i]=material;
            }
            renderer.sharedMaterials=slots;
        }
        if (triangles > 100000 || triangles < 20000) throw new Exception("Unexpected triangle count: "+triangles);
        var transforms = art.GetComponentsInChildren<Transform>();
        CheckPosition(transforms,"SM_blue_MainCastle",0,-11);
        CheckPosition(transforms,"SM_red_MainCastle",0,11);
        foreach (int x in new[]{-5,5})
        {
            CheckPosition(transforms,"SM_blue_Tower_"+x,x,-7.7f);
            CheckPosition(transforms,"SM_red_Tower_"+x,x,7.7f);
        }
        // Collisions are independent simple proxies. River remains non-walkable.
        var collision=new GameObject("Collision"); collision.transform.SetParent(arena.transform);
        foreach (int side in new[]{-1,1}) AddBox(collision.transform,"Bank",new Vector3(0,-.25f,side*8.25f),new Vector3(20,.5f,13.5f));
        foreach (int lane in new[]{-5,5}) AddBox(collision.transform,"Bridge",new Vector3(lane,.04f,0),new Vector3(2.5f,.08f,3.3f));
        var sun=new GameObject("Sun").AddComponent<Light>(); sun.type=LightType.Directional; sun.intensity=1.1f; sun.transform.rotation=Quaternion.Euler(50,-35,0); sun.shadows=LightShadows.Soft; sun.shadowStrength=.7f;
        sun.lightmapBakeType=LightmapBakeType.Realtime;
        sun.gameObject.AddComponent<UniversalAdditionalLightData>();
        RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.38f,.42f,.49f);
        var camera=new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera";
        camera.transform.position=new Vector3(0,38,-26); camera.transform.LookAt(Vector3.zero);
        camera.orthographic=true; camera.orthographicSize=18.5f; camera.nearClipPlane=.1f; camera.farClipPlane=150;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.1f,.16f,.22f);
        var cameraData=camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderPostProcessing=false; cameraData.SetRenderer(0);
        // This prototype uses realtime lighting, with no source bake or PPv2.
        const string lightingPath = "Assets/TwinbridgeArena/Rendering/TwinbridgeLighting.lighting";
        var lighting=AssetDatabase.LoadAssetAtPath<LightingSettings>(lightingPath);
        if (!lighting) {lighting=new LightingSettings(); AssetDatabase.CreateAsset(lighting,lightingPath);}
        lighting.bakedGI=false; lighting.realtimeGI=false;
        Lightmapping.lightingSettings=lighting;
        Lightmapping.Clear(); Lightmapping.ClearLightingDataAsset();
        EditorUtility.SetDirty(lighting);
        PrefabUtility.SaveAsPrefabAsset(arena,root+"/TwinbridgeArena.prefab");
        EditorSceneManager.SaveScene(scene,root+"/TwinbridgeArena.unity");
        AssetDatabase.SaveAssets();
        // Reload the saved scene before validation and capture.
        EditorSceneManager.OpenScene(root+"/TwinbridgeArena.unity",OpenSceneMode.Single);
        ValidateUrp(root);
        camera=Camera.main;
        var previousAsyncCompilation=ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation=false;
        var rt=new RenderTexture(1000,1200,24) {antiAliasing=4};
        // Prime the newly-created material/shader state before reading the frame.
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=rt});
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=rt});
        RenderTexture.active=rt; var image=new Texture2D(1000,1200,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1000,1200),0,0); image.Apply();
        File.WriteAllBytes(Path.Combine(output,"unity-preview.png"),image.EncodeToPNG());
        File.WriteAllBytes(Path.Combine(output,"unity-urp-preview.png"),image.EncodeToPNG());
        camera.targetTexture=null; RenderTexture.active=null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image);
        ShaderUtil.allowAsyncCompilation=previousAsyncCompilation;
        AssetDatabase.ExportPackage(root,Path.Combine(output,"TwinbridgeArena.unitypackage"),ExportPackageOptions.Recurse);
        File.WriteAllText(Path.Combine(output,"unity-validation.txt"),$"Unity {Application.unityVersion}\nFBX import: PASS\nTriangles: {triangles}\nRenderers: {renderers}\nMaterials: {materials.Count}\nAtlas: {atlas.width}x{atlas.height}\nCastle and tower world coordinates match layout.json: PASS\nPipeline: URP 17.6.0 (Lit shader)\nGraphics and all {QualitySettings.names.Length} Quality levels: PASS\nSaved renderer, materials, atlas and scene lighting: PASS\nLighting: realtime; no source bake, probes or PPv2 to migrate\nPrefab, scene, URP render and unitypackage created.\nNo gameplay or device performance validation.\n");
        Debug.Log("ARENA_UNITY_PASS " + triangles);
    }
    private static void ConfigureUrp(string root)
    {
        Directory.CreateDirectory(root+"/Rendering"); AssetDatabase.Refresh();
        var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(root+"/Rendering/TwinbridgeRenderer.asset");
        if (!renderer)
        {
            renderer=ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer,root+"/Rendering/TwinbridgeRenderer.asset");
        }
        var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(root+"/Rendering/TwinbridgeURP.asset");
        if (!pipeline)
        {
            pipeline=UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline,root+"/Rendering/TwinbridgeURP.asset");
        }
        pipeline.msaaSampleCount=4; pipeline.supportsHDR=false; pipeline.renderScale=1;
        pipeline.mainLightShadowmapResolution=2048;
        pipeline.shadowDistance=70; pipeline.shadowCascadeCount=2;
        pipeline.shadowDepthBias=.7f; pipeline.shadowNormalBias=.3f;
        // internal-api-ok: supportsSoftShadows has no public setter in URP 17.6.
        // Use the serialized Inspector field, guarding the version-specific name.
        var serializedPipeline=new SerializedObject(pipeline);
        var softShadows=serializedPipeline.FindProperty("m_SoftShadowsSupported");
        if (softShadows==null) throw new Exception("URP soft-shadow field is unavailable");
        softShadows.boolValue=true; serializedPipeline.ApplyModifiedProperties();
        if (!pipeline.supportsSoftShadows) throw new Exception("Soft shadows were not saved");
        GraphicsSettings.defaultRenderPipeline=pipeline;
        int previous=QualitySettings.GetQualityLevel();
        for (int i=0;i<QualitySettings.names.Length;i++) {QualitySettings.SetQualityLevel(i,false); QualitySettings.renderPipeline=pipeline;}
        QualitySettings.SetQualityLevel(previous,false);
        EditorUtility.SetDirty(renderer); EditorUtility.SetDirty(pipeline); AssetDatabase.SaveAssets();
    }
    private static void ValidateUrp(string root)
    {
        var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(root+"/Rendering/TwinbridgeURP.asset");
        var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(root+"/Rendering/TwinbridgeRenderer.asset");
        if (GraphicsSettings.defaultRenderPipeline!=pipeline || pipeline.rendererDataList.Length!=1 || pipeline.rendererDataList[0]!=renderer)
            throw new Exception("Saved URP asset/renderer mismatch");
        for (int i=0;i<QualitySettings.names.Length;i++)
            if (QualitySettings.GetRenderPipelineAssetAt(i)!=pipeline) throw new Exception("Quality level is missing URP: "+i);
        foreach (var guid in AssetDatabase.FindAssets("t:Material",new[]{root+"/Materials"}))
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (!material.shader || material.shader.name!="Universal Render Pipeline/Lit" || !material.shader.isSupported)
                throw new Exception("Invalid URP material: "+material.name);
            if (material.name.Contains("KayKit") && material.GetTexture("_BaseMap")==null) throw new Exception("Atlas lost during conversion");
        }
        foreach (var rendererInScene in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            foreach (var material in rendererInScene.sharedMaterials)
                if (!material || material.shader.name!="Universal Render Pipeline/Lit") throw new Exception("Scene still references a Built-in material");
        if (!Lightmapping.lightingSettings || Lightmapping.lightingSettings.bakedGI || Lightmapping.lightingSettings.realtimeGI || LightmapSettings.lightmaps.Length!=0)
            throw new Exception("Saved realtime lighting settings mismatch");
        if (!Camera.main.GetComponent<UniversalAdditionalCameraData>()) throw new Exception("URP camera data missing");
    }
    private static void CheckPosition(Transform[] transforms,string name,float x,float z)
    {
        var target=transforms.First(t=>t.name==name);
        if (Vector2.Distance(new Vector2(target.position.x,target.position.z),new Vector2(x,z))>.01f)
            throw new Exception($"Unexpected position for {name}: {target.position}");
    }
    private static void AddBox(Transform parent,string name,Vector3 position,Vector3 size)
    {
        var go=new GameObject(name); go.transform.SetParent(parent,false); go.transform.localPosition=position;
        go.AddComponent<BoxCollider>().size=size;
    }
}
