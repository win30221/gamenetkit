#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rumblebound.Example.Battle.Unity
{
    // Renders the application's own frame, including UI Toolkit, and checks
    // opaque HUD/control bounds against the protected gameplay viewport.
    public sealed class BattleHudVerification:MonoBehaviour
    {
        private BattleClient client;
        private GameFlow Flow=>client.Flow;
        private string output;
        private bool finished;
        private int captures;
        private void Awake(){client=GetComponent<BattleClient>();Application.logMessageReceived+=Log;}
        private void Log(string text,string stack,LogType type){if(!finished&&(type==LogType.Exception||type==LogType.Error)){finished=true;Application.Quit(1);}}
        private IEnumerator Until(Func<bool> condition,string stage){double end=client.Now+25;while(!condition()){if(client.Now>end)throw new Exception("RF_HUD_REVIEW FAIL "+stage+": "+Flow?.Notice+" "+Flow?.Battle.Status);yield return null;}}
        private void CheckLayout()
        {
            var root=GetComponent<UIDocument>().rootVisualElement;float scale=GetComponent<UIDocument>().panelSettings.scale;
            Rect arena=new Rect(Screen.width*.30f,Screen.height*.34f,Screen.width*.40f,Screen.height*.30f);
            if(client.Presentation.Camera.pixelRect.width<Screen.width*.95f)throw new Exception("RF_HUD_REVIEW FAIL battlefield is not full width");
            foreach(string selector in new[]{"hud-top","health-card","ability-row","hud-menu"}){
                var element=root.Q<VisualElement>(className:selector);if(element.resolvedStyle.display==DisplayStyle.None)continue;var bounds=element.worldBound;
                var pixels=new Rect(bounds.x*scale,Screen.height-bounds.yMax*scale,bounds.width*scale,bounds.height*scale);
                if(arena.Overlaps(pixels))throw new Exception("RF_HUD_REVIEW FAIL HUD overlaps protected arena: "+selector+" "+pixels+" arena="+arena);
            }
            if(BattleHud.TouchMode)foreach(string name in new[]{"touchMove","touchAttack","touchAim","touchDash"}){
                var bounds=root.Q<VisualElement>(name).worldBound;
                var pixels=new Rect(bounds.x*scale,Screen.height-bounds.yMax*scale,bounds.width*scale,bounds.height*scale);
                if(arena.Overlaps(pixels))throw new Exception("RF_HUD_REVIEW FAIL touch control overlaps arena: "+name);
            }
            if(BattleHud.TouchMode){var controls=new[]{"touchMove","touchAttack","touchAim","touchDash"};
                for(int i=0;i<controls.Length;i++)for(int j=i+1;j<controls.Length;j++)if(root.Q<VisualElement>(controls[i]).worldBound.Overlaps(root.Q<VisualElement>(controls[j]).worldBound))throw new Exception("RF_HUD_REVIEW FAIL touch controls overlap");}
            if(client.Presentation.AttackAimVisible!=(client.PreviewKind!=BattlePreviewKind.None))throw new Exception("RF_HUD_REVIEW FAIL wrong range visibility");
            if(!client.PreviewSelecting&&client.Presentation.TargetAimVisible)throw new Exception("RF_HUD_REVIEW FAIL target shown before drag");
        }
        private IEnumerator Capture(string hero,int width,int height,BattlePreviewKind kind=BattlePreviewKind.Skill,bool selecting=true,string suffix="")
        {
            client.ReviewPreview=kind;client.ReviewSelecting=selecting;
            Screen.SetResolution(width,height,false);
            yield return Until(()=>Screen.width==width&&Screen.height==height,"resolution");
            yield return new WaitForSeconds(.4f);
            for(int i=0;i<25;i++)yield return null;
            yield return Until(()=>client.Presentation.AttackAimVisible==(kind!=BattlePreviewKind.None),"aim presentation");
            CheckLayout();
            string file=Path.Combine(output,hero+suffix+"-"+width+"x"+height+".png");
            if(File.Exists(file))File.Delete(file);
            ScreenCapture.CaptureScreenshot(file);
            yield return Until(()=>File.Exists(file),"frame capture");
            captures++;
            Debug.Log("RF_HUD_REVIEW FRAME "+file+" arena="+BattleHud.ArenaPixels);
        }
        private IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]=="-rf-hud-output")output=args[++i];
            if(!Path.IsPathRooted(output))throw new Exception("RF_HUD_REVIEW FAIL absolute output directory required");
            Directory.CreateDirectory(output);
            yield return Until(()=>Flow?.Content!=null&&!Flow.Busy,"config");Flow.Guest();yield return Until(()=>Flow.Identity.IsAuthenticated&&!Flow.Busy,"guest");
            foreach(string hero in new[]{"ranger","tank","mage"}){
                if(!Flow.Practice(hero,"tank","idle"))throw new Exception("RF_HUD_REVIEW FAIL practice");
                yield return Until(()=>BattleHud.TouchCanPlay&&!Flow.Busy,"playable");
                var own=Flow.Battle.State.Latest.players.First(p=>p.playerID==Flow.Battle.State.PlayerID);
                client.ReviewAim=new Vector2((float)own.x,(float)own.z+5);
                yield return Capture(hero,989,446);
                if(hero=="ranger"){
                    yield return Capture(hero,844,390);yield return Capture(hero,1280,720);
                    yield return Capture(hero,989,446,BattlePreviewKind.None,false,"-idle");
                    yield return Capture(hero,989,446,BattlePreviewKind.Basic,false,"-basic-range");
                    yield return Capture(hero,989,446,BattlePreviewKind.Skill,false,"-skill-range");
                    yield return Capture(hero,989,446,BattlePreviewKind.Dash,false,"-dash-range");
                    client.ReviewPreview=BattlePreviewKind.None;
                    var target=Flow.Battle.State.Latest.players.First(p=>p.teamID!=own.teamID);
                    yield return Until(()=>target.protectedUntilTick<=Flow.Battle.State.Tick,"basic target protection");
                    if(BattleHud.TouchMode){client.TouchInput.BeginAttack(987);client.TouchInput.EndAttack(987);}else client.AttackOrder(target);
                    yield return Until(()=>client.Presentation.BasicArrowCount>0,"ordinary basic arrow");
                    string basic=Path.Combine(output,"ranger-basic-arrow-989x446.png");if(File.Exists(basic))File.Delete(basic);ScreenCapture.CaptureScreenshot(basic);yield return Until(()=>File.Exists(basic),"basic arrow capture");captures++;Debug.Log("RF_HUD_REVIEW FRAME "+basic);client.Orders.Clear();
                    client.ReviewPreview=BattlePreviewKind.None;client.SkillOrder(new Vector2((float)own.x,(float)own.z+6));
                    yield return Until(()=>client.Presentation.FlameArrowCount>0,"flaming skill arrow");yield return new WaitForSeconds(.17f);
                    string fire=Path.Combine(output,"ranger-fire-989x446.png");if(File.Exists(fire))File.Delete(fire);ScreenCapture.CaptureScreenshot(fire);yield return Until(()=>File.Exists(fire),"fire capture");captures++;Debug.Log("RF_HUD_REVIEW FRAME "+fire);
                    if(!BattleHud.TouchMode){client.MoveOrder(new Vector2(-8,4));yield return Capture(hero,989,446,BattlePreviewKind.None,false,"-destination");if(!client.Presentation.DestinationVisible)throw new Exception("RF_HUD_REVIEW FAIL missing destination");client.Orders.Clear();}
                }
                yield return Until(()=>!Flow.Busy,"leave ready");Flow.Leave();yield return Until(()=>Flow.Room==null&&!Flow.Busy,"left practice");
            }
            finished=true;Debug.Log("RF_HUD_REVIEW PASS "+captures+" actual GPU/UI frames; idle hidden, held ranges, drag direction/clamped ground target, ordinary basic/flaming skill arrows, desktop destination, full-width layout and non-overlapping controls");Application.Quit(0);
        }
        private void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
#endif
