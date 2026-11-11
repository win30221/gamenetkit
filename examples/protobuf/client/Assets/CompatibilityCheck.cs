using System;
using Google.Protobuf;
using GameNetKit.ProtobufExample;

public static class CompatibilityCheck
{
    // Runs in .NET, Editor and actual IL2CPP/WebGL Player; does not use reflection
    // to instantiate messages, so all required parsers are statically reachable.
    public static void Run()
    {
        var value = new CompatibilityProbe { OptionalValue = 0, Number = ulong.MaxValue };
        value.Values.Add(9007199254740993UL);
        value.Values.Add(ulong.MaxValue);
        var decoded = CompatibilityProbe.Parser.ParseFrom(value.ToByteArray());
        if (!decoded.HasOptionalValue || decoded.OptionalValue != 0 || decoded.Number != ulong.MaxValue ||
            decoded.BodyCase != CompatibilityProbe.BodyOneofCase.Number || decoded.Values.Count != 2 ||
            decoded.Values[0] != 9007199254740993UL || decoded.Values[1] != ulong.MaxValue)
            throw new Exception("Protobuf numeric/presence compatibility failed");
        decoded.ClearOptionalValue();
        decoded.Text = "Go + Unity：你好";
        decoded = CompatibilityProbe.Parser.ParseFrom(decoded.ToByteArray());
        if (decoded.HasOptionalValue || decoded.BodyCase != CompatibilityProbe.BodyOneofCase.Text ||
            decoded.Text != "Go + Unity：你好") throw new Exception("Protobuf oneof compatibility failed");
        // Unknown future fields survive decode/encode; field 99 varint value 1.
        byte[] bytes = decoded.ToByteArray();
        var extended = new byte[bytes.Length + 3];
        Array.Copy(bytes, extended, bytes.Length);
        extended[bytes.Length] = 0x98; extended[bytes.Length + 1] = 0x06; extended[bytes.Length + 2] = 1;
        if (CompatibilityProbe.Parser.ParseFrom(extended).CalculateSize() != extended.Length)
            throw new Exception("Protobuf unknown field preservation failed");
    }
}
