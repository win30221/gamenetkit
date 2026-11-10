using System;
using Realtime.Core;

public static class ReplyTrackerTests
{
    private static void Require(bool value,string reason){if(!value)throw new Exception("Reply tracker: "+reason);}
    public static void Run()
    {
        var tracker=new ReplyTracker(2,5);
        Require(!tracker.TryStart(0,0)&&!tracker.TryStart(1,double.NaN),"invalid requests entered tracker");
        Require(tracker.TryStart(1,0)&&!tracker.TryStart(1,.1),"duplicate ID replaced original send time");
        Require(tracker.TryStart(2,.1)&&!tracker.TryStart(3,.2),"capacity was not bounded");
        Require(tracker.TryComplete(1,.3,out double elapsed)&&Math.Abs(elapsed-.3)<1e-10,"wrong latency origin");
        Require(!tracker.TryComplete(1,.4,out _)&&!tracker.TryComplete(99,.4,out _),"duplicate/unknown reply produced sample");
        Require(tracker.TryStart(3,.5),"completed request did not release capacity");
        tracker.DiscardThrough(2);Require(!tracker.Contains(2)&&tracker.Contains(3),"cumulative ack removed a newer input");
        Require(!tracker.TryComplete(3,.4,out _),"backward clock produced latency");
        Require(tracker.TryStart(4,1)&&!tracker.TryComplete(4,6.1,out _),"expired reply produced sample");
        Require(tracker.TryStart(5,7)&&tracker.TryStart(6,7),"fresh requests rejected");
        Require(tracker.TryStart(7,12.1)&&tracker.Count==1,"expired requests leaked capacity");
        tracker.Clear();Require(tracker.Count==0&&!tracker.Contains(7),"connection reset leaked request");
        var probes=new ProbeScheduler(.5,5,2);
        Require(probes.TryBegin(0,out long one)&&one==1&&!probes.TryBegin(.49,out _),"probe interval changed");
        Require(probes.TryBegin(.5,out long two)&&two==2&&!probes.TryBegin(1,out _),"probe capacity not bounded");
        Require(probes.TryComplete(one,1,out elapsed)&&elapsed==1&&!probes.TryComplete(one,1,out _),"probe consumed more than once");
        Require(probes.TryBegin(1,out long three)&&three==3,"probe did not resume after reply");
        probes.Clear();Require(!probes.Contains(two)&&probes.TryBegin(0,out one)&&one==1,"probe reset failed");
        Console.WriteLine("REMAKE_REPLY_TRACKER PASS bounded requests, one-use replies, cumulative ack, expiration and probe scheduling/reset");
    }
}
