using System;
using System.Threading.Tasks;
using Google.Protobuf;
using Realtime.Transport;

namespace Realtime.Protobuf
{
    // One application owner calls Open/TrySendAsync/Pump. Transport callbacks
    // only enqueue bytes; handlers run synchronously on the Pump caller.
    // Generated messages are mutable: do not mutate them during serialization.
    // This adapter borrows the transport; its owner closes/disposes it.
    public sealed class ProtobufSession<TSend, TReceive>
        where TSend : class, IMessage<TSend>
        where TReceive : class, IMessage<TReceive>
    {
        private readonly MessageTransport transport;
        private readonly MessageParser<TReceive> parser;
        private readonly Func<TReceive, bool> accepts;
        private int epoch = -1;
        public event Action<TReceive> Received;
        public event Action<string> Failed;
        public double LastReceivedMS { get; private set; }

        // accepts is application policy (for example rejecting unknown oneof
        // bodies). Protobuf decoding alone cannot validate game semantics.
        public ProtobufSession(MessageTransport transport, MessageParser<TReceive> parser,
            Func<TReceive, bool> accepts = null)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
            this.accepts = accepts;
        }

        public void Open(int attempt, string url, TSend hello)
        {
            var bytes = Encode(hello);
            transport.OpenBinary(attempt, url, bytes);
        }

        // true is local submission/write acceptance, never peer delivery or ACK.
        // No unbounded pending-send queue and no automatic gameplay retries.
        public Task<bool> TrySendAsync(TSend message) => transport.TrySendBytes(Encode(message));

        private static byte[] Encode(TSend message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.CalculateSize() > MessageTransport.MaxMessageBytes)
                throw new ArgumentOutOfRangeException(nameof(message), "Protobuf message exceeds transport limit");
            return message.ToByteArray();
        }

        public int Pump(int budget = MessageTransport.MaxPendingMessages)
        {
            if (budget < 1 || budget > MessageTransport.MaxPendingMessages)
                throw new ArgumentOutOfRangeException(nameof(budget));
            if (epoch != transport.Epoch) { epoch = transport.Epoch; LastReceivedMS = 0; }
            int count = 0;
            while (count < budget && transport.TryReadPacket(out var packet))
            {
                count++;
                if (packet.ErrorCode != null)
                {
                    Failed?.Invoke(packet.ErrorCode);
                    break;
                }
                TReceive message;
                try { message = parser.ParseFrom(packet.Data); }
                catch (InvalidProtocolBufferException)
                {
                    Reject("protobuf_decode_failed");
                    break;
                }
                if (accepts != null && !accepts(message))
                {
                    Reject("unsupported_message");
                    break;
                }
                LastReceivedMS = packet.ReceivedMS;
                int deliveringEpoch = transport.Epoch;
                Received?.Invoke(message);
                // A handler may replace/close the connection. Never deliver
                // data from a replacement attempt in this same Pump call.
                if (transport.Epoch != deliveringEpoch) break;
            }
            return count;
        }

        private void Reject(string code)
        {
            transport.Close();
            Failed?.Invoke(code);
        }
    }
}
