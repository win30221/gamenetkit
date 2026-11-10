using System;
using Realtime.Transport;

namespace Realtime.Examples.InterpolationLesson
{
    // One instance per session. The scene bootstrap will own and dispose this
    // composition; application DTOs, world state and views do not belong here.
    public sealed class NetcodeComposition : IDisposable
    {
        public MessageTransport Transport { get; } = new MessageTransport();

        public void Dispose() => Transport.Dispose();
    }
}
