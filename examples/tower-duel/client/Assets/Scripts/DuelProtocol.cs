using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

namespace TowerDuel
{
    [Serializable] public sealed class EntityState
    {
        public long id, attackTick, hitTick, hitTarget, target, readyTick, pausedUntil, transformTick, buffedUntil;
        public int seat, hp, maxHP;
        public string kind;
        public float x,z,radius,dx,dz;
        public bool active,slowed,charging,airborne;
        public bool IsDeploying(double tick)=>readyTick>0&&tick<readyTick;
        public bool IsBuffed(double tick)=>hp>0&&buffedUntil>0&&tick<buffedUntil;
        public Vector3 Position => new Vector3(x,DuelMap.GroundHeight(z)+(airborne?2.35f:0),z);
    }
    [Serializable] public sealed class PlayerState { public int seat; public string name; public bool ready,connected,rematch; public float reconnectRemaining; }
    [Serializable] public sealed class EffectState { public long id,start,impact,end; public string kind; public int seat; public float x,z,radius; }
    [Serializable] public sealed class DuelMessage
    {
        public string type,room,match,mapHash,rulesHash,phase,reason,code,nextCard,resumeToken;
        public int protocol,seat,tickRate,snapshotRate,winner,count;
        public long tick,action,unitID,handVersion,actionWatermark;
        public float energy,countdown,remaining;
        public bool accepted,quick;
        public PlayerState[] players;
        public EntityState[] units,towers;
        public string[] hand;
        public EffectState[] effects;
    }
    [Serializable] public class HelloMessage
    {
        public string type="hello"; public int protocol=3;
        public string mode,room,name,mapHash,rulesHash,token="";
    }
    [Serializable] public sealed class DeckHelloMessage : HelloMessage
    {
        public string[] deck;
    }
    [Serializable] public sealed class DeployMessage
    {
        public string type="deploy",match,card="guard";
        public long action;public float x,z;
    }
    [Serializable] public sealed class ReadyMessage {public string type="ready",match;}
    [Serializable] public sealed class ControlMessage {public string type,match;}
    [Serializable] public sealed class DuelMap
    {
        public string id;public float width,length,riverHalfWidth,wallHalfWidth,wallHeight,bridgeHalfWidth,deploymentCellSize,pocketDepth;
        public float[] bridges; public EntityState[] towers;
        public bool HasWall=>wallHalfWidth>0;
        public float BarrierHalfWidth=>HasWall?wallHalfWidth:riverHalfWidth;
        private static bool flatGround;
        public static float GroundHeight(float z)=>flatGround?.02f:Mathf.Abs(z)<1.7f?.16f:.06f;
        public string Hash {get;private set;}
        public static DuelMap Load()
        {
            var asset=Resources.Load<TextAsset>("TowerDuel/map");
            if (!asset) throw new InvalidOperationException("Missing canonical map");
            var map=JsonUtility.FromJson<DuelMap>(asset.text);
            flatGround=map.HasWall;
            using (var hash=SHA256.Create()) map.Hash=BitConverter.ToString(hash.ComputeHash(asset.bytes)).Replace("-","").ToLowerInvariant();
            return map;
        }
        public bool CanDeploy(int seat,Vector3 point,DuelMessage state,CardDefinition card=null)
        {
            if(state==null||seat<0||seat>1)return false;
            if(card!=null&&card.IsSpell)return Finite(point.x)&&Finite(point.z)&&Mathf.Abs(point.x)<=width/2&&Mathf.Abs(point.z)<=length/2;
            foreach(var p in DuelRules.Formation(seat,point,card))if(!CanDeployPoint(seat,p,state,card?.BodyRadius??.32f))return false;
            return true;
        }
        private bool CanDeployPoint(int seat,Vector3 point,DuelMessage state,float radius)
        {
            if (!Finite(point.x)||!Finite(point.z)||Mathf.Abs(point.x)>width/2-radius||Mathf.Abs(point.z)>length/2-radius) return false;
            float depth=point.z*(seat==0?1:-1),bank=BarrierHalfWidth+radius+.08f;
            if(depth>-bank)
            {
                if(depth<bank||depth>BarrierHalfWidth+pocketDepth)return false;
                bool left=false,right=false;
                foreach(var tower in state.towers)
                {
                    if(tower.seat==seat||tower.kind!="tower"||tower.hp!=0)continue;
                    left|=tower.x<0;
                    right|=tower.x>0;
                }
                bool annexed=(point.x<0&&left)||(point.x>0&&right)||(point.x==0&&left&&right);
                if(!annexed)return false;
            }
            foreach (var tower in state.towers)
                if (tower.hp>0&&Vector2.Distance(new Vector2(point.x,point.z),new Vector2(tower.x,tower.z))<tower.radius+radius+.04f) return false;
            return true;
        }
        public Vector3 SnapDeployment(Vector3 point,CardDefinition card)
        {
            if(card!=null&&card.IsSpell)return point;
            float cell=deploymentCellSize;
            return new Vector3((Mathf.Floor((point.x+width*.5f)/cell)+.5f)*cell-width*.5f,point.y,
                (Mathf.Floor((point.z+length*.5f)/cell)+.5f)*cell-length*.5f);
        }
        public bool TryNearestDeployment(int seat,Vector3 point,DuelMessage state,CardDefinition card,out Vector3 nearest)
        {
            nearest=default;
            if(state==null||seat<0||seat>1||!Finite(point.x)||!Finite(point.z))return false;
            if(card!=null&&card.IsSpell)
            {
                nearest=new Vector3(Mathf.Clamp(point.x,-width*.5f,width*.5f),point.y,Mathf.Clamp(point.z,-length*.5f,length*.5f));
                return true;
            }
            // Resolve even wall/doorway aims to a legal cell; CanDeploy still rejects actual wall placement.
            var snapped=SnapDeployment(point,card);
            if(CanDeploy(seat,snapped,state,card)){nearest=snapped;return true;}
            // Measure from the original aim, including every member of a squad in legality checks.
            float best=float.PositiveInfinity,cell=deploymentCellSize;bool found=false;
            for(float x=-width*.5f+cell*.5f;x<width*.5f;x+=cell)
                for(float z=-length*.5f+cell*.5f;z<length*.5f;z+=cell)
                {
                    var candidate=new Vector3(x,point.y,z);
                    float distance=(candidate-point).sqrMagnitude;
                    if(distance>=best||!CanDeploy(seat,candidate,state,card))continue;
                    nearest=candidate;best=distance;found=true;
                }
            return found;
        }
        public static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
    }
    [Serializable] public sealed class CardDefinition
    {
        public string id,name,description,type,target;
        public int count,hp,damage,cooldown,windupTicks,deployTicks,delayTicks,durationTicks;
        public float cost,speed,range,aggro,splash,chargeDistance,chargeMultiplier,radius,slow,bodyRadius,weight,auraRadius,moveMultiplier,knockbackDistance,weightThreshold;
        public bool initialAir,hitAir;
        public int transformAfterTicks,hpMultiplier,pauseTicks;
        public float BodyRadius=>bodyRadius>0?bodyRadius:.32f;
        public bool IsSpell=>type=="spell";
    }
    [Serializable] public sealed class DuelRules
    {
        public const int DeckSize = 8;
        public float towerRange,keepRange;
        public string id;public int schema,towerHP,keepHP,towerDamage,towerCooldownTicks,duration;
        public CardDefinition[] cards;
        public string Hash {get;private set;}
        public static DuelRules Load()
        {
            var asset=Resources.Load<TextAsset>("TowerDuel/rules");
            if(!asset)throw new InvalidOperationException("Missing canonical card rules");
            var rules=JsonUtility.FromJson<DuelRules>(asset.text);
            using(var hash=SHA256.Create())rules.Hash=BitConverter.ToString(hash.ComputeHash(asset.bytes)).Replace("-","").ToLowerInvariant();
            if(rules.schema!=3||rules.towerCooldownTicks<1||rules.cards==null||rules.cards.Length<DeckSize)throw new InvalidOperationException("Invalid card rules");
            var ids=new HashSet<string>();foreach(var card in rules.cards)if(card==null||string.IsNullOrEmpty(card.id)||!ids.Add(card.id)||(card.type!="troop"&&card.type!="spell")||!DuelMap.Finite(card.cost)||card.cost<=0||card.cost>10)throw new InvalidOperationException("Invalid card catalog");
            foreach(var card in rules.cards)if(!card.IsSpell&&(card.deployTicks<0||card.deployTicks>300))throw new InvalidOperationException("Invalid troop deployment time");
            return rules;
        }
        public CardDefinition Find(string id){foreach(var card in cards)if(card.id==id)return card;return null;}
        public static Vector3[] Formation(int seat,Vector3 center,CardDefinition card)
        {
            if(card==null||card.IsSpell||card.count==1)return new[]{center};
            float forward=seat==0?1:-1;
            return new[]{center+new Vector3(-.48f,0,-.30f*forward),center+new Vector3(.48f,0,-.30f*forward),center+new Vector3(0,0,.50f*forward)};
        }
    }
    public sealed class DuelFrame
    {
        public readonly DuelMessage State;
        public readonly Dictionary<long,EntityState> Units=new Dictionary<long,EntityState>();
        public DuelFrame(DuelMessage state) {State=state;foreach(var entity in state.units) Units.Add(entity.id,entity);}
    }
    public static class DuelText
    {
        public static string Reason(string code)
        {
            switch(code)
            {
                case "invalid_placement":return "請部署在亮色格內；拆掉敵方前塔可開放該側前線。";
                case "occupied":return "這個位置有士兵，請換個落點。";
                case "not_enough_energy":return "能量不足，請等待回復。";
                case "card_not_in_hand":return "這張牌還沒輪到手上。";
                case "invalid_deck":return "請選擇八張不同的卡牌後再開始。";
                case "effect_limit":return "場上效果較多，請稍候。";
                case "resume_expired":return "恢復對戰的時間已過，請重新開房。";
                case "seat_connected":return "正在等待原連線釋放，稍後會再嘗試。";
                case "seat_reserved":return "這個席位正在等待玩家重新連線。";
                case "reconnect_timeout":return "玩家未在寬限時間內返回。";
                case "both_offline":return "雙方離線，對戰已結束。";
                case "not_playing":return "等待雙方準備後開始。";
                case "room_not_found":return "找不到房間，請確認房間碼。";
                case "room_full":return "房間已滿。";
                case "room_started":return "這個房間已開始對戰。";
                case "invalid_room":return "請輸入六位房間碼。";
                case "invalid_name":return "名稱請使用二十字以內的文字。";
                case "map_mismatch":return "地圖版本不同，請更新客戶端。";
                case "rules_mismatch":return "兵種版本不同，請更新客戶端。";
                case "unit_limit":return "戰場單位已達上限，請稍候。";
                case "opponent_left":return "對手已離開對戰。";
                case "keep_destroyed":return "主堡已被摧毀。";
                case "time_score":return "時間到，以破塔數分出勝負。";
                case "sudden_death":return "延長賽破塔決勝。";
                case "time_draw":return "時間結束，雙方平手。";
                case "simultaneous":return "雙方同時破塔，平手。";
                case "connection_closed":return "連線已中斷，請重新加入。";
                case "send_failed":return "送出失敗，請重新連線。";
                default:return "連線或操作失敗："+code;
            }
        }
    }
}
