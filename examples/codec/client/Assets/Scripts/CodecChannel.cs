using System;
using System.Collections.Generic;
using System.Text;
using Realtime.Codec;
using Realtime.Core;

namespace Realtime.Examples.CodecLesson
{
    public sealed class FrameReceipt
    {
        public string Kind;
        public int WireBytes,JSONBytes;
        public long ID,Baseline;
    }
    public sealed class CodecTraffic
    {
        private sealed class Sample{public double Time;public int Bytes;}
        private readonly Queue<Sample> window=new Queue<Sample>();
        public long Bytes{get;private set;}
        public long JSONFrames{get;private set;}
        public long Keyframes{get;private set;}
        public long Deltas{get;private set;}
        public long Missing{get;internal set;}
        public long Recoveries{get;internal set;}
        public long Stale{get;internal set;}
        public FrameReceipt Latest{get;private set;}=new FrameReceipt{Kind="none"};
        public void Packet(int bytes,double now){Bytes+=bytes;window.Enqueue(new Sample{Time=now,Bytes=bytes});while(window.Count>64)window.Dequeue();}
        public double PerSecond(double now){while(window.Count>0&&now-window.Peek().Time>2)window.Dequeue();int sum=0;foreach(var sample in window)sum+=sample.Bytes;return sum/2.0;}
        public void Delivered(FrameReceipt receipt){Latest=receipt;if(receipt.Kind=="json")JSONFrames++;else if(receipt.Kind=="keyframe")Keyframes++;else Deltas++;}
        public void Clear(){window.Clear();Bytes=JSONFrames=Keyframes=Deltas=Missing=Recoveries=Stale=0;Latest=new FrameReceipt{Kind="none"};}
    }

    // Decoding, validation and ack form one boundary. The rendered ReplicaState
    // is independent and may receive the validated payload later via simulation.
    public sealed class CodecChannel
    {
        private readonly FrameDecoder decoder=new FrameDecoder();
        private readonly ReplicaState received=new ReplicaState();
        private readonly Func<string,ServerMessage> parse;
        private long highestFrame;
        private bool ackReady;
        public uint Stream{get;private set;}
        public long Ack=>decoder.Ack;
        public bool Recovering{get;private set;}
        public CodecTraffic Metrics{get;}=new CodecTraffic();
        public CodecChannel(Func<string,ServerMessage> parse){this.parse=parse??throw new ArgumentNullException(nameof(parse));}
        public bool Receive(ServerMessage outer,string wire,double now,out ServerMessage model,out string payload,out FrameReceipt receipt)
        {
            model=outer;payload=wire;receipt=null;
            bool encoded=outer.type=="frame";
            if(outer.type!="welcome"&&outer.type!="snapshot"&&!encoded)throw new InvalidOperationException("Unexpected codec envelope");
            if(outer.type!="welcome")Metrics.Packet(Encoding.UTF8.GetByteCount(wire),now);
            uint stream=encoded?outer.stream:outer.codec?.stream??0;
            if(stream==0 || outer.protocol!=1)throw new InvalidOperationException("Invalid codec stream");
            if(stream<Stream){Metrics.Stale++;return false;}
            if(stream>Stream){Stream=stream;decoder.Reset();highestFrame=0;ackReady=false;Recovering=false;}
            if(encoded){
                if(outer.id<=0 || outer.tick<0)throw new InvalidOperationException("Invalid frame identity");
                if(outer.id<=highestFrame){Metrics.Stale++;return false;}
                var result=decoder.Decode(outer.id,outer.@base,outer.data,out payload);
                if(result==FrameDecodeResult.MissingBaseline){Metrics.Missing++;Recovering=true;ackReady=true;return false;}
                if(result==FrameDecodeResult.Stale){Metrics.Stale++;return false;}
                if(result!=FrameDecodeResult.Applied)throw new InvalidOperationException("Invalid encoded frame");
                model=parse(payload);
                if(model==null || model.type!="snapshot" || model.tick!=outer.tick || model.codec==null || model.codec.stream!=stream || model.codec.mode!="delta")throw new InvalidOperationException("Mismatched encoded payload");
            }else if(outer.type!="welcome"&&outer.codec.mode!="json")throw new InvalidOperationException("Encoded stream requires frame envelope");
            if(!received.Apply(model)){Metrics.Stale++;return false;}
            if(encoded){highestFrame=outer.id;ackReady=true;if(Recovering&&outer.@base==0){Recovering=false;Metrics.Recoveries++;}}
            receipt=new FrameReceipt{Kind=encoded?(outer.@base==0?"keyframe":"delta"):"json",WireBytes=Encoding.UTF8.GetByteCount(wire),JSONBytes=Encoding.UTF8.GetByteCount(payload),ID=encoded?outer.id:0,Baseline=encoded?outer.@base:0};
            return true;
        }
        public bool TakeAck(out uint stream,out long id){stream=Stream;id=decoder.Ack;if(!ackReady)return false;ackReady=false;return true;}
        public void ForgetBaseline(){if(received.Codec?.mode=="delta"){decoder.Reset();ackReady=false;}}
        public void Clear(){decoder.Reset();received.Clear();highestFrame=0;Stream=0;ackReady=false;Recovering=false;Metrics.Clear();}
    }
}
