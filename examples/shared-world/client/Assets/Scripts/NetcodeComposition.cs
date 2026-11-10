using System;
using Realtime.Core;
using Realtime.Transport;

namespace Realtime.Examples.SharedWorld
{
    // One instance per session. The scene bootstrap will own and dispose this
    // composition; application DTOs, world state and views do not belong here.
    public sealed class NetcodeComposition : IDisposable
    {
        public MessageTransport Transport { get; } = new MessageTransport();
        public IPresentationTimeline Timeline { get; } = new LatestSnapshotTimeline();

        public void Dispose() => Transport.Dispose();
    }
}
