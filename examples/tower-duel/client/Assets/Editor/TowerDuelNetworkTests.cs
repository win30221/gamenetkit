using System;
using System.Collections;
using System.Reflection;
using Realtime.Core;
using UnityEngine;

namespace TowerDuel.Editor
{
    // Run in an empty Play Mode scene; transport arrivals still pass through the actual SDK inbox.
    public static class TowerDuelNetworkTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const string ResumeKey="TowerDuel.Resume.v3";
        private static readonly MethodInfo Pump=typeof(DuelSession).GetMethod("Pump",Private,null,new[]{typeof(double)},null);
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Private).GetValue(owner);
        private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Private).SetValue(owner,value);
        private static void Advance(DuelSession session,double now)=>Pump.Invoke(session,new object[]{now});

        private sealed class Fixture : IDisposable
        {
            public readonly DuelMap Map=DuelMap.Load();
            public readonly DuelRules Rules=DuelRules.Load();
            public readonly DuelSession Session;
            public double Now;
            public ReconnectPolicy Recovery=>Field<ReconnectPolicy>(Session,"recovery");
            public Fixture()
            {
                Session=new DuelSession(Map,Rules);
                // Failed retries throw before opening a socket, keeping timer tests deterministic.
                Set(Session,"server","ftp://invalid");
                Set(Session,"playerName","Network regression");
                Set(Session,"callback","TowerDuelNetworkTests");
            }
            public DuelMessage State(long tick,string type="snapshot",string match="network-test",EffectState[] effects=null,float x=0)
            {
                var towers=new EntityState[Map.towers.Length];
                for(int i=0;i<towers.Length;i++)
                {
                    var t=Map.towers[i];
                    towers[i]=new EntityState{id=t.id,seat=t.seat,kind=t.kind,x=t.x,z=t.z,radius=t.radius,hp=100,maxHP=100};
                }
                return new DuelMessage{type=type,protocol=3,room="TESTAA",match=match,mapHash=Map.Hash,rulesHash=Rules.Hash,
                    seat=0,tick=tick,tickRate=30,snapshotRate=15,phase="playing",energy=10,resumeToken=new string('a',43),
                    players=new[]{new PlayerState{seat=0,connected=true},new PlayerState{seat=1,connected=true}},towers=towers,
                    units=new[]{new EntityState{id=100,seat=0,kind="guard",x=x,z=-3,radius=.32f,hp=100,maxHP=100}},
                    hand=new[]{Rules.cards[0].id,Rules.cards[1].id,Rules.cards[2].id,Rules.cards[3].id},nextCard=Rules.cards[4].id,
                    effects=effects??Array.Empty<EffectState>()};
            }
            public void Deliver(DuelMessage message)
            {
                // Capture time before arrival so assertions do not depend on Editor frame speed.
                Now=DuelSession.Now;
                Session.Transport.Receive(Session.Transport.Epoch,JsonUtility.ToJson(message));
                Advance(Session,Now);
            }
            public void Error(string code)=>Deliver(new DuelMessage{type="error",code=code});
            public void Dispose()=>Session.Dispose();
        }

        public static string Run()
        {
            Require(Application.isPlaying,"Run TowerDuelNetworkTests in an empty Play Mode scene.");
            bool hadResume=PlayerPrefs.HasKey(ResumeKey);
            string savedResume=PlayerPrefs.GetString(ResumeKey,"");
            try
            {
                BackoffAndExpiry();
                RestoreAndPendingAction();
                CancellationAndFatalErrors();
                SharedEffectTimeline();
                StalledTimelineAndMatchReset();
                return "TOWER_DUEL_NETWORK_TESTS PASS: SDK backoff/expiry, saved-seat restore, pending-action recovery, cancellation, shared effect timing, snapshot stalls and match resets";
            }
            finally
            {
                if(hadResume)PlayerPrefs.SetString(ResumeKey,savedResume);else PlayerPrefs.DeleteKey(ResumeKey);
                PlayerPrefs.Save();
            }
        }

        private static void BackoffAndExpiry()
        {
            using(var f=new Fixture())
            {
                f.Deliver(f.State(100,"welcome"));
                f.Error("connection_closed");
                Require(f.Session.Recovering&&!f.Session.CanAct,"Welcome did not arm recovery.");
                double deadline=f.Recovery.Deadline,next=f.Recovery.Next;
                Require(Math.Abs(next-f.Now-.5)<1e-6,"Initial retry did not use the SDK delay.");
                Advance(f.Session,next-.001);
                Require(f.Recovery.Attempts==0,"Retried before the SDK schedule.");
                foreach(double delay in new[]{1d,2d,3d,3d})
                {
                    next=f.Recovery.Next;
                    Advance(f.Session,next);
                    Require(Math.Abs(f.Recovery.Next-next-delay)<1e-6,"Failed retry did not use capped exponential backoff.");
                    Require(f.Recovery.Deadline==deadline,"A failed attempt extended the recovery window.");
                }
                // The window also expires while waiting for a handshake, before its own timeout.
                Set(f.Session,"<Connecting>k__BackingField",true);
                Advance(f.Session,deadline);
                Require(f.Session.State==null&&!f.Session.Recovering&&!f.Session.Connecting,"Recovery did not expire at its deadline.");
                Require(!f.Recovery.Armed&&double.IsPositiveInfinity(f.Recovery.Next),"Expiry left a retry armed.");
            }
        }

        private static void RestoreAndPendingAction()
        {
            using(var f=new Fixture())
            {
                f.Deliver(f.State(100,"welcome"));
                Require(f.Session.Deploy(new Vector3(-5,0,-3)),"Could not create a pending deployment.");
                long action=Field<DeployMessage>(f.Session,"pending").action;
                int oldEpoch=f.Session.Transport.Epoch;
                f.Error("connection_closed");
                f.Session.Transport.Receive(oldEpoch,JsonUtility.ToJson(f.State(500,"welcome")));
                Advance(f.Session,f.Now);
                Require(f.Session.Recovering&&f.Session.State.tick==100,"An old transport callback replaced the recovering state.");
                var welcome=f.State(120,"welcome");welcome.actionWatermark=action;
                f.Deliver(welcome);
                Require(!f.Session.Recovering&&f.Session.Resumes==1&&f.Session.Pending,"Resume lost the pending action or failed to reset recovery.");
                var result=new DuelMessage{type="action",match=welcome.match,action=action,accepted=true};
                f.Deliver(result);f.Deliver(result);
                Require(!f.Session.Pending&&f.Session.Accepted==1,"A repeated confirmation was presented more than once.");
                Require(f.Session.Deploy(new Vector3(-5,0,-3))&&Field<DeployMessage>(f.Session,"pending").action>action,"Resume reused an acknowledged action ID.");
            }
            using(var restored=new Fixture())
            {
                Require(restored.Session.TryRestore("ftp://invalid","TowerDuelNetworkTests"),"Saved-seat restoration did not start.");
                Require(restored.Session.Recovering&&restored.Recovery.Armed,"Saved credentials did not arm the SDK policy.");
                restored.Deliver(restored.State(140,"welcome"));
                Require(restored.Session.Resumes==1&&!restored.Session.Recovering&&restored.Recovery.Attempts==0,"Restored welcome did not reset retry state.");
            }
        }

        private static void CancellationAndFatalErrors()
        {
            using(var f=new Fixture())
            {
                f.Error("connection_closed");
                Require(!f.Session.Recovering&&!f.Recovery.Armed,"An initial failure without a seat started automatic retries.");
                f.Deliver(f.State(100,"welcome"));f.Error("connection_closed");
                f.Session.Disconnect();Advance(f.Session,f.Now+60);
                Require(!f.Session.Recovering&&!f.Recovery.Armed&&f.Recovery.Attempts==0,"Manual disconnect restarted recovery.");
                f.Deliver(f.State(200,"welcome"));f.Error("connection_closed");f.Error("rules_mismatch");
                Require(f.Session.State==null&&!f.Session.Recovering&&!f.Recovery.Armed,"An incompatible server kept retrying.");
            }
        }

        private static EffectState Fireball(long start,long impact,long end)=>new EffectState{id=900,kind="fireball",seat=0,x=-5,z=-3,radius=2.2f,start=start,impact=impact,end=end};
        private static IDictionary Visuals(DuelEffects effects)=>Field<IDictionary>(effects,"visuals");
        private static void WithEffects(Action<DuelEffects,Func<int>> test)
        {
            var root=new GameObject("Network test effects");
            int impacts=0;
            var effects=new DuelEffects(root.transform,_=>impacts++);
            try{test(effects,()=>impacts);}
            finally{effects.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }

        private static void SharedEffectTimeline()
        {
            using(var f=new Fixture())WithEffects((effects,impacts)=>
            {
                var spell=Fireball(102,106,110);
                f.Deliver(f.State(100,"welcome"));effects.Update(f.Session);
                f.Deliver(f.State(104,effects:new[]{spell},x:4));effects.Update(f.Session);
                Require(Visuals(effects).Count==0&&impacts()==0,"An effect appeared before its render-time start.");
                f.Deliver(f.State(106,effects:new[]{spell},x:6));effects.Update(f.Session);
                Require(Visuals(effects).Count==1&&impacts()==0,"The fireball impacted before the shared render tick.");
                var position=f.Session.Sample(f.Session.State.units[0]);
                Require(Math.Abs(position.x-2)<.001,"Soldiers did not sample the same delayed render tick.");
                f.Deliver(f.State(110,x:10));effects.Update(f.Session);
                Require(f.Session.State.effects.Length==0&&Visuals(effects).Count==1,"A newer roster removed the spell before its rendered lifetime ended.");
                Require(impacts()==1&&Math.Abs(f.Session.RenderTick-106)<.001,"Impact sound did not follow the SDK render tick.");
                effects.Update(f.Session);
                Require(impacts()==1,"Impact sound repeated on another rendered frame.");
                Advance(f.Session,f.Now+.2);effects.Update(f.Session);
                Require(Visuals(effects).Count==0&&impacts()==1,"An expired effect or its impact survived past the shared timeline.");
            });
        }

        private static void StalledTimelineAndMatchReset()
        {
            using(var f=new Fixture())WithEffects((effects,impacts)=>
            {
                f.Deliver(f.State(100,"welcome",effects:new[]{Fireball(90,102,114)}));
                Advance(f.Session,f.Now+1);effects.Update(f.Session);
                Require(f.Session.RenderTick<=100&&impacts()==0,"A stalled snapshot stream extrapolated a spell impact past authority.");
                f.Error("connection_closed");
                double frozen=f.Session.RenderTick;
                Advance(f.Session,f.Now+.1);effects.Update(f.Session);
                Require(f.Session.RenderTick==frozen&&impacts()==0,"Effects advanced while the session was recovering.");
                f.Deliver(f.State(110,"welcome"));effects.Update(f.Session);
                Require(f.Session.SampleEffects().Count==0&&Visuals(effects).Count==0,"Resume retained pre-welcome effects.");
                f.Deliver(f.State(0,"welcome","new-match"));
                Require(f.Session.RenderTick<0&&f.Session.SampleEffects().Count==0,"Rematch did not reset the timeline to a fresh tick domain.");
                f.Deliver(f.State(10,match:"new-match",effects:new[]{Fireball(0,8,20)}));effects.Update(f.Session);
                var ended=f.State(12,match:"new-match");ended.phase="ended";f.Deliver(ended);effects.Update(f.Session);
                Require(Visuals(effects).Count==0,"Match end left buffered spell visuals active.");
            });
        }
    }
}
