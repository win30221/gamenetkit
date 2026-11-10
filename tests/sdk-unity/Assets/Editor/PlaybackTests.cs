using System;
using Realtime.Playback;
using UnityEngine;

public static class PlaybackTests
{
    public static void Run()
    {
        var clock=new PlaybackClock();clock.Play();Require(!clock.Playing&&clock.FrameIndex==-1,"unloaded clock");
        long[] ticks={10,11,14,30};clock.Load(ticks,30);ticks[0]=999;
        Require(clock.FirstTick==10&&clock.Tick==10&&clock.FrameIndex==0&&!clock.Playing,"load owns ticks");
        clock.Play();clock.Advance(.1);Require(Math.Abs(clock.Tick-13)<1e-8&&clock.FrameIndex==1,"sparse tick selection");
        clock.Pause();clock.Advance(1);Require(clock.Tick==13,"pause");clock.Seek(14.5);Require(clock.FrameIndex==2,"seek floor");
        clock.Speed=2;clock.Play();clock.Advance(.1);Require(Math.Abs(clock.Tick-20.5)<1e-8,"unscaled speed");
        clock.Advance(10);Require(clock.Ended&&!clock.Playing&&clock.FrameIndex==3,"bounded end");clock.Play();Require(clock.Tick==10&&clock.Playing,"restart");
        clock.Seek(-100);Require(clock.Tick==10,"seek lower bound");clock.Seek(100);Require(clock.Tick==30,"seek upper bound");
        Reject(()=>clock.Load(new long[]{1,1},30));Reject(()=>clock.Load(new long[]{2,1},30));Reject(()=>clock.Load(new long[]{-1},30));Reject(()=>clock.Load(new long[]{0,108001},30));Reject(()=>clock.Load(new long[]{0},0));
        Reject(()=>clock.Advance(double.NaN));Reject(()=>clock.Advance(-1));Reject(()=>clock.Seek(double.PositiveInfinity));Reject(()=>clock.Speed=0);Reject(()=>clock.Speed=8);
        Require(clock.LastTick==30,"invalid load partially changed clip");clock.Load(new long[]{7},30);clock.Play();Require(!clock.Playing&&clock.Ended,"single frame");
        clock.Clear();Require(clock.FrameIndex==-1&&clock.Tick==0&&!clock.Playing,"clear");
        Debug.Log("RF_PLAYBACK_SDK PASS: owned atomic tick index, sparse seek, pause, unscaled speed, bounds/end/restart, invalid data and clear");
    }
    private static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Playback accepted invalid input");}
    private static void Require(bool passed,string message){if(!passed)throw new Exception(message);}
}
