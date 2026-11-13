using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using TowerDuel;

public static class ResponsiveAudit
{
    public static object SetSize(int width,int height)
    {
        var assembly=typeof(EditorWindow).Assembly;
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.FlattenHierarchy;
        var type=assembly.GetType("UnityEditor.GameView");
        var window=EditorWindow.GetWindow(type);window.Show();
        var selection=type.GetProperty("selectedSizeIndex",flags);
        if(SessionState.GetInt("TowerDuel.QA.originalSize",-1)<0)SessionState.SetInt("TowerDuel.QA.originalSize",(int)selection.GetValue(window));
        var sizesType=assembly.GetType("UnityEditor.GameViewSizes");
        var sizes=sizesType.GetProperty("instance",flags).GetValue(null);
        var group=sizesType.GetProperty("currentGroup",flags).GetValue(sizes);
        var sizeType=assembly.GetType("UnityEditor.GameViewSize");
        var fixedResolution=Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType"),"FixedResolution");
        var size=Activator.CreateInstance(sizeType,new object[]{fixedResolution,width,height,"Tower Duel QA "+width+"x"+height});
        group.GetType().GetMethod("AddCustomSize",flags).Invoke(group,new[]{size});
        int index=(int)group.GetType().GetMethod("GetTotalCount",flags).Invoke(group,null)-1;
        selection.SetValue(window,index);window.Repaint();
        return new {width,height,index};
    }
    public static void RestoreGameView()
    {
        var assembly=typeof(EditorWindow).Assembly;
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.FlattenHierarchy;
        var viewType=assembly.GetType("UnityEditor.GameView");var window=EditorWindow.GetWindow(viewType);
        viewType.GetProperty("selectedSizeIndex",flags).SetValue(window,SessionState.GetInt("TowerDuel.QA.originalSize",0));SessionState.EraseInt("TowerDuel.QA.originalSize");
        var sizesType=assembly.GetType("UnityEditor.GameViewSizes");var sizes=sizesType.GetProperty("instance",flags).GetValue(null);var group=sizesType.GetProperty("currentGroup",flags).GetValue(sizes);
        var labels=(string[])group.GetType().GetMethod("GetDisplayTexts",flags).Invoke(group,null);
        for(int i=labels.Length-1;i>=0;i--)if(labels[i].Contains("Tower Duel QA "))
        {
            var index=group.GetType().GetMethod("TotalIndexToCustomIndex",flags).Invoke(group,new object[]{i});
            group.GetType().GetMethod("RemoveCustomSize",flags).Invoke(group,new[]{index});
        }
    }
    public static object Measure()
    {
        var client=UnityEngine.Object.FindAnyObjectByType<TowerDuelClient>();
        var root=client.GetComponent<UIDocument>().rootVisualElement.Q("duelRoot");
        var cards=root.Query<Button>(className:"unit-card").ToList().Where(c=>c.resolvedStyle.display!=DisplayStyle.None).ToArray();
        var measured=cards.Select(c=>new {name=c.name,rect=c.worldBound,ratio=c.worldBound.width/c.worldBound.height}).ToArray();
        if(cards.Length!=4)throw new Exception("Expected four visible cards");
        foreach(var c in measured)
            if(Mathf.Abs(c.rect.width-c.rect.height*112f/150)*(Screen.height/root.worldBound.height)>2||!Inside(root.worldBound,c.rect))throw new Exception("Card distorted or clipped: "+c.name+" ratio="+c.ratio+" card="+c.rect+" root="+root.worldBound+" safe="+Screen.safeArea+" dock="+root.Q("dock").worldBound);
        var dock=root.Q("dockFrame").worldBound;
        if(!Inside(root.worldBound,dock))throw new Exception("Dock clipped");
        if(dock.height/root.worldBound.height>.185f)throw new Exception("Dock consumes too much battle space");
        // Width changes the visible side margins. Castles, labels and bridge approaches
        // must stay visible without changing vertical framing to fit outer grass.
        bool fullFieldInCanvas=true;
        foreach(float x in new[]{-9.68f,9.68f})foreach(float z in new[]{-14.68f,14.68f})
        {
            var p=client.Camera.WorldToViewportPoint(new Vector3(x,0,z));
            fullFieldInCanvas&=p.x>=0&&p.x<=1&&p.y>=0&&p.y<=1;
        }
        float tacticalMin=1,tacticalMax=0;
        foreach(var tower in client.Map.towers)
        {
            foreach(float x in new[]{tower.x-1.7f,tower.x+1.7f})
            {
                var edge=client.Camera.WorldToViewportPoint(new Vector3(x,0,tower.z));
                if(edge.x<0||edge.x>1)throw new Exception("Castle approach outside horizontal view");
            }
            foreach(float y in new[]{0,tower.kind=="keep"?TowerDuelClient.KeepHealthHeight:3.9f})
            {
                var p=client.Camera.WorldToViewportPoint(new Vector3(tower.x,y,tower.z));
                tacticalMin=Mathf.Min(tacticalMin,p.y);tacticalMax=Mathf.Max(tacticalMax,p.y);
            }
            if(tower.kind=="keep")
            {
                var p=client.Camera.WorldToViewportPoint(new Vector3(tower.x,0,tower.z+Mathf.Sign(tower.z)*2.1f));
                tacticalMin=Mathf.Min(tacticalMin,p.y);tacticalMax=Mathf.Max(tacticalMax,p.y);
            }
        }
        if(tacticalMin<client.BoardBottom||tacticalMax>1-client.BoardTop)throw new Exception("Castle or health label outside combat view");
        foreach(var label in root.Query<Label>(className:"keep-status").ToList())
            if(!Inside(root.worldBound,label.worldBound)||label.worldBound.Overlaps(root.Q("audioButton").worldBound)||label.worldBound.Overlaps(root.Q("leaveButton").worldBound))throw new Exception("Keep label clipped or behind controls");
        float maxHeight=client.Rules.cards.Where(c=>!c.IsSpell).Max(c=>client.Presentation.UnitHeight(c.id));
        foreach(float z in new[]{-14.68f,14.68f})
        {
            var p=client.Camera.WorldToViewportPoint(new Vector3(0,maxHeight,z));
            if(p.y<0||p.y>1)throw new Exception("Rear troop clipped by screen edge");
        }
        float error=0;
        foreach(float x in new[]{-8f,0,8f})foreach(float z in new[]{-13f,0,13f})
        {
            var original=new Vector3(x,0,z);var pixel=client.Camera.WorldToScreenPoint(original);
            var ray=client.Camera.ScreenPointToRay(pixel);new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float distance);
            error=Mathf.Max(error,Vector3.Distance(original,ray.GetPoint(distance)));
        }
        if(error>.001f)throw new Exception("Deployment coordinate projection changed");
        var hud=typeof(TowerDuelClient).GetField("hud",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(client);
        var drop=typeof(DuelHud).GetMethod("DropPoint",BindingFlags.NonPublic|BindingFlags.Instance);
        int sign=client.Session.State.seat==0?1:-1;
        var rear=client.Camera.WorldToScreenPoint(new Vector3(0,0,sign*14.6f));
        var opening=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(rear.x,Screen.height-rear.y));
        if(!(bool)drop.Invoke(hud,new object[]{opening,default(Vector3)}))throw new Exception("Header opening rejects arena input");
        foreach(var blocked in new[]{dock.center,root.Q("audioButton").worldBound.center,root.Q("leaveButton").worldBound.center})
            if((bool)drop.Invoke(hud,new object[]{blocked,default(Vector3)}))throw new Exception("UI control permits deployment through it");
        return new {width=Screen.width,height=Screen.height,cameraPitch=client.Camera.transform.eulerAngles.x,
            cards=measured.Select(c=>new {c.name,c.ratio,x=c.rect.x,y=c.rect.y,width=c.rect.width,height=c.rect.height}).ToArray(),
            dockX=dock.x,dockY=dock.y,dockWidth=dock.width,dockHeight=dock.height,dockFraction=dock.height/root.worldBound.height,rootWidth=root.worldBound.width,rootHeight=root.worldBound.height,
            fullFieldInCanvas,tacticalContentClear=true,headerOpeningAcceptsDrop=true,controlsBlockDrop=true,coordinateError=error,boardBottom=client.BoardBottom,boardTop=client.BoardTop,
            orthoSize=client.Camera.orthographicSize,tacticalHeightPixels=(tacticalMax-tacticalMin)*Screen.height,
            topGapPixels=(1-client.BoardTop-tacticalMax)*Screen.height,bottomGapPixels=(tacticalMin-client.BoardBottom)*Screen.height,
            dockTopNormalized=dock.yMin/root.worldBound.height};
    }
    public static async Task<object> CheckWidthInvariant()
    {
        var rows=new List<object>();float? initialSize=null,initialRiver=null,initialDock=null;
        float sizeDrift=0,riverDrift=0,dockDrift=0;
        foreach(int width in new[]{390,768,912,1366})
        {
            SetSize(width,892);await Task.Delay(500);
            rows.Add(Measure());
            var client=UnityEngine.Object.FindAnyObjectByType<TowerDuelClient>();
            var root=client.GetComponent<UIDocument>().rootVisualElement.Q("duelRoot");
            float size=client.Camera.orthographicSize,river=client.Camera.WorldToViewportPoint(Vector3.zero).y;
            float dock=root.Q("dockFrame").worldBound.yMin/root.worldBound.height;
            if(!initialSize.HasValue){initialSize=size;initialRiver=river;initialDock=dock;}
            sizeDrift=Mathf.Max(sizeDrift,Mathf.Abs(size-initialSize.Value));
            riverDrift=Mathf.Max(riverDrift,Mathf.Abs(river-initialRiver.Value)*892);
            dockDrift=Mathf.Max(dockDrift,Mathf.Abs(dock-initialDock.Value)*892);
        }
        if(sizeDrift>.0001f||riverDrift>.1f||dockDrift>1.5f)throw new Exception("Width changed vertical framing");
        return new {passed=true,sizeDrift,riverDriftPixels=riverDrift,dockDriftPixels=dockDrift,rows};
    }
    private static bool Inside(Rect parent,Rect child)=>child.xMin>=parent.xMin-.5f&&child.xMax<=parent.xMax+.5f&&child.yMin>=parent.yMin-.5f&&child.yMax<=parent.yMax+.5f;
}
