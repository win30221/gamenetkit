using Rumblebound.Gameplay;
using System;
using UnityEngine;

namespace Rumblebound.Presentation
{
    // One small software SFX bus owns every combat voice. The shared gain is
    // applied to the whole pool, including sounds already playing; there are
    // no unbounded PlayOneShot tails or per-event audio objects.
    public sealed class CombatAudio : IDisposable
    {
        public const int VoiceCapacity = 8;
        public const float MasterHeadroom = .2f;
        private const string VolumeKey = "Rumblebound.Audio.SFXVolume";
        private const string MuteKey = "Rumblebound.Audio.Muted";
        private readonly GameObject root;
        private readonly AudioSource[] voices = new AudioSource[VoiceCapacity];
        private readonly double[] busyUntil = new double[VoiceCapacity];
        private readonly float[] eventGain = new float[VoiceCapacity];
        private readonly long[] speculativeIDs=new long[VoiceCapacity];
        private readonly AudioClip shot, wall, player, swing;
        private uint variation;
        private bool disposed;

        public float Volume { get; private set; }
        public bool Muted { get; private set; }
        public bool Available => !disposed && shot != null && wall != null && player != null && swing != null;
        public int VoiceCount => disposed ? 0 : voices.Length;
        public int PlayedCount { get; private set; }

        public CombatAudio(Transform parent)
        {
            Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, .8f));
            Muted = PlayerPrefs.GetInt(MuteKey, 0) != 0;
            shot = Resources.Load<AudioClip>("CombatAudio/shot");
            wall = Resources.Load<AudioClip>("CombatAudio/wall");
            player = Resources.Load<AudioClip>("CombatAudio/player");
            swing = Resources.Load<AudioClip>("CombatAudio/swing");
            root = new GameObject("Combat SFX bus");
            root.transform.SetParent(parent, false);
            for (int i = 0; i < voices.Length; i++)
            {
                var voice = new GameObject("SFX voice " + i);
                voice.transform.SetParent(root.transform, false);
                var source = voice.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0; // Explicit stereo panning for the fixed arena view.
                source.dopplerLevel = 0;
                source.reverbZoneMix = 0;
                source.bypassEffects = true;
                source.bypassListenerEffects = true;
                source.bypassReverbZones = true;
                source.priority = 128;
                eventGain[i] = 1;
                voices[i] = source;
            }
            ApplyBus();
        }

        public static float BusGain(float normalized, bool muted)
        {
            float value = Mathf.Clamp01(normalized);
            // Squared amplitude spreads perceived loudness over the slider.
            return muted ? 0 : MasterHeadroom * value * value;
        }

        public void SetVolume(float normalized)
        {
            if (disposed) return;
            Volume = Mathf.Clamp01(normalized);
            PlayerPrefs.SetFloat(VolumeKey, Volume);
            PlayerPrefs.Save();
            ApplyBus();
            if (Volume <= 0) Clear();
        }

        public void SetMuted(bool muted)
        {
            if (disposed) return;
            Muted = muted;
            PlayerPrefs.SetInt(MuteKey, muted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyBus();
            if (muted) Clear();
        }

        private void ApplyBus()
        {
            float gain = BusGain(Volume, Muted);
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i].mute = Muted || Volume <= 0;
                voices[i].volume = gain * eventGain[i];
            }
        }

        public void Play(string kind, Vector3 position, bool own, double now,long speculativeID=0)
        {
            if (!Available || Muted || Volume <= 0 || double.IsNaN(now) || double.IsInfinity(now)) return;
            AudioClip clip;
            float kindGain;
            switch (kind)
            {
                case "shot": clip = shot; kindGain = .8f; break;
                case "melee_swing": clip = swing; kindGain = .7f; break;
                case "wall": case "boundary": clip = wall; kindGain = .65f; break;
                case "hit": case "player": clip = player; kindGain = 1; break;
                default: return; // Expiry is silent, never a false impact sound.
            }
            int selected = -1;
            for (int i = 0; i < voices.Length; i++)
                if (busyUntil[i] <= now) { selected = i; break; }
            // Saturation drops low-priority extra events instead of producing
            // an unbounded mixture or stealing a still-audible hit halfway.
            if (selected < 0) return;
            variation++;
            float pitch = .97f + (variation % 7) * .01f;
            var source = voices[selected];
            source.Stop();
            source.clip = clip;
            source.pitch = pitch;
            source.panStereo = Mathf.Clamp(position.x / 10f, -1, 1) * .55f;
            eventGain[selected] = kindGain * (own ? 1 : .75f);
            speculativeIDs[selected]=speculativeID;
            source.volume = BusGain(Volume, Muted) * eventGain[selected];
            source.mute = false;
            busyUntil[selected] = now + clip.length / pitch;
            source.Play();
            PlayedCount++;
        }

        public void Clear()
        {
            if (disposed) return;
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i].Stop();
                voices[i].clip = null;
                busyUntil[i] = 0;
            }
        }

        public void ResolveSpeculative(long id,bool accepted)
        {
            if(disposed||id<=0)return;
            for(int i=0;i<voices.Length;i++)if(speculativeIDs[i]==id){speculativeIDs[i]=0;if(!accepted){voices[i].Stop();voices[i].clip=null;busyUntil[i]=0;}}
        }
        public void Dispose()
        {
            if (disposed) return;
            Clear(); disposed = true;
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(root); return; }
#endif
            UnityEngine.Object.Destroy(root);
        }
    }
}
