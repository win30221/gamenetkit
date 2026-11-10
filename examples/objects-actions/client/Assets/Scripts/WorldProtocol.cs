using System;

namespace Realtime.Examples.ObjectsActions
{
    [Serializable] public sealed class ServerMessage
    {
        public string type;
        public int protocol;
        public long playerID;
        public long tick;
        public int tickRate;
        public int snapshotRate;
        public PlayerState[] players;
        public string code;
        public uint generation;
        public WorldObject[] objects;
        public long id;
        public string kind;
        public bool accepted;
        public string reason;
        public string objectID;
        public float x, z;
    }

    [Serializable] public sealed class WorldObject
    {
        public string id;
        public long owner;
        public uint life;
        public float x, z;
    }

    [Serializable] public sealed class PlayerState
    {
        public long id;
        public float x;
        public float z;
        public long ack;
        public long inputTick;
    }

    [Serializable] public sealed class InputMessage
    {
        public string type = "input";
        public long seq;
        public float x;
        public float z;
    }

    public static class ExampleEndpoint
    {
        public const string Default = "ws://127.0.0.1:18129/ws";

        public static string Resolve(string[] arguments, string pageURL)
        {
            for (int i = 0; i < arguments.Length; i++)
                if (arguments[i] == "-rf-server")
                {
                    if (i + 1 == arguments.Length) throw new ArgumentException("-rf-server requires a URL");
                    return Normalize(arguments[i + 1]);
                }
            if (!string.IsNullOrEmpty(pageURL)) return Normalize(new Uri(pageURL).GetLeftPart(UriPartial.Authority));
            return Default;
        }

        public static string Normalize(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) throw new ArgumentException("Invalid server URL");
            var builder = new UriBuilder(uri);
            switch (uri.Scheme)
            {
                case "http": builder.Scheme = "ws"; break;
                case "https": builder.Scheme = "wss"; break;
                case "ws": case "wss": break;
                default: throw new ArgumentException("Server URL must use http, https, ws or wss");
            }
            if (uri.IsDefaultPort) builder.Port = -1;
            if (builder.Path == "/" || string.IsNullOrEmpty(builder.Path)) builder.Path = "/ws";
            builder.Fragment = "";
            return builder.Uri.AbsoluteUri;
        }
    }
}
