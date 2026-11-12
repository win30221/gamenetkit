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
        private readonly HashSet<string> mapSeen=new HashSet<string>();
        private readonly List<string> mapRemoved=new List<string>();
        private readonly List<VisualElement> mapPoints=new List<VisualElement>();
        private static readonly Vector3[] MapCorners={Vector3.zero,Vector3.right,Vector3.up,Vector3.one};
        private Label pointDirection;
        private readonly Dictionary<string,VisualElement> mapDots=new Dictionary<string,VisualElement>();
        private void CreateMiniMap(){
            miniMap=Box("battle-minimap");miniMap.name="battleMiniMap";hudLayer.Add(miniMap);
            pointDirection=Text("","point-direction");Ignore(pointDirection);hudLayer.Add(pointDirection);
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
                mapKey=content.Config.mapHash;miniMap.Clear();mapDots.Clear();mapPoints.Clear();
                foreach(var o in content.Map.obstacles){var box=Box("map-cover");box.pickingMode=PickingMode.Ignore;box.style.left=Length.Percent((float)((o.x-o.halfX+half)/(2*half)*100));box.style.top=Length.Percent((float)((half-o.z-o.halfZ)/(2*half)*100));box.style.width=Length.Percent((float)(o.halfX/half*100));box.style.height=Length.Percent((float)(o.halfZ/half*100));miniMap.Add(box);}
                for(int i=0;i<content.Map.controlPoints.Length;i++){var point=content.Map.controlPoints[i];var dot=Box("map-point");dot.name="mapPoint"+i;dot.pickingMode=PickingMode.Ignore;PlaceMap(dot,point.x,point.z,half,4);miniMap.Add(dot);mapPoints.Add(dot);}
                mapViewport=Box("map-viewport");mapViewport.pickingMode=PickingMode.Ignore;miniMap.Add(mapViewport);
            }
            var match=world.snapshot.match;
            for(int i=0;i<mapPoints.Count;i++){
                var dot=mapPoints[i];bool active=match?.point?.index==i,next=match?.nextPoint?.index==i&&!active;
                dot.EnableInClassList("point-active",active);dot.EnableInClassList("point-next",next);
                dot.EnableInClassList("point-contested",active&&match.pointStatus=="contested");
                dot.style.backgroundColor=active&&match.pointStatus=="scoring"?TeamColor(match.scoringTeamID):new Color(.93f,.8f,.45f);
                dot.style.opacity=next?(Mathf.Sin((float)client.Now*6)>0?1:.35f):active?1:.4f;
                dot.style.translate=new Translate(active||next?-6:-4,active||next?-6:-4);
            }
            mapSeen.Clear();foreach(var actor in world.players){
                var p=actor.state;if(!mapDots.TryGetValue(p.playerID,out var dot)){dot=Box("map-player");dot.pickingMode=PickingMode.Ignore;miniMap.Add(dot);mapDots[p.playerID]=dot;}
                mapSeen.Add(p.playerID);
                dot.style.display=p.hp>0&&!p.left?DisplayStyle.Flex:DisplayStyle.None;dot.style.backgroundColor=actor.own?Color.white:TeamColor(p.teamID);PlaceMap(dot,actor.x,actor.z,half,3);
            }
            mapRemoved.Clear();foreach(var id in mapDots.Keys)if(!mapSeen.Contains(id))mapRemoved.Add(id);
            foreach(var id in mapRemoved){mapDots[id].RemoveFromHierarchy();mapDots.Remove(id);}
            var camera=client.Presentation.Camera;
            pointDirection.style.display=DisplayStyle.None;
            if(camera!=null&&match?.point!=null){
                var screen=camera.WorldToViewportPoint(new Vector3((float)match.point.x,0,(float)match.point.z));
                if(screen.z<=0||screen.x<0||screen.x>1||screen.y<0||screen.y>1){
                    var direction=new Vector2(screen.x-.5f,screen.y-.5f);if(screen.z<0)direction=-direction;
                    string arrow=Mathf.Abs(direction.x)>Mathf.Abs(direction.y)?direction.x>0?"→":"←":direction.y>0?"↑":"↓";
                    pointDirection.text=arrow+" 據點 "+(match.point.index+1);pointDirection.style.display=DisplayStyle.Flex;
                }
            }
            if(camera!=null){var plane=new Plane(Vector3.up,Vector3.zero);float minX=half,maxX=-half,minZ=half,maxZ=-half;
                foreach(var corner in MapCorners){var ray=camera.ViewportPointToRay(corner);if(!plane.Raycast(ray,out float distance))continue;var hit=ray.GetPoint(distance);minX=Mathf.Min(minX,hit.x);maxX=Mathf.Max(maxX,hit.x);minZ=Mathf.Min(minZ,hit.z);maxZ=Mathf.Max(maxZ,hit.z);}
                minX=Mathf.Clamp(minX,-half,half);maxX=Mathf.Clamp(maxX,-half,half);minZ=Mathf.Clamp(minZ,-half,half);maxZ=Mathf.Clamp(maxZ,-half,half);
                mapViewport.style.left=Length.Percent((minX+half)/(2*half)*100);mapViewport.style.top=Length.Percent((half-maxZ)/(2*half)*100);mapViewport.style.width=Length.Percent((maxX-minX)/(2*half)*100);mapViewport.style.height=Length.Percent((maxZ-minZ)/(2*half)*100);
            }
            if(Input.GetKeyDown(KeyCode.Space))client.Presentation.Recenter();
        }
        private static void PlaceMap(VisualElement dot,double x,double z,float half,float radius){dot.style.left=Length.Percent((float)((x+half)/(2*half)*100));dot.style.top=Length.Percent((float)((half-z)/(2*half)*100));dot.style.translate=new Translate(-radius,-radius);}
    }
}
