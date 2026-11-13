using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TowerDuel
{
    public sealed class DuelCardDrag : PointerManipulator
    {
        private readonly Func<bool> canStart;
        private readonly Action<Vector2,bool> begin;
        private readonly Action<Vector2> move,drop;
        private readonly Action cancel,click;
        private int pointer=-1;
        private Vector2 origin;
        private bool dragging,touch;
        public DuelCardDrag(Func<bool> canStart,Action<Vector2,bool> begin,Action<Vector2> move,Action<Vector2> drop,Action cancel,Action click)
        {this.canStart=canStart;this.begin=begin;this.move=move;this.drop=drop;this.cancel=cancel;this.click=click;}
        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(Down,TrickleDown.TrickleDown);
            target.RegisterCallback<PointerMoveEvent>(Move,TrickleDown.TrickleDown);
            target.RegisterCallback<PointerUpEvent>(Up,TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCancelEvent>(Cancelled);
            target.RegisterCallback<PointerCaptureOutEvent>(Lost);
        }
        protected override void UnregisterCallbacksFromTarget()
        {
            Cancel();
            target.UnregisterCallback<PointerDownEvent>(Down,TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(Move,TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(Up,TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCancelEvent>(Cancelled);
            target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
        }
        private void Down(PointerDownEvent evt)
        {
            if(pointer>=0||!evt.isPrimary||evt.button!=0||!canStart())return;
            pointer=evt.pointerId;origin=evt.position;dragging=false;touch=evt.pointerType!=UnityEngine.UIElements.PointerType.mouse;
            target.CapturePointer(pointer);target.Focus();evt.StopImmediatePropagation();
        }
        private void Move(PointerMoveEvent evt)
        {
            if(evt.pointerId!=pointer)return;
            if(!dragging&&((Vector2)evt.position-origin).sqrMagnitude>=16){dragging=true;begin(origin,touch);}
            if(dragging)move(evt.position);
            evt.StopImmediatePropagation();
        }
        private void Up(PointerUpEvent evt)
        {
            if(evt.pointerId!=pointer)return;
            bool wasDragging=dragging;Release();
            if(wasDragging)drop(evt.position);else click();
            evt.StopImmediatePropagation();
        }
        private void Cancelled(PointerCancelEvent evt){if(evt.pointerId==pointer)Cancel();}
        private void Lost(PointerCaptureOutEvent evt){if(evt.pointerId==pointer)Cancel();}
        private void Release(){int old=pointer;pointer=-1;dragging=false;if(old>=0&&target.HasPointerCapture(old))target.ReleasePointer(old);}
        public void Cancel(){bool wasDragging=dragging;Release();if(wasDragging)cancel();}
    }
}
