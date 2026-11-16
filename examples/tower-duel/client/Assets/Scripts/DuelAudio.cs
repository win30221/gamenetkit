using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace TowerDuel
{
    // Original synthesized cues, with bounded voice count and separate UI/SFX routing.
    public sealed class DuelAudio : IDisposable
    {
        private readonly GameObject root;
        private readonly AudioSource ui;
        private readonly AudioSource[] voices=new AudioSource[6];
        private readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        private readonly Dictionary<long,int> health=new Dictionary<long,int>();
        private string match,phase;
        private int voice;
        private double nextHit;
        public bool Muted {get;private set;}
        public DuelAudio(Transform parent)
        {
            root=new GameObject("Duel audio");root.transform.SetParent(parent,false);
            var mixer=Resources.Load<AudioMixer>("TowerDuel/Audio/DuelMixer");
            if(!mixer)throw new InvalidOperationException("Missing Duel UI/SFX mixer; prepare game content before building");
            AudioSource Source(string bus)
            {
                var source=root.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;
                if(mixer){var groups=mixer.FindMatchingGroups(bus);if(groups.Length>0)source.outputAudioMixerGroup=groups[0];}
                return source;
            }
            ui=Source("UI");for(int i=0;i<voices.Length;i++)voices[i]=Source("SFX");
            Muted=PlayerPrefs.GetInt("TowerDuel.AudioMuted",0)!=0;
            clips["select"]=Tone("Select",620,.07f,false);
            clips["deploy"]=Tone("Deploy",350,.13f,false);
            clips["reject"]=Tone("Reject",140,.16f,false);
            clips["hit"]=Tone("Hit",90,.10f,true);
            clips["death"]=Tone("Death",160,.20f,true);
            clips["fireball"]=Tone("Fireball",65,.35f,true);
            clips["frost"]=Tone("Frost",1000,.32f,false);
            clips["countdown"]=Tone("Countdown",480,.18f,false);
            clips["win"]=Tone("Victory",740,.45f,false);
            clips["lose"]=Tone("Defeat",200,.40f,false);
        }
        private static AudioClip Tone(string name,float frequency,float duration,bool noise)
        {
            const int rate=22050;var samples=new float[Mathf.CeilToInt(duration*rate)];uint random=1729;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)rate,progress=t/duration;
                random=random*1664525+1013904223;
                float n=((random>>8)/16777215f)*2-1;
                float wave=Mathf.Sin(2*Mathf.PI*frequency*t*(1-.25f*progress));
                samples[i]=(noise?.65f*n+.35f*wave:wave)*Mathf.Min(1,t/.008f)*Mathf.Pow(1-progress,2)*.4f;
            }
            var clip=AudioClip.Create(name,samples.Length,1,rate,false);clip.SetData(samples,0);return clip;
        }
        public void Toggle(){Muted=!Muted;PlayerPrefs.SetInt("TowerDuel.AudioMuted",Muted?1:0);PlayerPrefs.Save();if(Muted){ui.Stop();foreach(var source in voices)source.Stop();}}
        public void Play(string cue,bool interfaceCue=false)
        {
            if(Muted||!clips.TryGetValue(cue,out var clip))return;
            var source=interfaceCue?ui:voices[voice++%voices.Length];source.clip=clip;source.volume=Mathf.Pow(10,(interfaceCue?-18f:-14f)/20f);source.pitch=1+(voice%3-1)*.025f;source.Play();
        }
        public void Observe(DuelMessage state)
        {
            if(state==null){health.Clear();match=phase=null;return;}
            if(state.match!=match){health.Clear();match=state.match;phase=null;}
            if(state.phase!=phase){phase=state.phase;if(phase=="countdown")Play("countdown",true);else if(phase=="ended")Play(state.winner==state.seat?"win":"lose",true);}
            bool hit=false;var seen=new HashSet<long>();
            foreach(var group in new[]{state.units,state.towers})foreach(var unit in group){seen.Add(unit.id);if(unit.concealed){health.Remove(unit.id);continue;}if(health.TryGetValue(unit.id,out int hp)&&unit.hp<hp)hit=true;health[unit.id]=unit.hp;}
            var removed=new List<long>();foreach(long id in health.Keys)if(!seen.Contains(id))removed.Add(id);
            foreach(long id in removed)health.Remove(id);
            if(DuelSession.Now>=nextHit&&(hit||removed.Count>0)){Play(removed.Count>0?"death":"hit");nextHit=DuelSession.Now+.12;}
        }
        public void Dispose(){foreach(var clip in clips.Values)UnityEngine.Object.Destroy(clip);UnityEngine.Object.Destroy(root);}
    }
}
