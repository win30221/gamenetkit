using System;
using System.Collections.Generic;
using Realtime.Actions;

namespace Realtime.Examples.ObjectsActions
{
    public sealed class ActionFeedback
    {
        public sealed class Row
        {
            public long id;public string kind,phase,reason="",objectID="";
            public float x,z;public bool speculative,visible;public int starts;public double created;
        }
        private readonly ActionPresentation ledger=new ActionPresentation(32,1.4,4);
        private readonly List<Row> rows=new List<Row>();
        public IReadOnlyList<Row> Rows=>rows;
        public int Pending=>ledger.Pending;
        public long Confirmed=>ledger.Confirmed;
        public long Rejected=>ledger.Rejected;
        public long Expired=>ledger.Expired;
        public bool Begin(long id,string kind,bool speculative,float x,float z,double now)
        {
            Advance(now);if(!ledger.Begin(id,now))return false;
            rows.Add(new Row{id=id,kind=kind,phase="pending",speculative=speculative,visible=speculative,starts=speculative?1:0,x=x,z=z,created=now});return true;
        }
        public bool Resolve(ServerMessage m)
        {
            var row=rows.Find(r=>r.id==m.id);
            if(row==null || row.kind!=m.kind || !ledger.Resolve(m.id,m.accepted))return false;
            row.phase=m.accepted?"confirmed":"rejected";row.reason=m.reason;row.objectID=m.objectID;
            if(m.accepted){row.x=m.x;row.z=m.z;if(!row.speculative)row.starts++;row.visible=true;}
            else row.visible=false;
            return true;
        }
        public void Advance(double now)
        {
            ledger.Advance(now);
            foreach(var row in rows)if(row.phase=="pending" && now-row.created>1.4){row.phase="expired";row.visible=false;}
            rows.RemoveAll(r=>now-r.created>4);
        }
        public void Clear(){ledger.Clear();rows.Clear();}
    }
}
