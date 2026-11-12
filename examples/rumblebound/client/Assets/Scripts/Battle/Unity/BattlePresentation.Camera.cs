using System.Linq;
using UnityEngine;
using Rumblebound.Presentation;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed partial class BattlePresentation
    {
        private Vector2? cameraPeek;
        private Vector2 cameraFocus;
        private bool cameraValid;
        private string cameraMatch;
        private long cameraLife=-1,cameraTick;
        private LineRenderer targetRing;
        private LineRenderer destinationMarker;
        public bool DestinationVisible=>destinationMarker!=null&&destinationMarker.gameObject.activeSelf;
        public Vector3 DestinationPoint=>client!=null&&client.TouchAttackPreview?new Vector3(client.TouchAttackDestination.x,.07f,client.TouchAttackDestination.y):client!=null?new Vector3((float)client.Orders.DestinationX,.07f,(float)client.Orders.DestinationZ):Vector3.zero;
        private float cameraZoom=8;
        public float CameraZoom=>cameraZoom;
        public void SetCameraZoom(float value){cameraZoom=Mathf.Clamp(value,5,10);PlayerPrefs.SetFloat("Rumblebound.CameraZoom.v4",cameraZoom);PlayerPrefs.Save();}
        public void PeekAt(Vector2? point){cameraPeek=point;}
        public void Recenter(){cameraPeek=null;cameraValid=false;}
        private void FollowCamera(WorldView world,Vector2 aim){
            var own=world.players.FirstOrDefault(p=>p.own)??world.players.FirstOrDefault(p=>p.state.hp>0&&!p.state.left);if(own==null||stage==null)return;
            stage.Camera.orthographicSize=cameraZoom;
            var target=cameraPeek??new Vector2((float)own.x,(float)own.z);
            if(!cameraPeek.HasValue){var lead=aim-target;if(lead.sqrMagnitude>.01f)target+=lead.normalized*.65f;}
            float half=(float)content.Map.arenaHalfSize,wide=stage.Camera.orthographicSize*stage.Camera.aspect,deep=stage.Camera.orthographicSize/Mathf.Sin(Mathf.Atan2(26,18));
            target.x=Mathf.Clamp(target.x,-Mathf.Max(0,half-wide),Mathf.Max(0,half-wide));
            target.y=Mathf.Clamp(target.y,-Mathf.Max(0,half-deep),Mathf.Max(0,half-deep));
            bool cut=!cameraValid||cameraMatch!=world.snapshot.matchID||cameraLife!=own.state.life||world.tick<cameraTick;
            cameraFocus=cut?target:Vector2.Lerp(cameraFocus,target,1-Mathf.Exp(-12*Time.unscaledDeltaTime));
            cameraValid=true;cameraMatch=world.snapshot.matchID;cameraLife=own.state.life;cameraTick=world.tick;
            var focus=new Vector3(cameraFocus.x,0,cameraFocus.y);
            stage.Camera.transform.position=focus+new Vector3(0,26,-18);stage.Camera.transform.LookAt(focus);
        }
        private void PresentLock(WorldView world){
            var target=world.players.FirstOrDefault(p=>p.state.playerID==client.CurrentTargetID&&p.state.hp>0&&!p.state.left);
            bool showLock=BattleHud.TouchMode?client.TouchAttackPreview||client.Orders.TargetID!=null:client.PreviewKind==BattlePreviewKind.Basic&&client.PreviewSelecting;
            if(!showLock||target==null||world.frozen||client.Flow.Playback.Active||BattleHud.BlocksGameplay){if(targetRing!=null)targetRing.gameObject.SetActive(false);return;}
            if(targetRing==null){targetRing=GroundCastPresentation.Circle("Selected enemy",new Color(1,.35f,.15f),.05f);targetRing.transform.SetParent(transform,false);}
            targetRing.gameObject.SetActive(true);GroundCastPresentation.PlaceCircle(targetRing,new Vector3((float)target.x,.05f,(float)target.z),(float)content.Rules.playerRadius+.3f);
        }
        private void PresentDestination(WorldView world){
            bool visible=(client.TouchAttackPreview||client.Orders.Moving)&&!world.frozen&&!client.Flow.Playback.Active&&client.Flow.Battle.CanControl&&!BattleHud.BlocksGameplay;
            if(!visible){if(destinationMarker!=null)destinationMarker.gameObject.SetActive(false);return;}
            if(destinationMarker==null){destinationMarker=GroundCastPresentation.Circle("Movement destination",Color.green,.07f);destinationMarker.transform.SetParent(transform,false);}
            destinationMarker.sharedMaterial.color=client.TouchAttackPreview||client.Orders.AttackMoving?new Color(1,.45f,.2f):new Color(.35f,1,.6f);
            destinationMarker.gameObject.SetActive(true);
            GroundCastPresentation.PlaceCircle(destinationMarker,DestinationPoint,.32f);
        }
    }
}
