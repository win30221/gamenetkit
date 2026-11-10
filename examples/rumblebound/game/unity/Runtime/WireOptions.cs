using System;
namespace Rumblebound.Gameplay
{
    [Serializable] public sealed class ServerNetOptions
    {
        public string codec="json",interest="all",lag="off";
        public bool quantize,history;
        public double radius=12;
    }
    [Serializable] public sealed class TechState
    {
        public bool rewindUsed;
        public ServerNetOptions options;
        public int historySamples,visible,total;
        public long rewindTick;
    }
    [Serializable] public sealed class FrameEnvelope
    {
        public string type,content,matchID,data;
        public int protocol;
        public uint generation;
        public long tick,id,@base;
    }
    [Serializable] public sealed class HelloMessage
    {
        public string type="hello",content,matchID,ticket;
        public int protocol;
        public ServerNetOptions net;
    }
}
