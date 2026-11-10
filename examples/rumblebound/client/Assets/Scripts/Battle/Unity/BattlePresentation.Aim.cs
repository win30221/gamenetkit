using System.Linq;
using UnityEngine;
using Rumblebound.Gameplay;
using Rumblebound.Presentation;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed partial class BattlePresentation
    {
        private LineRenderer inputRange,dashDirection;
        private void PresentAim(WorldView world,Vector2 aim){
            HideAim();
            var own=world.players.FirstOrDefault(p=>p.own);
            if(own==null||own.state.hp<=0||own.state.left||world.frozen||client.Flow.Playback.Active||!client.Flow.Battle.CanControl||BattleHud.BlocksGameplay)return;
            var hero=content.Hero(own.state.heroID);var kind=client.PreviewKind;
            if(hero==null||kind==BattlePreviewKind.None)return;
            var position=new Vector3((float)own.x,0,(float)own.z);
            var direction=aim-new Vector2(position.x,position.z);
            if(kind==BattlePreviewKind.Basic||kind==BattlePreviewKind.Dash){
                if(inputRange==null){inputRange=GroundCastPresentation.Circle("Held action range",Color.cyan,.03f);inputRange.transform.SetParent(transform,false);}
                double reach=kind==BattlePreviewKind.Basic?hero.basicRange:hero.dashSpeed*content.Rules.dashTicks/content.Rules.tickRate;
                bool ready=(kind==BattlePreviewKind.Basic?own.state.basicReadyTick:own.state.dashReadyTick)<=world.tick;
                inputRange.sharedMaterial.color=ready?new Color(.3f,.8f,1f):new Color(.45f,.5f,.55f);
                inputRange.gameObject.SetActive(true);GroundCastPresentation.PlaceCircle(inputRange,position,(float)reach);
                if(kind==BattlePreviewKind.Dash&&client.PreviewSelecting){
                    var axis=direction.sqrMagnitude>.001f?direction.normalized:Vector2.up;
                    if(ArenaCollision.FirstMapHit(content.Map,own.x,own.z,axis.x*reach,axis.y*reach,content.Rules.playerRadius,false,out double at,out _,out _))reach*=at;
                    if(dashDirection==null){dashDirection=GroundCastPresentation.Circle("Dash direction",new Color(.8f,.7f,1),.07f);dashDirection.transform.SetParent(transform,false);dashDirection.loop=false;dashDirection.positionCount=2;}
                    dashDirection.gameObject.SetActive(true);dashDirection.SetPosition(0,position+Vector3.up*.08f);dashDirection.SetPosition(1,position+new Vector3(axis.x*(float)reach,.08f,axis.y*(float)reach));
                }return;
            }
            bool skillReady=own.state.fireReadyTick<=world.tick;
            if(castRange==null){castRange=GroundCastPresentation.Circle("Skill range",Color.cyan,.03f);castRange.transform.SetParent(transform,false);}
            castRange.sharedMaterial.color=skillReady?new Color(.3f,.72f,.95f):new Color(.45f,.5f,.55f);
            double range=hero.attackKind=="ground"?hero.castRange:hero.attackKind=="melee"?hero.meleeRange:HeroRules.RangedReach(content.Rules,hero);
            castRange.gameObject.SetActive(true);GroundCastPresentation.PlaceCircle(castRange,position,(float)range);
            if(!client.PreviewSelecting)return;
            if(hero.attackKind!="ground"){
                if(attackRange==null)attackRange=new AttackRangePresentation(transform);
                attackRange.Show(content.Rules,hero,content.Map,position,direction,skillReady);return;
            }
            if(castTarget==null){castTarget=GroundCastPresentation.Circle("Skill target",Color.yellow,.05f);castTarget.transform.SetParent(transform,false);}
            castTarget.gameObject.SetActive(true);
            bool valid=BattleAimAssist.GroundPoint(content,hero,position.x,position.z,aim.x,aim.y,out double px,out double pz);
            castTarget.sharedMaterial.color=!valid?new Color(1,.2f,.15f):skillReady?new Color(1,.7f,.15f):new Color(.65f,.55f,.3f);
            GroundCastPresentation.PlaceCircle(castTarget,new Vector3((float)px,0,(float)pz),(float)hero.impactRadius);
        }
        private void HideAim(){
            attackRange?.Hide();
            if(targetRing!=null)targetRing.gameObject.SetActive(false);
            if(castRange!=null)castRange.gameObject.SetActive(false);if(castTarget!=null)castTarget.gameObject.SetActive(false);if(inputRange!=null)inputRange.gameObject.SetActive(false);if(dashDirection!=null)dashDirection.gameObject.SetActive(false);
        }
    }
}
