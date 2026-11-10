using System;
using Realtime.Core;
using Realtime.Simulation;

namespace Realtime.Examples.InterpolationLesson
{
    // Connection and input lifetime. Serialization is supplied by the Unity adapter.
    public sealed class ExampleSession : IDisposable
    {
        private readonly NetcodeComposition netcode;
        private readonly Func<string, ServerMessage> decode;
        private readonly Func<InputMessage, string> encode;
        private MessageConditioner network;
        public InterpolatedWorld View { get; } = new InterpolatedWorld();
        public ConditioningSummary Network => network.Summary;
        private long sequence;
        private double nextInput;
        private double connectDeadline;
        private float lastX, lastZ;
        public ReplicaState State { get; } = new ReplicaState();
        public string Status { get; private set; } = "Disconnected";
        public bool Connecting { get; private set; }
        public bool Connected => State.Joined;

        public ExampleSession(NetcodeComposition netcode, Func<string, ServerMessage> decode, Func<InputMessage, string> encode)
        {
            this.netcode = netcode; this.decode = decode; this.encode = encode;
            ConfigureNetwork(0, 0, 0);
        }

        // Only snapshot presentation is conditioned. Control and input stay immediate.
        public void ConfigureNetwork(double delay, double jitter, double loss)
        {
            network = new MessageConditioner(new ConditioningSettings {DelaySeconds=delay, JitterSeconds=jitter, LossProbability=loss, Seed=17});
        }

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
                        if (message.type == "welcome") {
                            network.ClearPending(); View.Accept(message, now);
                        } else if (!network.Enqueue(MessageDirection.Receive, incoming.JSON, true, now)) {
                            throw new InvalidOperationException("Snapshot queue full");
                        }
                        Connecting = false; Status = "Connected";
                    }
                }
                catch (Exception) { Disconnect("Disconnected: invalid server message"); break; }
            }
            if (Connecting && now > connectDeadline) Disconnect("Disconnected: welcome timeout");
            if (!Connected) return;
            while (network.TryTake(MessageDirection.Receive, now, out var snapshot)) View.Accept(decode(snapshot), now);
            View.Advance(now);
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
        { netcode.Transport.Close(); State.Clear(); network.ClearPending(); View.Clear(); Connecting = false; Status = status; }

        public void ReceiveBrowserMessage(string json) => netcode.Transport.Receive(netcode.Transport.Epoch, json);
        public void Dispose() { Disconnect(); netcode.Dispose(); }
    }
}
