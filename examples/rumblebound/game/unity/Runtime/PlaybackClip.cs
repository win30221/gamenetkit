using System;
namespace Rumblebound.Gameplay
{
    [Serializable] public sealed class PlaybackFrame { public long tick; public string payload; }
    [Serializable] public sealed class PlaybackClip
    {
        public int version,tickRate,protocol;
        public string model,matchID,content,mapHash,config,sourceDigest;
        public PlaybackFrame[] frames;
    }
}
