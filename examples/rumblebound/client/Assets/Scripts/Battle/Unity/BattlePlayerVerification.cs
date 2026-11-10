#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle.Unity
{
    // Explicit development-only launch flag. Drives the existing application
    // owner through real HTTP/transport; never patches authority or scene state.
    public sealed class BattlePlayerVerification:MonoBehaviour
    {
        private BattleClient client;
        private GameFlow flow;
        private string role,roomFile,renderDirectory;
        private bool finished;
        private void Awake(){client=GetComponent<BattleClient>();Application.SetStackTraceLogType(LogType.Log,StackTraceLogType.None);Application.logMessageReceived+=Log;}
        private void Log(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Error){if(!finished){finished=true;Application.Quit(1);}}}
        private static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException("RF_BATTLE_PLAYER FAIL "+reason);}
        private IEnumerator Until(Func<bool> condition,string label,double seconds=45)
        {
            Debug.Log("RF_BATTLE_PLAYER STEP "+role+" "+label);
            double end=Time.realtimeSinceStartupAsDouble+seconds;
            while(!condition()){if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException("RF_BATTLE_PLAYER FAIL "+label+": "+flow?.Notice+" "+flow?.Battle.Status);yield return null;}
        }
        private PlayerState Own{
            get{if(flow.Battle.State.Latest==null)throw new InvalidOperationException("RF_BATTLE_PLAYER FAIL lost session: "+flow.Notice+" "+flow.Battle.Status+" "+flow.Battle.ValidationFailure);return flow.Battle.State.Latest.players.First(p=>p.playerID==flow.Battle.State.PlayerID);}
        }
        // Render the game's camera into our own texture; no window/desktop readback.
        // This verifies 3D content only. UI and physical input remain separate.
        private IEnumerator Capture(string stage)
        {
            if(renderDirectory==null)yield break;
            yield return null;
            var camera=client.Presentation.Camera;
            Require(camera!=null,"camera unavailable: "+stage);
            var target=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);
            var active=RenderTexture.active;var viewport=camera.rect;
            var frame=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{
                camera.rect=new Rect(0,0,1,1);
                Rumblebound.Presentation.URPCameras.Render(camera,target);
                RenderTexture.active=target;frame.ReadPixels(new Rect(0,0,target.width,target.height),0,0);frame.Apply();
                var colors=frame.GetPixels32();int visible=0,magenta=0,low=255,high=0;
                foreach(var color in colors){int level=(color.r+color.g+color.b)/3;low=Math.Min(low,level);high=Math.Max(high,level);if(Math.Max(color.r,Math.Max(color.g,color.b))-Math.Min(color.r,Math.Min(color.g,color.b))>25)visible++;if(color.r>220&&color.b>220&&color.g<40)magenta++;}
                File.WriteAllBytes(Path.Combine(renderDirectory,role+"-"+stage+".png"),frame.EncodeToPNG());
                bool game=stage=="battle"||stage=="practice"||stage=="replay";
                Require(high-low>30&&(!game||visible>colors.Length/100)&&magenta<colors.Length/200,"rendered content/shaders: "+stage+" (colored="+visible+", magenta="+magenta+", contrast="+(high-low)+")");
                Debug.Log("RF_BATTLE_RENDER PASS "+role+" "+stage+": "+target.width+"x"+target.height+" offscreen 3D (no UI/input claim)");
            }finally{camera.rect=viewport;RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);Destroy(frame);}
        }
        private IEnumerator Move(double x,double z)
        {
            // Practice now starts in the central lane. A direct vector to the
            // old smoke waypoints can run into cover; use the production swept
            // navigation without changing authority, collision or spawn state.
            var own=Own;var route=ArenaNavigation.Find(flow.Content.Map,own.x,own.z,x,z,flow.Content.Rules.playerRadius);
            Require(route.Length>0&&Math.Abs(route[route.Length-1].x-x)+Math.Abs(route[route.Length-1].z-z)<.01,"waypoint has no exact legal route");
            double distance=0,previousX=own.x,previousZ=own.z;
            foreach(var point in route){distance+=Math.Sqrt(Math.Pow(point.x-previousX,2)+Math.Pow(point.z-previousZ,2));previousX=point.x;previousZ=point.z;}
            double speed=flow.Content.Hero(own.heroID).speed*.6;
            double end=Time.realtimeSinceStartupAsDouble+Math.Max(10,distance/speed*2+3);
            Debug.Log("RF_BATTLE_PLAYER STEP "+role+" waypoint "+x+","+z+" from "+own.x+","+own.z+" route="+route.Length);
            foreach(var point in route)while(Math.Sqrt(Math.Pow(Own.x-point.x,2)+Math.Pow(Own.z-point.z,2))>.12){
                if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException("RF_BATTLE_PLAYER FAIL waypoint "+x+","+z+" at "+Own.x+","+Own.z+" tick="+flow.Battle.State.Tick+" ack="+Own.ack+" "+flow.Battle.Status);
                var delta=new Vector2((float)(point.x-Own.x),(float)(point.z-Own.z));client.VerificationDrive=delta.normalized*.6f;yield return null;
            }
            client.VerificationDrive=Vector2.zero;
        }
        private IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();role="host";
            for(int i=0;i+1<args.Length;i++){if(args[i]=="-rf-battle-role")role=args[++i];else if(args[i]=="-rf-room-file")roomFile=args[++i];else if(args[i]=="-rf-render-directory")renderDirectory=args[++i];}
            Require(Path.IsPathRooted(roomFile),"isolated room-file required");
            if(renderDirectory!=null){Require(Path.IsPathRooted(renderDirectory)&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"graphics/output required");Directory.CreateDirectory(renderDirectory);}
            yield return Until(()=>client.Flow?.Content!=null&&!client.Flow.Busy,"config");flow=client.Flow;
            flow.Selection.SpeculativeActions=BattleNetcode.ActionsFactory!=null;flow.Selection.Server.codec=BattleNetcode.DecoderFactory!=null?"delta":"json";
            if(Array.IndexOf(args,"-rf-use-webtransport")>=0)flow.Selection.Transport="webtransport";
            Require(flow.Guest(),"guest");yield return Until(()=>flow.Identity.IsAuthenticated&&!flow.Busy,"guest reply");
            if(role=="host"){
                Require(flow.Create(),"create");yield return Until(()=>flow.Room!=null&&!flow.Busy,"create reply");File.WriteAllText(roomFile+".tmp",flow.Room.code);File.Move(roomFile+".tmp",roomFile);
            }else{
                yield return Until(()=>File.Exists(roomFile),"host room code");Require(flow.Join(File.ReadAllText(roomFile)),"join");yield return Until(()=>flow.Room!=null&&!flow.Busy,"join reply");
            }
            Require(flow.Team(flow.Room.teams[role=="host"?0:1].teamID),"team");yield return Until(()=>!flow.Busy,"team reply");
            Require(flow.Hero(role=="host"?"ranger":"tank"),"hero");yield return Until(()=>!flow.Busy,"hero reply");
            Require(flow.Ready(true),"ready");yield return Until(()=>!flow.Busy,"ready reply");
            if(role=="host"){yield return Until(()=>flow.Room.canStart&&!flow.Busy,"two peers ready");Require(flow.Start(),"start");}
            yield return Until(()=>flow.Battle.CanControl&&flow.Battle.State.Latest.players.Length==2&&!flow.Busy,"actual scene loading gate");
            Require(client.Presentation.ActorCount==2&&client.Presentation.CoverCount==flow.Content.Map.obstacles.Length,"shared scene/hero resources");
            string player=Own.playerID,entity=Own.entityID,user=flow.Identity.UserID;uint generation=flow.Battle.State.Generation;
            client.VerificationDrive=new Vector2(0,.5f);double original=Own.z;
            yield return Until(()=>Own.z>original+.4&&Own.ack>=5,"authoritative input ACK/movement");client.VerificationDrive=Vector2.zero;
            yield return Capture("battle");
            if(role!="host"){
                Require(flow.AimAction("fire",Own.x-1,Own.z,client.Now),"tank action");yield return Until(()=>flow.Battle.LastActionCode=="melee","melee confirmation");
                yield return Until(()=>flow.Stage=="result","authoritative forfeit result",90);Require(flow.Battle.State.Latest.match.result.winnerTeamID==Own.teamID,"winner result");
                yield return Capture("result");
                yield return Until(()=>!flow.Busy,"result poll");Require(flow.ReturnToRoom(),"return");yield return Until(()=>flow.Stage=="room"&&!flow.Busy,"room reset");Require(flow.Leave(),"room leave");yield return Until(()=>flow.Room==null&&!flow.Busy,"leave reply");
            }else{
                flow.StartRecording();Require(flow.AimAction("fire",Own.x+5,Own.z,client.Now),"ranger action");yield return Until(()=>flow.Battle.LastActionCode=="fired"&&client.Presentation.ProjectileInstances>0,"ranged confirmation/art");
                yield return Until(()=>flow.Recording.Frames>=5,"authority recording");flow.Recording.Stop();string recorded=flow.Recording.Export();
                flow.Battle.Transport.InterruptForDiagnostics();yield return Until(()=>flow.Battle.CanControl&&flow.Battle.State.Generation>generation,"native automatic recovery");Require(flow.Identity.UserID==user&&Own.playerID==player&&Own.entityID==entity,"same identity/seat");
                yield return Until(()=>!flow.Busy,"logout readiness");Require(flow.Logout(),"forfeit logout");yield return Until(()=>!flow.Identity.IsAuthenticated&&!flow.Busy,"logout");
                Require(flow.Guest(),"practice guest");yield return Until(()=>flow.Identity.IsAuthenticated&&!flow.Busy,"practice guest reply");Require(flow.Practice("mage","ranger","idle"),"mage practice");yield return Until(()=>flow.Battle.CanControl&&!flow.Busy,"new match scene loading");
                var west=flow.Content.Map.obstacles.FirstOrDefault(o=>o.id=="cover-west");
                double safeZ=west==null?flow.Content.Rules.controlPointRadius+1:west.z+west.halfZ+flow.Content.Rules.playerRadius+1;
                double safeX=west==null?0:west.x-west.halfX-flow.Content.Rules.playerRadius-1;
                yield return Move(safeX,safeZ);yield return Move(0,safeZ);
                // Derive clear waypoints from the actual cover dimensions. The
                // old (-6,3) destination is inside the larger map's west cover.
                yield return Until(()=>flow.Battle.State.Latest.match.scores.Any(s=>s.score>0),"control point scoring");
                yield return Move(0,0);
                var target=flow.Battle.State.Latest.players.First(p=>p.bot);long life=target.life;
                for(int i=0;i<3;i++){
                    yield return Until(()=>Own.fireReadyTick<=flow.Battle.State.Tick,"cast cooldown");
                    target=flow.Battle.State.Latest.players.First(p=>p.bot);int hp=target.hp;
                    Require(flow.AimAction("fire",target.x,target.z,client.Now),"ground cast");
                    yield return Until(()=>flow.Battle.State.Latest.players.First(p=>p.bot).hp<hp,"authoritative ground damage");
                }
                Require(flow.Battle.State.Latest.players.First(p=>p.bot).hp==0,"death");
                yield return Until(()=>flow.Battle.State.Latest.players.First(p=>p.bot).life>life&&flow.Battle.State.Latest.players.First(p=>p.bot).hp>0,"new-life respawn");
                yield return Capture("practice");
                yield return Until(()=>!flow.Busy,"practice leave readiness");Require(flow.Leave(),"practice leave");yield return Until(()=>flow.Room==null&&!flow.Busy,"practice leave reply");
                if(BattleNetcode.PlaybackFactory!=null){flow.LoadPlayback(recorded);flow.Playback.Clock.Pause();double first=flow.Playback.Clock.FirstTick;flow.Playback.Seek(first);flow.Playback.Clock.Speed=2;flow.Playback.Clock.Play();yield return Until(()=>flow.Playback.Clock.Tick>first,"SDK offline playback");yield return Capture("replay");flow.EndPlayback();}
            }
            finished=true;Debug.Log("RF_BATTLE_PLAYER PASS "+role+": real Unity API/JSON/SDK, two-player room/loading/movement/actions and shared art; "+(role=="host"?"identity recovery, new-match mage damage/death/respawn, point scoring, recording and installed playback":"authoritative result/return/leave"));Application.Quit(0);
        }
        private void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
#endif
