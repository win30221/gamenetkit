using System.Collections.Generic;
using UnityEngine;
using Rumblebound.Gameplay;
using Rumblebound.Presentation;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed partial class BattlePresentation
    {
        private readonly Dictionary<string,long> basicAnimations=new Dictionary<string,long>();
        private readonly Dictionary<string,LineRenderer> basicTraces=new Dictionary<string,LineRenderer>();
        private readonly Dictionary<string,double> traceUntil=new Dictionary<string,double>();
        private sealed class BasicArrow {public GameObject root;public Vector3 start,target;public string targetID;public double begin,end;}
        private readonly List<BasicArrow> basicArrows=new List<BasicArrow>();
        private readonly Stack<GameObject> freeBasicArrows=new Stack<GameObject>();
        public int BasicArrowCount=>basicArrows.Count;
        private void PresentBasicAnimation(PlayerState player,MiniActorView actor,bool own,long tick){
            if(player.basicActionID<=0||basicAnimations.TryGetValue(player.playerID,out long prior)&&prior==player.basicActionID)return;
            basicAnimations[player.playerID]=player.basicActionID;
            if(player.attackReleaseTick>tick&&(!own||!WasPreviewed(player.basicActionID)))actor.PredictAttack();
        }
        private void PresentBasicHit(CombatEvent hit){
            Play("shot",hit.ownerID,hit.x,hit.z,hit.dx,hit.dz);
            var target=actors.TryGetValue(hit.targetID??"",out var victim)?victim.transform.position:new Vector3((float)hit.x,0,(float)hit.z);
            target.y=ProjectilePresentation.ArrowHeight;
            effects.Play("player",target,Vector2.right,now);audioBus.Play("player",target,hit.ownerID==current.playerID||hit.targetID==current.playerID,now);
            victim?.FlashHit(now);
            if(!actors.TryGetValue(hit.ownerID,out var source))return;
            if(heroes.TryGetValue(hit.ownerID,out string hero)&&hero=="ranger"){
                if(basicArrows.Count>=32)ReleaseBasicArrow(0);
                var arrow=freeBasicArrows.Count>0?freeBasicArrows.Pop():Instantiate(Resources.Load<GameObject>("KayKit/Arrow"),transform);
                arrow.name="Basic arrow";arrow.SetActive(true);var origin=source.transform.position;origin.y=ProjectilePresentation.ArrowHeight;
                arrow.transform.position=origin;
                basicArrows.Add(new BasicArrow{root=arrow,start=origin,target=target,targetID=hit.targetID,begin=now,end=now+Mathf.Clamp(Vector3.Distance(origin,target)/35,.08f,.25f)});
                return;
            }
            if(!basicTraces.TryGetValue(hit.ownerID,out var line)){line=GroundCastPresentation.Circle("Locked attack "+hit.ownerID,new Color(1,.8f,.35f),.05f);line.transform.SetParent(transform,false);line.loop=false;line.positionCount=2;basicTraces[hit.ownerID]=line;}
            line.gameObject.SetActive(true);var start=source.transform.position;start.y=ProjectilePresentation.ArrowHeight;
            line.SetPosition(0,start);line.SetPosition(1,target);traceUntil[hit.ownerID]=now+.12;
        }
        private void ReleaseBasicArrow(int index){var arrow=basicArrows[index];arrow.root.SetActive(false);freeBasicArrows.Push(arrow.root);basicArrows.RemoveAt(index);}
        private void RenderBasicTraces(){
            foreach(var entry in basicTraces)if(!traceUntil.TryGetValue(entry.Key,out double until)||now>=until)entry.Value.gameObject.SetActive(false);
            for(int i=basicArrows.Count-1;i>=0;i--){var arrow=basicArrows[i];if(now>=arrow.end){ReleaseBasicArrow(i);continue;}
                if(actors.TryGetValue(arrow.targetID??"",out var target)){arrow.target=target.transform.position;arrow.target.y=ProjectilePresentation.ArrowHeight;}
                var at=Vector3.Lerp(arrow.start,arrow.target,(float)((now-arrow.begin)/(arrow.end-arrow.begin)));var direction=arrow.target-arrow.start;
                arrow.root.transform.position=at;if(direction.sqrMagnitude>.00001f)arrow.root.transform.rotation=Quaternion.LookRotation(direction);
            }
        }
        private void ClearBasics(){while(basicArrows.Count>0)ReleaseBasicArrow(0);foreach(var line in basicTraces.Values)DestroyLine(line);basicTraces.Clear();traceUntil.Clear();basicAnimations.Clear();}
        private void DisposeBasics(){while(freeBasicArrows.Count>0)Destroy(freeBasicArrows.Pop());}
    }
}
