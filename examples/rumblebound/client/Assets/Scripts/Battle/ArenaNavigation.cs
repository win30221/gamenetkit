using System;
using System.Collections.Generic;
using Rumblebound.Gameplay;

namespace Rumblebound.Example.Battle
{
    // Bounded navigation over the same collision map as prediction/authority.
    // Every edge and final shortcut is swept with the player's actual radius.
    public static class ArenaNavigation
    {
        public static bool Clear(ArenaMapDefinition map,double x,double z,double tx,double tz,double radius)=>
            !ArenaCollision.FirstMapHit(map,x,z,tx-x,tz-z,radius,false,out double at,out _,out _)||at>=1-ArenaCollision.ContactEpsilon;
        public static ArenaPoint[] Find(ArenaMapDefinition map,double x,double z,double tx,double tz,double radius)
        {
            if(map==null)return new[]{new ArenaPoint{x=tx,z=tz}};
            double limit=map.arenaHalfSize-radius-.04;
            tx=Math.Max(-limit,Math.Min(limit,tx));tz=Math.Max(-limit,Math.Min(limit,tz));
            bool exact=ArenaCollision.CircleClear(map,tx,tz,radius);
            if(exact&&Clear(map,x,z,tx,tz,radius))return new[]{new ArenaPoint{x=tx,z=tz}};
            int side=Math.Min(65,(int)Math.Ceiling(2*limit/.5)+1),count=side*side;
            double step=2*limit/(side-1);
            var valid=new bool[count];var cost=new double[count];var parent=new int[count];var closed=new bool[count];
            double X(int n)=>-limit+(n%side)*step;double Z(int n)=>-limit+(n/side)*step;
            int start=-1,goal=-1;double ds=double.PositiveInfinity,dg=ds;
            for(int n=0;n<count;n++){
                cost[n]=double.PositiveInfinity;parent[n]=-1;
                if(!(valid[n]=ArenaCollision.CircleClear(map,X(n),Z(n),radius)))continue;
                double a=(X(n)-x)*(X(n)-x)+(Z(n)-z)*(Z(n)-z),b=(X(n)-tx)*(X(n)-tx)+(Z(n)-tz)*(Z(n)-tz);
                if(a<ds&&Clear(map,x,z,X(n),Z(n),radius)){ds=a;start=n;}
                if(b<dg){dg=b;goal=n;}
            }
            if(start<0||goal<0)return Array.Empty<ArenaPoint>();
            var heap=new List<(int node,double priority)>();
            void Push(int node,double priority){int i=heap.Count;heap.Add((node,priority));while(i>0){int p=(i-1)/2;if(heap[p].priority<=priority)break;heap[i]=heap[p];i=p;}heap[i]=(node,priority);}
            int Pop(){var first=heap[0];var last=heap[heap.Count-1];heap.RemoveAt(heap.Count-1);if(heap.Count>0){int i=0;while(i*2+1<heap.Count){int c=i*2+1;if(c+1<heap.Count&&heap[c+1].priority<heap[c].priority)c++;if(last.priority<=heap[c].priority)break;heap[i]=heap[c];i=c;}heap[i]=last;}return first.node;}
            double Heuristic(int n)=>Math.Sqrt((X(n)-X(goal))*(X(n)-X(goal))+(Z(n)-Z(goal))*(Z(n)-Z(goal)));
            cost[start]=0;Push(start,Heuristic(start));
            while(heap.Count>0){
                int n=Pop();if(closed[n])continue;closed[n]=true;if(n==goal)break;
                for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++){
                    int ix=n%side+dx,iz=n/side+dz;if(dx==0&&dz==0||ix<0||iz<0||ix>=side||iz>=side)continue;
                    int next=iz*side+ix;if(!valid[next]||closed[next]||!Clear(map,X(n),Z(n),X(next),Z(next),radius))continue;
                    double nextCost=cost[n]+step*(dx!=0&&dz!=0?Math.Sqrt(2):1);
                    if(nextCost>=cost[next])continue;cost[next]=nextCost;parent[next]=n;Push(next,nextCost+Heuristic(next));
                }
            }
            if(!closed[goal])return Array.Empty<ArenaPoint>();
            var raw=new List<ArenaPoint>();for(int n=goal;n>=0;n=parent[n])raw.Add(new ArenaPoint{x=X(n),z=Z(n)});raw.Reverse();
            if(exact&&Clear(map,raw[raw.Count-1].x,raw[raw.Count-1].z,tx,tz,radius))raw.Add(new ArenaPoint{x=tx,z=tz});
            var result=new List<ArenaPoint>();int index=0;
            while(index<raw.Count){int last=index;for(int j=index;j<raw.Count;j++)if(Clear(map,x,z,raw[j].x,raw[j].z,radius))last=j;
                var point=raw[last];result.Add(point);x=point.x;z=point.z;index=last+1;}
            return result.ToArray();
        }
    }
}
