using System;

namespace Realtime.Examples.ReplayLesson
{
    // Acknowledgements describe server recording; they never modify live replicas.
    public sealed class RecordingStatus
    {
        public string Status{get;private set;}="idle";
        public string ID{get;private set;}="";
        public string Reason{get;private set;}="";
        public int Frames{get;private set;}
        public double Duration{get;private set;}
        public static bool ValidID(string id)
        {if(id==null || id.Length!=32)return false;foreach(char c in id)if(!(c>='0'&&c<='9'||c>='a'&&c<='f'))return false;return true;}
        public static void Validate(ServerMessage m)
        {
            if(m.protocol!=1 || (m.status!="recording"&&m.status!="finalizing"&&m.status!="ready"&&m.status!="error") || m.frameCount<0 || m.frameCount>451 || double.IsNaN(m.duration) || double.IsInfinity(m.duration) || m.duration<0 || m.duration>30 || (m.status!="error"&&!ValidID(m.recordingID)))throw new InvalidOperationException("Invalid recording status");
        }
        public void Apply(ServerMessage m){Validate(m);Status=m.status;ID=m.recordingID??"";Reason=m.reason??"";Frames=m.frameCount;Duration=m.duration;}
        public void Clear(){Status="idle";ID=Reason="";Frames=0;Duration=0;}
    }
}
