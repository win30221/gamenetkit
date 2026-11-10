using UnityEngine;

namespace Rumblebound.Example.Battle.Unity
{
    // Visual-only, owned and reused by the existing projectile pool. The first
    // late frame resets the trail after the pool has positioned the new arrow.
    public sealed class RangerFlameArrow:MonoBehaviour
    {
        private ParticleSystem[] flames;
        private TrailRenderer trail;
        private Material trailMaterial;
        private BattlePresentation owner;
        private bool reset;
        public int ParticleCount{get{int count=0;if(flames!=null)foreach(var ps in flames)count+=ps.particleCount;return count;}}
        private void Awake(){
            owner=GetComponentInParent<BattlePresentation>();
            var prefab=Resources.Load<GameObject>("Mage/FireballDrop");
            if(prefab!=null){var fire=Instantiate(prefab,transform);fire.name="Ranger flame";fire.transform.localPosition=Vector3.zero;fire.transform.localRotation=Quaternion.identity;fire.transform.localScale=prefab.transform.localScale*(.4f/Mathf.Max(.001f,transform.lossyScale.x));}
            flames=GetComponentsInChildren<ParticleSystem>(true);
            foreach(var ps in flames){var main=ps.main;main.playOnAwake=false;main.scalingMode=ParticleSystemScalingMode.Hierarchy;main.maxParticles=Mathf.Min(main.maxParticles,32);main.startLifetime=new ParticleSystem.MinMaxCurve(.12f,.25f);main.simulationSpace=ParticleSystemSimulationSpace.World;ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
            trail=gameObject.AddComponent<TrailRenderer>();trail.time=.18f;trail.minVertexDistance=.04f;trail.widthMultiplier=.16f;trail.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;trail.receiveShadows=false;
            trailMaterial=new Material(Resources.Load<Material>("Mage/TargetCircle"));trailMaterial.color=new Color(1,.65f,.1f);trail.sharedMaterial=trailMaterial;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(1,.95f,.45f),0),new GradientColorKey(new Color(1,.2f,.02f),1)},new[]{new GradientAlphaKey(.95f,0),new GradientAlphaKey(0,1)});trail.colorGradient=gradient;
        }
        private void OnEnable(){reset=true;}
        private void LateUpdate(){
            float speed=owner!=null?owner.VisualSpeed:1;
            if(reset){trail.Clear();foreach(var ps in flames){ps.Clear(true);ps.Play(true);}reset=false;}
            trail.emitting=speed>0;
            foreach(var ps in flames){var main=ps.main;main.simulationSpeed=Mathf.Max(.01f,speed);if(speed==0){if(!ps.isPaused)ps.Pause(true);}else if(ps.isPaused)ps.Play(true);}
        }
        private void OnDisable(){if(trail!=null){trail.emitting=false;trail.Clear();}if(flames!=null)foreach(var ps in flames)ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
        private void OnDestroy(){if(trailMaterial!=null)Destroy(trailMaterial);}
    }
}
