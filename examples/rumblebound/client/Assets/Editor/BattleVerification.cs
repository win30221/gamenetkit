using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Rumblebound.Gameplay;
using Rumblebound.Example.Battle;
using Rumblebound.Example.Battle.Unity;

namespace Rumblebound.Example.Editor
{
    public static class BattleVerification
    {
        public static void Run()
        {
            string map=File.ReadAllText("../game/shared/maps/arena-01.json"),hash;
            using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(map))).Replace("-","").ToLowerInvariant();
            var content=new GameContent(new LabConfig{rules=JsonUtility.FromJson<Rules>(File.ReadAllText("../game/shared/rules.json")),mapJSON=map,mapHash=hash},new UnityGameJson());
            new BattleSettings().Validate();
            foreach(string id in new[]{"Knight","Ranger","Mage"}){
                var actor=Resources.Load<GameObject>("KayKit/"+id);var animator=actor?.GetComponentInChildren<Animator>(true);
                if(actor==null||animator==null||animator.runtimeAnimatorController==null)throw new Exception("Missing baked hero/animation: "+id);
            }
            foreach(string asset in new[]{"G0Floor","KayKit/Arrow","VFX/WallHit","VFX/PlayerHit","VFX/PointAura","Mage/FireballDrop"})if(Resources.Load<GameObject>(asset)==null)throw new Exception("Missing visual asset: "+asset);
            if(Resources.Load<Font>("UI/RumbleboundUI")==null||AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Battle.unity")==null)throw new Exception("Battle font or saved scene missing");
            if(Resources.Load<UnityEngine.UIElements.PanelSettings>("UI/GamePanelSettings")==null||Resources.Load<UnityEngine.UIElements.ThemeStyleSheet>("UI/GameTheme")==null)throw new Exception("Original UI Toolkit panel/theme missing");
            foreach(string texture in new[]{"button-primary","button-secondary","button-danger","icon-ready","icon-star"})if(Resources.Load<Texture2D>("UI/Textures/"+texture)==null)throw new Exception("Original UI texture missing: "+texture);
            var themeDependencies=AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(Resources.Load<UnityEngine.UIElements.ThemeStyleSheet>("UI/GameTheme")));
            foreach(string name in new[]{"button-primary","button-secondary","button-danger","icon-ready","icon-star"})if(Array.IndexOf(themeDependencies,AssetDatabase.GetAssetPath(Resources.Load<Texture2D>("UI/Textures/"+name)))<0)throw new Exception("UI stylesheet does not resolve original texture: "+name);
            if(Array.IndexOf(themeDependencies,AssetDatabase.GetAssetPath(Resources.Load<Font>("UI/RumbleboundUI")))<0)throw new Exception("UI stylesheet does not resolve original font");
            FontEngine.InitializeFontEngine();
            if(FontEngine.LoadFontFace(Resources.Load<Font>("UI/RumbleboundUI"),24)!=FontEngineError.Success)throw new Exception("Cannot load battle UI font");
            foreach(string path in Directory.GetFiles("Assets/Scripts","*.cs",SearchOption.AllDirectories))foreach(char character in File.ReadAllText(path))
                if((character>='\u3000'&&character<='\u9fff'||character=='\u2713')&&(!FontEngine.TryGetGlyphIndex(character,out uint glyph)||glyph==0))throw new Exception("Battle UI font missing U+"+((int)character).ToString("X4")+"; regenerate samples/rumblebound/scripts/Prepare-UIFont.py");
            if(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline==null)throw new Exception("Shared URP pipeline missing");
            Debug.Log("RF_BATTLE_ASSETS PASS original game rules/map, JSON parser, hero animations, shared art/font, scene and pipeline");
        }
    }
}
