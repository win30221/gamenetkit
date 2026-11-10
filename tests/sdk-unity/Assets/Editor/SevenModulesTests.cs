using System;
using System.IO;
using System.Text;
using Realtime.Core;
using Realtime.Clock;
using Realtime.Replication;
using Realtime.Actions;
using Realtime.Codec;
using UnityEngine;

public static class SevenModulesTests
{
    private sealed class Item { public string ID,Life;public double[] Position; }
    [Serializable] private sealed class Frame { public long id,@base;public string data; }
    [Serializable] private sealed class Case { public Frame frame;public string expected; }
    [Serializable] private sealed class Fixtures { public Case[] cases; }
    private static void Require(bool value,string message){if(!value)throw new Exception("Seven modules SDK: "+message);}
    private static void Equal(double a,double b){Require(Math.Abs(a-b)<1e-7,a+" != "+b);}
    public static void Run()
    {
        var clock=new DisciplinedClock(30);clock.Observe(30,1.1,.2);Equal(clock.Tick(1.1),33);
        double before=clock.Tick(1.2);Equal(before,36);clock.Observe(29,2,0);Require(clock.Tick(1.3)>=before,"stale clock rewound");
        Require(clock.Tick(100)<=45,"clock extrapolated through outage");clock.Reset();Equal(clock.Tick(100),0);clock.Observe(0,100,0);Equal(clock.Tick(100),0);
        var store=new ReplicaStore<Item>(p=>p.ID,p=>p.Life,p=>new Item{ID=p.ID,Life=p.Life,Position=(double[])p.Position.Clone()},2);
        var publishedValues=store.Values;
        var input=new Item{ID="a",Life="one",Position=new[]{1.0}};
        Require(store.Apply(1,1,new[]{input})&&store.Added.Count==1,"spawn");input.Position[0]=99;Equal(store.Values["a"].Position[0],1);
        Require(store.Apply(1,2,new[]{new Item{ID="a",Life="two",Position=new[]{2.0}}})&&store.Removed.Count==1&&store.Added.Count==1,"new life did not reset");
        Require(!store.Apply(1,3,new[]{input,input})&&store.Values["a"].Life=="two","invalid roster partially applied");
        Require(store.Added.Count==1&&store.Removed.Count==1&&store.Updated.Count==0,"invalid roster changed lifecycle lists");
        Require(ReferenceEquals(publishedValues,store.Values)&&publishedValues["a"].Life=="two","published replica view was replaced or stale");
        Require(!store.Apply(0,9,new[]{input})&&!store.Apply(1,1,new[]{input}),"old replica generation/tick accepted");
        Require(store.Apply(1,4,Array.Empty<Item>())&&store.Removed.Count==1,"despawn");Require(store.Apply(2,0,new[]{input})&&store.Added.Count==1,"reconnect/reentry");
        bool rejectClone=false;
        var failingStore=new ReplicaStore<Item>(p=>p.ID,p=>p.Life,p=>{
            if(rejectClone&&p.ID=="b")throw new InvalidOperationException("clone failure");
            return new Item{ID=p.ID,Life=p.Life,Position=(double[])p.Position.Clone()};
        });
        var second=new Item{ID="b",Life="one",Position=new[]{2.0}};
        Require(failingStore.Apply(1,1,new[]{input}),"initial exception-test roster");
        var original=failingStore.Values["a"];rejectClone=true;bool cloneFailed=false;
        try { failingStore.Apply(1,2,new[]{input,second}); } catch(InvalidOperationException) { cloneFailed=true; }
        Require(cloneFailed&&failingStore.Values.Count==1&&ReferenceEquals(original,failingStore.Values["a"])&&failingStore.Added.Count==1,"clone failure changed published state");
        rejectClone=false;
        Require(failingStore.Apply(1,2,new[]{input,second})&&failingStore.Values.Count==2,"staged clones survived failed apply");
        input.Position[0]=7;Equal(failingStore.Values["a"].Position[0],99);
        failingStore.Clear();Require(failingStore.Values.Count==0&&failingStore.Apply(1,0,new[]{second}),"clear/reuse failed");
        var actions=new ActionPresentation(2,1,4);
        Require(actions.Begin(1,0)&&!actions.Begin(1,0)&&actions.Begin(2,0)&&!actions.Begin(3,0),"action bounds/dedup");
        Require(actions.Resolve(1,true)&&!actions.Resolve(1,true)&&actions.Confirmed==1,"confirmation replayed");
        actions.Advance(1.1);Require(actions.Expired==1&&actions.Pending==0&&actions.WasPresented(2),"timeout forgot dedup");
        Require(actions.Resolve(2,true)&&actions.Confirmed==2,"late confirmation");actions.Advance(5);Require(actions.Begin(3,5)&&actions.Resolve(3,false)&&actions.Rejected==1,"rejection");actions.Clear();Require(actions.Pending==0&&!actions.WasPresented(1),"action reset");
        var fixtures=JsonUtility.FromJson<Fixtures>(File.ReadAllText("../fixtures/codec.json"));var decoder=new FrameDecoder();
        foreach(var item in fixtures.cases){Require(decoder.Decode(item.frame.id,item.frame.@base,item.frame.data,out var text)==FrameDecodeResult.Applied&&text==item.expected,"Go/C# byte codec parity");}
        long highest=decoder.Ack;Require(decoder.Decode(100,99,"AA==",out _)==FrameDecodeResult.MissingBaseline&&decoder.Ack==0,"missing baseline did not request recovery");
        Require(decoder.Decode(highest,0,Convert.ToBase64String(Encoding.UTF8.GetBytes("old")),out _)==FrameDecodeResult.Stale,"recovery reset replay protection");
        Require(decoder.Decode(101,0,Convert.ToBase64String(Encoding.UTF8.GetBytes("recovered")),out var recovered)==FrameDecodeResult.Applied&&recovered=="recovered","full recovery");
        Require(decoder.Decode(102,101,"AA==",out _)==FrameDecodeResult.Invalid,"truncated copy accepted");
        Require(decoder.Decode(102,0,Convert.ToBase64String(new byte[]{255}),out _)==FrameDecodeResult.Invalid,"invalid UTF8 accepted");
        decoder.Reset();Require(decoder.Ack==0,"codec reset");
        Require(decoder.Decode(1,0,Convert.ToBase64String(Encoding.UTF8.GetBytes("abcdef")),out _)==FrameDecodeResult.Applied,"scratch baseline");
        Require(decoder.Decode(2,1,Convert.ToBase64String(new byte[]{1,2,0,88,89,0,2,0,4,0}),out var patched)==FrameDecodeResult.Applied&&patched=="XYcdef","mixed literal/copy patch");
        string copySix=Convert.ToBase64String(new byte[]{0,0,0,6,0});
        Require(decoder.Decode(3,1,copySix,out var originalText)==FrameDecodeResult.Applied&&originalText=="abcdef","patch workspace mutated old baseline");
        Require(decoder.Decode(4,2,Convert.ToBase64String(new byte[]{1,2,0,122,122,0}),out _)==FrameDecodeResult.Invalid&&decoder.Ack==3,"partial invalid patch was committed");
        Require(decoder.Decode(4,2,copySix,out var savedPatch)==FrameDecodeResult.Applied&&savedPatch=="XYcdef","invalid patch workspace corrupted cached frame");
        Require(decoder.Decode(5,4,Convert.ToBase64String(new byte[]{1,1,0,113}),out var shortPatch)==FrameDecodeResult.Applied&&shortPatch=="q","short patch retained scratch suffix");
        Require(decoder.Decode(6,2,copySix,out var olderPatch)==FrameDecodeResult.Applied&&olderPatch=="XYcdef","reused workspace aliased cached frame");
        Debug.Log("RF_SEVEN_SDK PASS: midpoint/monotonic/bounded clock; cloned atomic replica lifecycle/generations; bounded action confirm/reject/timeout/dedup; "+fixtures.cases.Length+" Go/C# codec fixtures, missing-baseline recovery, stale/invalid rejection and reset");
    }
}
