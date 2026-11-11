# Typed Protobuf sessions

Optional adapter for official generated `Google.Protobuf` messages over GameNetKit binary WebSocket. It owns no gameplay protocol, message registry, world, retries or Unity objects.

Install the local `com.realtime.transport`, `com.realtime.protobuf-runtime`, and `com.realtime.protobuf` packages. Your assembly references `Realtime.Transport`, `Realtime.Protobuf`, your generated assembly and `Google.Protobuf.dll`. See the complete [example](../../../examples/protobuf/README.md) and its package manifest.

```csharp
var transport = new MessageTransport();
var session = new ProtobufSession<ClientPacket, ServerPacket>(
    transport, ServerPacket.Parser,
    packet => packet.BodyCase != ServerPacket.BodyOneofCase.None);
session.Received += HandleServerPacket;
session.Failed += HandleFailure;
session.Open(transport.BeginAttempt(), url, helloPacket);
// Wait for the application welcome; sending while opening can return false.
bool accepted = await session.TrySendAsync(inputPacket);
// Each Update, on the application's single owner:
session.Pump();
// When leaving:
transport.Dispose();
```

The optional predicate validates envelope policy (for example rejecting unknown oneof bodies). Successful protobuf parsing does not validate movement ranges, authorization, sequence or protocol versions. Omit the predicate to handle all decoded envelopes yourself. Unknown additive fields are preserved by the official runtime. Do not use serialized bytes as a canonical cross-language state hash.

`Pump` delivers at most its budget (default64, valid1..64), on the caller thread. Data preceding a normal close is delivered before its typed failure; parse/predicate rejection closes the connection and calls `Failed`. Exceptions from your handlers/predicate propagate to the application. A handler replacing the connection ends the current Pump. `LastReceivedMS` is the transport's recorded arrival time, reset on observing a new epoch.

`TrySendAsync` serializes immediately, rejects null/over32KiB input, then submits owned bytes to transport. false means not ready, busy or failed; no implicit retry queue. Native true means local write completed; WebGL true means the browser accepted the send. Neither means application ACK. Do not modify generated messages while serializing; received messages are owned by the caller, and history consumers must clone as needed.

The session borrows its transport; the caller owns Close/Dispose. Binary sessions use `OpenBinary` and `TryReadPacket`; text/binary read/send API mixing is rejected. V1 supports native/WebGL WebSocket only. Existing string conditioner/delta decoder and WebTransport are not binary-compatible adapters yet. Original JSON/text APIs remain available.

Verification: `./tests/scripts/Test-Protobuf.ps1`; platform evidence is recorded in [validation](../../../docs/PROTOBUF_VALIDATION.md).
