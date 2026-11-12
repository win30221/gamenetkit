using UnityEngine;
using UnityEngine.UIElements;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed partial class BattleHud
    {
        private VisualElement touchLayer,movePad,aimPad,attackPad,moveKnob,aimKnob,attackKnob,dashPad;
        private bool touchSeen;
        private VisualElement moveZone;
        private Vector2 moveOrigin;
        private int moveOriginPointer=-1;
        private bool rawTouches;
        private BattleTouchInput.Sample[] touchSamples=new BattleTouchInput.Sample[10];
        private int touchWidth,touchHeight;
        private Label basicCooldown,skillCooldown,dashCooldown;
        private bool ReadingTouches=>rawTouches||Input.touchCount>0;
        public static bool TouchMode=>instance!=null&&(instance.touchSeen||Application.isMobilePlatform);
        public static bool TouchCanPlay=>instance!=null&&instance.client!=null&&instance.client.CanTouchControl&&!BlocksGameplay&&!instance.Flow.Playback.Active;
        private void BuildTouchControls()
        {
            touchLayer=Box("touch-controls");touchLayer.name="touchControls";touchLayer.pickingMode=PickingMode.Ignore;
            moveZone=Box("touch-move-zone");touchLayer.Add(moveZone);
            movePad=Stick("touchMove","移動",false,out moveKnob);
            aimPad=Stick("touchAim","技能",true,out aimKnob);
            attackPad=Stick("touchAttack","普攻",false,out attackKnob,true);
            basicCooldown=Text("","touch-cooldown");basicCooldown.pickingMode=PickingMode.Ignore;attackPad.Add(basicCooldown);
            skillCooldown=Text("","touch-cooldown");skillCooldown.pickingMode=PickingMode.Ignore;aimPad.Add(skillCooldown);
            moveZone.Add(movePad);touchLayer.Add(aimPad);touchLayer.Add(attackPad);
            var dash=Box("touch-dash");dashPad=dash;dash.name="touchDash";var label=Text("衝刺");label.pickingMode=PickingMode.Ignore;dash.Add(label);
            dashCooldown=Text("","touch-cooldown");dashCooldown.pickingMode=PickingMode.Ignore;dash.Add(dashCooldown);
            dash.RegisterCallback<PointerDownEvent>(evt=>{if(ReadingTouches||!TouchCanPlay||!client.TouchInput.BeginDash(evt.pointerId))return;dash.CapturePointer(evt.pointerId);evt.StopPropagation();});
            dash.RegisterCallback<PointerUpEvent>(evt=>{if(ReadingTouches)return;client.TouchInput.EndDash(evt.pointerId);dash.ReleasePointer(evt.pointerId);evt.StopPropagation();});
            dash.RegisterCallback<PointerCancelEvent>(evt=>{if(ReadingTouches)return;client.TouchInput.EndDash(evt.pointerId);dash.ReleasePointer(evt.pointerId);});
            dash.RegisterCallback<PointerCaptureOutEvent>(evt=>{if(!ReadingTouches)client.TouchInput.EndDash(evt.pointerId);});
            touchLayer.Add(dash);root.Insert(root.IndexOf(screenLayer),touchLayer);
            root.RegisterCallback<PointerDownEvent>(evt=>{if(evt.pointerType==UnityEngine.UIElements.PointerType.touch)touchSeen=true;},TrickleDown.TrickleDown);
            touchSeen=System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-rf-touch-controls")>=0;
            touchLayer.style.display=DisplayStyle.None;
        }
        private VisualElement Stick(string name,string label,bool aiming,out VisualElement knob,bool attacking=false)
        {
            var pad=Box("touch-stick"+(attacking?" touch-attack":aiming?" touch-aim":" touch-move"));pad.name=name;
            var title=Text(label,"touch-label");title.pickingMode=PickingMode.Ignore;pad.Add(title);
            var handle=Box("touch-knob");handle.pickingMode=PickingMode.Ignore;pad.Add(handle);knob=handle;
            void Drag(int pointer,Vector2 position){
                var local=!aiming&&!attacking?position-moveOrigin:pad.WorldToLocal(position)-pad.contentRect.center;
                float radius=Mathf.Max(1,pad.contentRect.width*.32f);
                if(attacking)client.TouchInput.DragAttack(pointer,local.x/radius,-local.y/radius);else client.TouchInput.Drag(aiming,pointer,local.x/radius,-local.y/radius);
            }
            var inputPad=!aiming&&!attacking?moveZone:pad;
            inputPad.RegisterCallback<PointerDownEvent>(evt=>{
                if(ReadingTouches||!TouchCanPlay||!(attacking?client.TouchInput.BeginAttack(evt.pointerId):client.TouchInput.Begin(aiming,evt.pointerId)))return;
                if(!aiming&&!attacking)SetMoveOrigin(evt.pointerId,evt.position);
                inputPad.CapturePointer(evt.pointerId);Drag(evt.pointerId,evt.position);evt.StopPropagation();
            });
            inputPad.RegisterCallback<PointerMoveEvent>(evt=>{if(ReadingTouches)return;Drag(evt.pointerId,evt.position);evt.StopPropagation();});
            inputPad.RegisterCallback<PointerUpEvent>(evt=>{
                if(ReadingTouches||!inputPad.HasPointerCapture(evt.pointerId))return;
                Drag(evt.pointerId,evt.position);if(attacking)client.TouchInput.EndAttack(evt.pointerId);else client.TouchInput.End(aiming,evt.pointerId);inputPad.ReleasePointer(evt.pointerId);evt.StopPropagation();
            });
            inputPad.RegisterCallback<PointerCancelEvent>(evt=>{if(ReadingTouches)return;if(attacking)client.TouchInput.EndAttack(evt.pointerId,true);else client.TouchInput.End(aiming,evt.pointerId,true);inputPad.ReleasePointer(evt.pointerId);evt.StopPropagation();});
            inputPad.RegisterCallback<PointerCaptureOutEvent>(evt=>{if(!ReadingTouches){if(attacking)client.TouchInput.EndAttack(evt.pointerId,true);else client.TouchInput.End(aiming,evt.pointerId,true);}});
            return pad;
        }
        public static void PollTouches()=>instance?.ReadTouchFrame();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal BattleTouchInput.Sample[] VerificationTouches;
#endif
        private void ReadTouchFrame()
        {
            if(touchLayer==null||client==null)return;
            if(!TouchCanPlay){ResetTouchControls();return;}
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(VerificationTouches!=null){touchSeen=true;rawTouches=true;client.TouchInput.ApplyTouches(VerificationTouches,VerificationTouches.Length);return;}
#endif
            int count=Input.touchCount;
            if(count==0){if(rawTouches){client.TouchInput.ApplyTouches(touchSamples,0);rawTouches=false;}return;}
            touchSeen=true;
            if(!rawTouches){ResetTouchControls();rawTouches=true;}
            if(touchSamples.Length<count)touchSamples=new BattleTouchInput.Sample[count];
            for(int i=0;i<count;i++){
                var touch=Input.GetTouch(i);
                var point=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(touch.position.x,Screen.height-touch.position.y));
                var target=attackPad.worldBound.Contains(point)?BattleTouchInput.Target.Attack:dashPad.worldBound.Contains(point)?BattleTouchInput.Target.Dash:moveZone.worldBound.Contains(point)?BattleTouchInput.Target.Move:aimPad.worldBound.Contains(point)?BattleTouchInput.Target.Aim:BattleTouchInput.Target.None;
                var pad=touch.fingerId==client.TouchInput.AimPointer||target==BattleTouchInput.Target.Aim?aimPad:movePad;
                // Once assigned, a finger remains on its original stick even if
                // it slides over another control or the touch-array order changes.
                if(touch.fingerId==client.TouchInput.MovePointer)pad=movePad;
                if(touch.fingerId==client.TouchInput.AttackPointer||target==BattleTouchInput.Target.Attack)pad=attackPad;
                if(touch.phase==TouchPhase.Began&&target==BattleTouchInput.Target.Move&&client.TouchInput.MovePointer<0)SetMoveOrigin(touch.fingerId,point);
                var offset=touch.fingerId==moveOriginPointer?point-moveOrigin:pad.WorldToLocal(point)-pad.contentRect.center;float radius=Mathf.Max(1,pad.contentRect.width*.32f);
                var phase=touch.phase==TouchPhase.Began?BattleTouchInput.Phase.Began:touch.phase==TouchPhase.Ended?BattleTouchInput.Phase.Ended:touch.phase==TouchPhase.Canceled?BattleTouchInput.Phase.Cancelled:BattleTouchInput.Phase.Held;
                touchSamples[i]=new BattleTouchInput.Sample(touch.fingerId,target,phase,offset.x/radius,-offset.y/radius);
            }
            client.TouchInput.ApplyTouches(touchSamples,count);
        }
        private void RefreshTouchControls()
        {
            if(touchLayer==null)return;
            if(Input.touchCount>0)touchSeen=true;
            bool rotated=touchWidth>0&&touchHeight>0&&(touchWidth>touchHeight)!=(Screen.width>Screen.height);
            touchWidth=Screen.width;touchHeight=Screen.height;
            bool visible=TouchMode&&TouchCanPlay;
            if(visible){var state=Flow.Battle.State.Latest;var own=System.Array.Find(state.players,p=>p.playerID==Flow.Battle.State.PlayerID);if(own!=null){
                string Cooldown(long until)=>until<=state.tick?"就緒":((until-state.tick)/(double)Flow.Content.Rules.tickRate).ToString("0.0")+"s";
                basicCooldown.text=own.attackReleaseTick>state.tick?"出手中":Cooldown(own.basicReadyTick);skillCooldown.text=Cooldown(own.fireReadyTick);dashCooldown.text=Cooldown(own.dashReadyTick);
                attackPad.EnableInClassList("cooling",own.basicReadyTick>state.tick);aimPad.EnableInClassList("cooling",own.fireReadyTick>state.tick);dashPad.EnableInClassList("cooling",own.dashReadyTick>state.tick);
            }}
            var safe=Screen.safeArea;touchLayer.style.left=14+safe.xMin/panel.scale;touchLayer.style.right=14+(Screen.width-safe.xMax)/panel.scale;touchLayer.style.bottom=14+safe.yMin/panel.scale;
            touchLayer.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            root.EnableInClassList("touch-mode",TouchMode);
            if(!visible||rotated)ResetTouchControls();
            PositionKnob(attackKnob,client.TouchInput.AttackHeld?client.TouchInput.AttackX:0,client.TouchInput.AttackHeld?client.TouchInput.AttackZ:0);
            if(client.TouchInput.MovePointer<0){movePad.style.translate=new Translate(0,0);moveOriginPointer=-1;}
            PositionKnob(moveKnob,client.TouchInput.MoveX,client.TouchInput.MoveZ);
            PositionKnob(aimKnob,client.TouchInput.AimPointer>=0?client.TouchInput.AimX*client.TouchInput.AimMagnitude:0,client.TouchInput.AimPointer>=0?client.TouchInput.AimZ*client.TouchInput.AimMagnitude:0);
        }
        private void SetMoveOrigin(int pointer,Vector2 point)
        {
            moveOriginPointer=pointer;moveOrigin=point;
            var local=moveZone.WorldToLocal(point);
            movePad.style.translate=new Translate(local.x-movePad.resolvedStyle.width/2,local.y-moveZone.contentRect.height+movePad.resolvedStyle.height/2);
        }
        private static void PositionKnob(VisualElement knob,double x,double z)
        {
            float radius=knob.parent.contentRect.width*.32f;
            if(float.IsNaN(radius))return;
            knob.style.left=Length.Percent(50);knob.style.top=Length.Percent(50);
            float half=knob.resolvedStyle.width>0?knob.resolvedStyle.width/2:23;
            knob.style.translate=new Translate((float)x*radius-half,(float)-z*radius-half);
        }
        private void ResetTouchControls()
        {
            int move=client.TouchInput.MovePointer,aim=client.TouchInput.AimPointer,attack=client.TouchInput.AttackPointer,dash=client.TouchInput.DashPointer;
            client.TouchInput.Reset();
            rawTouches=false;
            moveOriginPointer=-1;if(movePad!=null)movePad.style.translate=new Translate(0,0);
            if(move>=0)moveZone?.ReleasePointer(move);
            if(aim>=0)aimPad?.ReleasePointer(aim);
            if(attack>=0)attackPad?.ReleasePointer(attack);
            if(dash>=0)dashPad?.ReleasePointer(dash);
        }
        private void OnApplicationFocus(bool focused){if(!focused)ResetTouchControls();}
        private void OnApplicationPause(bool paused){if(paused)ResetTouchControls();}
    }
}
