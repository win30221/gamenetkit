using System;

namespace Realtime.Examples.SharedWorld
{
    // Connection and input lifetime. Serialization is supplied by the Unity adapter.
    public sealed class ExampleSession : IDisposable
    {
        private readonly NetcodeComposition netcode;
        private readonly Func<string, ServerMessage> decode;
        private readonly Func<InputMessage, string> encode;
        private long sequence;
        private double nextInput;
        private double connectDeadline;
        private float lastX, lastZ;
        public ReplicaState State { get; } = new ReplicaState();
        public string Status { get; private set; } = "Disconnected";
        public bool Connecting { get; private set; }
        public bool Connected => State.Joined;

        public ExampleSession(NetcodeComposition netcode, Func<string, ServerMessage> decode, Func<InputMessage, string> encode)
        { this.netcode = netcode; this.decode = decode; this.encode = encode; }

        public void Connect(string endpoint, string callbackTarget, double now)
        {
            Disconnect("Connecting");
            int attempt = netcode.Transport.BeginAttempt();
            sequence = 0; nextInput = now; lastX = lastZ = 0;
            Connecting = true; connectDeadline = now + 10;
            try { netcode.Transport.Open(attempt, endpoint, "{\"type\":\"hello\",\"protocol\":1}", "", callbackTarget, false); }
            catch (Exception) { Disconnect("Connection failed"); }
        }

        public void Pump(double now, float x, float z)
        {
            while (netcode.Transport.TryRead(out var incoming))
            {
                try
                {
                    var message = decode(incoming.JSON);
                    if (message == null) throw new InvalidOperationException();
                    if (message.type == "error") { Disconnect("Disconnected: " + message.code); break; }
                    if (State.Apply(message))
                    {
                        if (message.type == "welcome") netcode.Timeline.Reset(message.tick, now);
                        else netcode.Timeline.Observe(message.tick, now);
                        Connecting = false; Status = "Connected";
                    }
                }
                catch (Exception) { Disconnect("Disconnected: invalid server message"); break; }
            }
            if (Connecting && now > connectDeadline) Disconnect("Disconnected: welcome timeout");
            if (!Connected) return;
            netcode.Timeline.Advance(now);
            bool changed = x != lastX || z != lastZ;
            if (now >= nextInput || changed) SendInput(now, x, z);
        }

        public void StopInput(double now) { if (Connected) SendInput(now, 0, 0); }

        private void SendInput(double now, float x, float z)
        {
            if (netcode.Transport.Sending) return;
            lastX = x; lastZ = z; nextInput = now + 1.0 / 30.0;
            _ = netcode.Transport.Send(encode(new InputMessage { seq = ++sequence, x = x, z = z }));
        }

        public void Disconnect(string status = "Disconnected")
        { netcode.Transport.Close(); State.Clear(); netcode.Timeline.Reset(-1, 0); Connecting = false; Status = status; }

        public void ReceiveBrowserMessage(string json) => netcode.Transport.Receive(netcode.Transport.Epoch, json);
        public void Dispose() { Disconnect(); netcode.Dispose(); }
    }
}
