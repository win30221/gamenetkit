using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rumblebound.Example.Battle.Unity
{
    public sealed partial class BattleHud
    {
        private VisualElement miniMap,mapViewport;
        private string mapKey;
        private readonly Dictionary<string,VisualElement> mapDots=new Dictionary<string,VisualElement>();
        private void CreateMiniMap(){
            miniMap=Box("battle-minimap");miniMap.name="battleMiniMap";hudLayer.Add(miniMap);
            void Peek(Vector2 position){if(Flow.Content==null)return;var local=miniMap.WorldToLocal(position);float half=(float)Flow.Content.Map.arenaHalfSize;
                client.Presentation.PeekAt(new Vector2((Mathf.Clamp01(local.x/miniMap.contentRect.width)*2-1)*half,(1-Mathf.Clamp01(local.y/miniMap.contentRect.height)*2)*half));}
            miniMap.RegisterCallback<PointerDownEvent>(evt=>{if(BlocksGameplay)return;miniMap.CapturePointer(evt.pointerId);Peek(evt.position);evt.StopPropagation();});
            miniMap.RegisterCallback<PointerMoveEvent>(evt=>{if(miniMap.HasPointerCapture(evt.pointerId)){Peek(evt.position);evt.StopPropagation();}});
            miniMap.RegisterCallback<PointerUpEvent>(evt=>{client.Presentation.PeekAt(null);miniMap.ReleasePointer(evt.pointerId);evt.StopPropagation();});
            miniMap.RegisterCallback<PointerCancelEvent>(evt=>{client.Presentation.PeekAt(null);miniMap.ReleasePointer(evt.pointerId);});
            miniMap.RegisterCallback<PointerCaptureOutEvent>(_=>client.Presentation.PeekAt(null));
        }
        private void UpdateMiniMap(){
            if(miniMap==null)CreateMiniMap();
            var world=client.Presentation.World;var content=Flow.Playback.Active?Flow.Playback.Content:Flow.Content;
            if(world?.snapshot==null||content==null){miniMap.style.display=DisplayStyle.None;return;}miniMap.style.display=DisplayStyle.Flex;
            float half=(float)content.Map.arenaHalfSize;
            if(mapKey!=content.Config.mapHash){
                mapKey=content.Config.mapHash;miniMap.Clear();mapDots.Clear();
                foreach(var o in content.Map.obstacles){var box=Box("map-cover");box.pickingMode=PickingMode.Ignore;box.style.left=Length.Percent((float)((o.x-o.halfX+half)/(2*half)*100));box.style.top=Length.Percent((float)((half-o.z-o.halfZ)/(2*half)*100));box.style.width=Length.Percent((float)(o.halfX/half*100));box.style.height=Length.Percent((float)(o.halfZ/half*100));miniMap.Add(box);}
                for(int i=0;i<content.Map.controlPoints.Length;i++){var point=content.Map.controlPoints[i];var dot=Box("map-point");dot.name="mapPoint"+i;PlaceMap(dot,point.x,point.z,half,4);miniMap.Add(dot);}
                mapViewport=Box("map-viewport");mapViewport.pickingMode=PickingMode.Ignore;miniMap.Add(mapViewport);
            }
            foreach(var actor in world.players){
                var p=actor.state;if(!mapDots.TryGetValue(p.playerID,out var dot)){dot=Box("map-player");dot.pickingMode=PickingMode.Ignore;miniMap.Add(dot);mapDots[p.playerID]=dot;}
                dot.style.display=p.hp>0&&!p.left?DisplayStyle.Flex:DisplayStyle.None;dot.style.backgroundColor=actor.own?Color.white:TeamColor(p.teamID);PlaceMap(dot,actor.x,actor.z,half,3);
            }
            var camera=client.Presentation.Camera;
            if(camera!=null){var plane=new Plane(Vector3.up,Vector3.zero);float minX=half,maxX=-half,minZ=half,maxZ=-half;
                foreach(var corner in new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.one}){var ray=camera.ViewportPointToRay(corner);if(!plane.Raycast(ray,out float distance))continue;var hit=ray.GetPoint(distance);minX=Mathf.Min(minX,hit.x);maxX=Mathf.Max(maxX,hit.x);minZ=Mathf.Min(minZ,hit.z);maxZ=Mathf.Max(maxZ,hit.z);}
                minX=Mathf.Clamp(minX,-half,half);maxX=Mathf.Clamp(maxX,-half,half);minZ=Mathf.Clamp(minZ,-half,half);maxZ=Mathf.Clamp(maxZ,-half,half);
                mapViewport.style.left=Length.Percent((minX+half)/(2*half)*100);mapViewport.style.top=Length.Percent((half-maxZ)/(2*half)*100);mapViewport.style.width=Length.Percent((maxX-minX)/(2*half)*100);mapViewport.style.height=Length.Percent((maxZ-minZ)/(2*half)*100);
            }
            if(Input.GetKeyDown(KeyCode.Space))client.Presentation.Recenter();
        }
        private static void PlaceMap(VisualElement dot,double x,double z,float half,float radius){dot.style.left=Length.Percent((float)((x+half)/(2*half)*100));dot.style.top=Length.Percent((float)((half-z)/(2*half)*100));dot.style.translate=new Translate(-radius,-radius);}
    }
}
