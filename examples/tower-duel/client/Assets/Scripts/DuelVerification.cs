using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TowerDuel
{
    // Opt-in development Player driver. Uses the same session/deployment entry points as UI.
    public sealed class DuelVerification : IDisposable
    {
        [Serializable] private sealed class Report
        {
            public string room, match, phase, status, role, reason, resultTitle;
            public string mapID;
            public bool environment2D,ownBuildingsAway,mudObserved;
            public int mudSyncErrors,mudMoveSamples;
            public float mudMoveRatioTotal;
            public int seat, winner, accepted, rejected, frames, unitCount, unitsRemoved;
            public float uiWidth, uiHeight;
            public int cardsUsed,models,walkPose,attackPose,deathPose,walkKinds,attackKinds,deaths,arrows,previewCards;
            public int sprites,b3Cards,b3Frames;
            public int spriteShadows;
            public int visibleTeamDiscs;
            public float shadowX,shadowY;
            public bool shadowsLowerLeft;
            public int completedRounds,resumes,spellsSeen,maxUnits,snapshotBytes;
            public float frameP95MS;
            public string[] hand,completedMatches;
            public long tick;
            public bool crossed, damagedTower, damagedUnit, ended;
            public bool wholeFieldVisible;
            public bool tacticalContentVisible,numericHealth,fractionalEnergy,fullEnergyReminder;
            public bool previewsCleared=true;
            public EntityState[] towers;
        }
        private readonly TowerDuelClient client;
        private readonly string path, role;
        private readonly Report report = new Report();
        private readonly VisualElement ui;
        private HashSet<long> previousUnits=new HashSet<long>();
        private double nextReport, nextDeploy;
        private bool captured, endCaptured, connected,b3Captured;
        private readonly int rounds;
        private readonly bool exerciseRecovery;
        private bool recoveryTriggered;
        private string currentMatch;
        private readonly List<string> completed=new List<string>();
        private readonly List<float> frameTimes=new List<float>();
        private int acceptedObserved;
        private readonly Dictionary<long,Vector3> mudPositions=new();
        private readonly Dictionary<long,long> mudTicks=new();
        private readonly HashSet<long> muddyBefore=new();
        public DuelVerification(TowerDuelClient client)
        {
            this.client = client;
            path = TowerDuelClient.Argument("-td-report", "");
            role = TowerDuelClient.Argument("-td-role", "observer"); report.role = role;
            rounds=int.TryParse(TowerDuelClient.Argument("-td-rounds","1"),out int n)?Mathf.Clamp(n,1,3):1;
            exerciseRecovery=TowerDuelClient.Argument("-td-recover","")=="1";
            ui = client.GetComponent<UIDocument>().rootVisualElement;
        }
        public void Update()
        {
            report.frames++;
            report.uiWidth=ui.resolvedStyle.width;report.uiHeight=ui.resolvedStyle.height;
            report.wholeFieldVisible=true;foreach(float x in new[]{-9.68f,9.68f})foreach(float z in new[]{-14.68f,14.68f}){var p=client.Camera.WorldToViewportPoint(new Vector3(x,0,z));if(p.x<0||p.x>1||p.y<client.BoardBottom||p.y>1-client.BoardTop)report.wholeFieldVisible=false;}
            report.tacticalContentVisible=CombatViewVisible();
            ui.Query<Label>(className:"health-amount").ForEach(label=>{if(label.text.Contains(" / "))report.numericHealth=true;});
            ui.Query<VisualElement>(className:"energy-pip-fill").ForEach(fill=>{
                float fraction=fill.resolvedStyle.width/Mathf.Max(1,fill.parent.contentRect.width);
                if(fraction>.05f&&fraction<.95f)report.fractionalEnergy=true;
            });
            var energyRow=ui.Q("energyRow");
            if(energyRow.ClassListContains("energy-full")&&ui.Q<Label>("energyMode").text.StartsWith("能量已滿"))report.fullEnergyReminder=true;
            report.sprites|=client.Presentation.SpriteKinds;
            report.spriteShadows|=client.Presentation.SpriteShadowKinds;
            report.visibleTeamDiscs=Mathf.Max(report.visibleTeamDiscs,client.Presentation.VisibleTeamDiscs);
            var shadowDirection=DuelLighting.ScreenShadow(client.Camera);
            report.shadowX=shadowDirection.x;report.shadowY=shadowDirection.y;
            report.shadowsLowerLeft=shadowDirection.x<-.1f&&shadowDirection.y<-.1f;
            for(int i=0;i<client.Rules.cards.Length;i++)
            {
                var button=ui.Q<Button>(client.Rules.cards[i].id+"Card");
                if(button!=null&&button.ClassListContains("b3-card"))
                {
                    report.b3Cards|=1<<i;
                    if(button.Q(className:"b3-card-frame")!=null)report.b3Frames|=1<<i;
                }
            }
            var presentation=client.Presentation;report.models|=presentation.RenderedKinds;report.walkPose=presentation.WalkPoseSamples;report.attackPose=presentation.AttackPoseSamples;report.deathPose=presentation.DeathPoseSamples;report.deaths=presentation.DeathCount;report.arrows=presentation.ArrowsFired;report.walkKinds|=presentation.WalkKinds;report.attackKinds|=presentation.AttackKinds;report.spellsSeen|=presentation.SpellsSeen;
            if(client.Session.Accepted>acceptedObserved){for(int i=0;i<client.Rules.cards.Length;i++)if(client.Rules.cards[i].id==client.Session.LastPlayedCard)report.cardsUsed|=1<<i;acceptedObserved=client.Session.Accepted;}
            if (!connected && report.uiWidth>100 && report.uiHeight>100)
            {
                connected = true;
                ui.Q<TextField>("serverField").value=client.Server;
                ui.Q<TextField>("nameField").value=role;
                ui.Q<TextField>("roomField").value=TowerDuelClient.Argument("-td-room", "");
                Click(TowerDuelClient.Argument("-td-auto", "create")=="create"?"createButton":"joinButton");
            }
            var state = client.Session.State;
            report.status = client.Session.Status;
            if (state != null)
            {
                if(currentMatch!=state.match){currentMatch=state.match;acceptedObserved=0;previousUnits.Clear();captured=endCaptured=b3Captured=false;}
                report.room = state.room; report.match = state.match; report.phase = state.phase;
                report.seat = state.seat; report.tick = state.tick; report.winner = state.winner;
                report.mapID=client.Map.id;
                var environment=client.Presentation.Environment;
                report.environment2D=environment!=null&&environment.BuildingCount==6;
                report.ownBuildingsAway=report.environment2D&&environment.ViewSeat==state.seat;
                if(report.ownBuildingsAway)foreach(var tower in state.towers)
                    report.ownBuildingsAway&=environment.BuildingFrame(tower.id)%2==(tower.seat==state.seat?0:1);
                foreach(var unit in state.units)
                {
                    if(unit.concealed)continue;
                    bool mud=client.Map.TerrainMovementFactor(unit.Position)<1;
                    if(mud!=unit.muddy)report.mudSyncErrors++;
                    report.mudObserved|=mud&&unit.muddy;
                    if(mudTicks.TryGetValue(unit.id,out long tick)&&tick<state.tick&&mud&&muddyBefore.Contains(unit.id)&&!unit.IsDeploying(state.tick))
                    {
                        float travel=Vector3.Distance(unit.Position,mudPositions[unit.id]);
                        if(travel>.001f){report.mudMoveRatioTotal+=travel/((state.tick-tick)/30f*client.Rules.Find(unit.kind).speed);report.mudMoveSamples++;}
                    }
                    if(!mudTicks.TryGetValue(unit.id,out long previous)||previous<state.tick)
                    {mudTicks[unit.id]=state.tick;mudPositions[unit.id]=unit.Position;if(mud)muddyBefore.Add(unit.id);else muddyBefore.Remove(unit.id);}
                }
                report.accepted = client.Session.Accepted; report.rejected = client.Session.Rejected;
                report.unitCount = state.units.Length; report.towers = state.towers;
                if(state.phase=="ended"&&!completed.Contains(state.match)){completed.Add(state.match);report.completedRounds=completed.Count;report.completedMatches=completed.ToArray();}
                report.ended = state.phase == "ended"&&completed.Count>=rounds; report.reason = state.reason;
                report.hand=state.hand;report.resumes=client.Session.Resumes;report.snapshotBytes=Mathf.Max(report.snapshotBytes,client.Session.LastSnapshotBytes);report.maxUnits=Mathf.Max(report.maxUnits,state.units.Length);
                report.resultTitle=ui.Q<Label>("resultTitle").text;
                if (state.phase == "waiting" && !state.players[state.seat].ready) Click("readyButton");
                if(state.phase=="ended"&&completed.Count<rounds)Click("rematchButton");
                if(exerciseRecovery&&!recoveryTriggered&&client.Session.Accepted>=2&&!client.Session.Pending){recoveryTriggered=true;client.Session.SimulateDisconnect();}
                foreach (var unit in state.units) if ((unit.seat == 0 && unit.z > 1.9f) || (unit.seat == 1 && unit.z < -1.9f)) report.crossed = true;
                var currentUnits=new HashSet<long>();
                foreach(var unit in state.units){currentUnits.Add(unit.id);if(unit.hp<unit.maxHP)report.damagedUnit=true;}
                foreach(long id in previousUnits)if(!currentUnits.Contains(id))report.unitsRemoved++;
                previousUnits=currentUnits;
                foreach (var tower in state.towers) if (tower.hp < tower.maxHP) report.damagedTower = true;
                if(!b3Captured&&state.phase=="playing"&&report.frames>300)
                    foreach(string id in state.hand)if(DuelSpriteLibrary.Converted(id)){Capture("b3-cards");b3Captured=true;break;}
                if ((role == "attacker" || (role=="defender" && client.Session.Accepted<8)) && client.Session.CanAct && (state.phase == "playing" || state.phase == "overtime") && !client.Session.Pending && DuelSession.Now >= nextDeploy)
                {
                    string card=client.Rules.cards[client.Session.Accepted%8].id;
                    if(client.Session.InHand(card)&&state.energy>=client.Rules.Find(card).cost)
                    {
                    float x=card=="swords"?-5:-5+(report.accepted%2==0?-.38f:.38f);
                    if(role=="attacker"&&report.accepted==0&&TowerDuelClient.Argument("-td-terrain-audit","")=="1")x=5;
                    float z=card=="archer"||card=="bomber"?4.2f:3.2f;
                    var position=new Vector3(x,0,state.seat==0?-z:z);
                    if(client.Rules.Find(card).IsSpell){position=new Vector3(-5,0,state.seat==0?7.7f:-7.7f);foreach(var unit in state.units)if(unit.seat!=state.seat){position=unit.Position;break;}}
                    DeployThroughUI(position,card);
                    nextDeploy = DuelSession.Now + .4;
                    }
                }
                if (!captured && report.crossed && report.damagedTower && state.units.Length >= 2)
                {
                    Capture("playing"); captured = true;
                }
                if (report.ended && !endCaptured) { Capture("ended"); endCaptured = true; }
                if(state.phase=="playing"&&report.frames>300){frameTimes.Add(Time.unscaledDeltaTime*1000);if(frameTimes.Count>1200)frameTimes.RemoveAt(0);}
            }
            if (DuelSession.Now >= nextReport) { Save(); nextReport = DuelSession.Now + 1; }
        }
        private void Click(string name)
        {
            var button=ui.Q<Button>(name);
            if(!button.enabledInHierarchy)return;
            button.Focus();
            using(var submit=NavigationSubmitEvent.GetPooled())button.SendEvent(submit);
        }
        private bool CombatViewVisible()
        {
            foreach(var tower in client.Map.towers)
            {
                foreach(float x in new[]{tower.x-1.7f,tower.x+1.7f})
                    if(!InCombatView(new Vector3(x,0,tower.z)))return false;
                if(!InCombatView(new Vector3(tower.x,tower.kind=="keep"?TowerDuelClient.KeepHealthHeight:client.Map.HasWall?7.4f:3.9f,tower.z)))return false;
                if(tower.kind=="keep"&&!InCombatView(new Vector3(tower.x,0,tower.z+Mathf.Sign(tower.z)*2.1f)))return false;
            }
            foreach(float bridge in client.Map.bridges)foreach(float z in new[]{-2.5f,2.5f})
                if(!InCombatView(new Vector3(bridge,0,z)))return false;
            return true;
        }
        private bool InCombatView(Vector3 point)
        {
            var p=client.Camera.WorldToViewportPoint(point);
            return p.z>0&&p.x>=0&&p.x<=1&&p.y>=client.BoardBottom&&p.y<=1-client.BoardTop;
        }
        private void DeployThroughUI(Vector3 position,string card)
        {
            if(!client.Selected||client.SelectedCard!=card)Click(card+"Card");
            if(!client.Selected)return;
            Vector3 screen=client.Camera.WorldToScreenPoint(position);
            var world=ui.Q("worldInput");
            Vector2 point=RuntimePanelUtils.ScreenToPanel(world.panel,new Vector2(screen.x,Screen.height-screen.y));
            using(var down=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,mousePosition=point,button=0,clickCount=1}))world.SendEvent(down);
            var definition=client.Rules.Find(card);var preview=client.Presentation.DeploymentPreview;
            if(preview.ActiveGhosts==(definition.IsSpell?0:definition.count)&&Mathf.Abs(preview.ActiveRadius-(definition.IsSpell?definition.radius:0))<.001f)
                for(int i=0;i<client.Rules.cards.Length;i++)if(client.Rules.cards[i].id==card)report.previewCards|=1<<i;
            using(var up=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,mousePosition=point,button=0,clickCount=1}))world.SendEvent(up);
            report.previewsCleared&=preview.ActiveGhosts==0&&preview.ActiveRadius==0;
        }
        private string ImagePath(string suffix) => Path.ChangeExtension(path, null) + "-" + suffix + ".png";
        private void Capture(string suffix) { if (path != "" && TowerDuelClient.Argument("-td-capture", "")=="1") ScreenCapture.CaptureScreenshot(ImagePath(suffix)); }
        private void Save()
        {
            if (path == "") return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            if(frameTimes.Count>0){var sorted=frameTimes.ToArray();Array.Sort(sorted);report.frameP95MS=sorted[Mathf.Min(sorted.Length-1,(int)(sorted.Length*.95f))];}
            File.WriteAllText(temporary, JsonUtility.ToJson(report, true));
            File.Copy(temporary, path, true); File.Delete(temporary);
        }
        public void Dispose() => Save();
    }
}
