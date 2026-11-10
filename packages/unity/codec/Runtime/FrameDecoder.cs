using System;
using System.Collections.Generic;
using System.Text;
using Realtime.Core;

namespace Realtime.Codec
{
    public sealed class FrameDecoder : IFrameDecoder
    {
        private const int MaxBytes=16384;
        private readonly Dictionary<long,byte[]> frames=new Dictionary<long,byte[]>();
        private readonly Queue<long> order=new Queue<long>();
        private readonly UTF8Encoding utf8=new UTF8Encoding(false,true);
        private byte[] scratch;
        public long Ack {get;private set;}
        private long highest;
        public void Reset(){frames.Clear();order.Clear();Ack=highest=0;}
        public FrameDecodeResult Decode(long id,long baseline,string data,out string payload)
        {
            payload=null;
            if(id<=0 || id>9007199254740991L || baseline<0 || baseline>=id || data==null || data.Length>4*((MaxBytes+2)/3))return FrameDecodeResult.Invalid;
            if(id<=highest)return FrameDecodeResult.Stale;
            if(baseline>0 && !frames.ContainsKey(baseline)){Ack=0;return FrameDecodeResult.MissingBaseline;}
            try {
                byte[] encoded=Convert.FromBase64String(data), result;
                if(baseline==0)result=encoded;
                else {
                    // Cached baselines own their arrays; only the temporary patch workspace is reused.
                    var source=frames[baseline];var output=scratch??(scratch=new byte[MaxBytes]);int count=0;
                    for(int i=0;i<encoded.Length;){
                        int code=encoded[i++];
                        if(code==0){
                            if(i+4>encoded.Length)return FrameDecodeResult.Invalid;
                            int at=encoded[i]|encoded[i+1]<<8,n=encoded[i+2]|encoded[i+3]<<8;i+=4;
                            if(n==0 || at+n>source.Length || count+n>MaxBytes)return FrameDecodeResult.Invalid;
                            Buffer.BlockCopy(source,at,output,count,n);count+=n;
                        }else if(code==1){
                            if(i+2>encoded.Length)return FrameDecodeResult.Invalid;int n=encoded[i]|encoded[i+1]<<8;i+=2;
                            if(n==0 || i+n>encoded.Length || count+n>MaxBytes)return FrameDecodeResult.Invalid;
                            Buffer.BlockCopy(encoded,i,output,count,n);i+=n;count+=n;
                        }else return FrameDecodeResult.Invalid;
                    }
                    result=new byte[count];Buffer.BlockCopy(output,0,result,0,count);
                }
                if(result.Length>MaxBytes)return FrameDecodeResult.Invalid;
                payload=utf8.GetString(result);frames[id]=result;order.Enqueue(id);Ack=highest=id;
                while(order.Count>64)frames.Remove(order.Dequeue());return FrameDecodeResult.Applied;
            }catch(FormatException){return FrameDecodeResult.Invalid;}catch(DecoderFallbackException){return FrameDecodeResult.Invalid;}
        }
    }
}
