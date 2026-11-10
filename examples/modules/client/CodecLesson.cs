using System;
using System.IO;
using System.Text.Json;
using Realtime.Core;
using Realtime.Codec;

namespace Realtime.Examples.Modules
{
    public static class CodecLesson
    {
        public static void Main(string[] args)
        {
            // Reuse actual Go-generated fixtures; do not implement a second encoder.
            using var fixtures = JsonDocument.Parse(File.ReadAllText(args[0]));
            var cases = fixtures.RootElement.GetProperty("cases");
            var decoder = new FrameDecoder();
            for (int i=0; i<2; i++) {
                var frame = cases[i].GetProperty("frame");
                var result = decoder.Decode(frame.GetProperty("id").GetInt64(), frame.GetProperty("base").GetInt64(), frame.GetProperty("data").GetString(), out var payload);
                Check.That(result==FrameDecodeResult.Applied && payload==cases[i].GetProperty("expected").GetString(), "Go/C# full or delta mismatch");
            }
            Check.That(decoder.Ack==2, "ack only decoded data");
            decoder.Reset();
            var patch = cases[1].GetProperty("frame");
            Check.That(decoder.Decode(2, 1, patch.GetProperty("data").GetString(), out _)==FrameDecodeResult.MissingBaseline && decoder.Ack==0, "missing baseline requests full");
            Console.WriteLine("codec: raw JSON -> decoded full frame 1 + delta frame 2; reconnect clears baseline, missing delta base -> ack=0");
        }
    }
}
