using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed partial class BattlePresentation
    {
        private sealed class FireBurst {public GameObject root;public ParticleSystem[] particles;public double until;}
        private readonly List<FireBurst> fireBursts=new List<FireBurst>();
        private readonly Stack<FireBurst> freeFireBursts=new Stack<FireBurst>();
        public float VisualSpeed=>current?.frozen==true?0:client?.Flow?.Playback.Active==true?(client.Flow.Playback.Clock.Playing?(float)client.Flow.Playback.Clock.Speed:0):1;
        public int FlameArrowCount=>GetComponentsInChildren<RangerFlameArrow>().Count(arrow=>arrow.ParticleCount>0);
        private GameObject CreateArrow(){var arrow=Instantiate(Resources.Load<GameObject>("KayKit/Arrow"),transform);arrow.AddComponent<RangerFlameArrow>();return arrow;}
        private void FireImpact(ProjectileEnd end){
            if(current?.players.FirstOrDefault(p=>p.state.playerID==end.ownerID)?.state.heroID!="ranger")return;
            if(fireBursts.Count>=8)ReleaseFire(0);
            FireBurst burst;
            if(freeFireBursts.Count>0)burst=freeFireBursts.Pop();else{
                var prefab=Resources.Load<GameObject>("Mage/GroundImpact");if(prefab==null)return;
                var root=Instantiate(prefab,transform);root.name="Ranger fire impact";root.transform.localScale=prefab.transform.localScale*.22f;burst=new FireBurst{root=root,particles=root.GetComponentsInChildren<ParticleSystem>(true)};
            }
            burst.root.transform.position=new Vector3((float)end.x,.35f,(float)end.z);burst.root.SetActive(true);burst.until=now+.45;
            foreach(var ps in burst.particles){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.Play(true);}fireBursts.Add(burst);
        }
        private void ReleaseFire(int index){var burst=fireBursts[index];foreach(var ps in burst.particles)ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);burst.root.SetActive(false);fireBursts.RemoveAt(index);freeFireBursts.Push(burst);}
        private void RenderFire(){for(int i=fireBursts.Count-1;i>=0;i--){if(now>=fireBursts[i].until){ReleaseFire(i);continue;}foreach(var ps in fireBursts[i].particles){var main=ps.main;main.simulationSpeed=Mathf.Max(.01f,VisualSpeed);if(VisualSpeed==0)ps.Pause(true);else if(ps.isPaused)ps.Play(true);}}}
        private void ClearFire(){while(fireBursts.Count>0)ReleaseFire(0);}
        private void DisposeFire(){ClearFire();while(freeFireBursts.Count>0)Destroy(freeFireBursts.Pop().root);}
    }
}
