using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rumblebound.Example.Battle.Unity
{
    // The original game's menu hierarchy and shared USS classes, backed by the example's GameFlow.
    public sealed partial class BattleHud
    {
        private bool lobbyPractice, roomManagementOpen, pendingResultLeave;
        private int teamCount = 2;
        private string hero = "ranger", bot = "ranger", code = "", copiedRoomCode;
        private string practiceMode = "idle";

        private static string MenuTeamName(string id)
        {
            switch (id) { case "t1": return "藍隊"; case "t2": return "橘隊"; case "t3": return "紫隊"; case "t4": return "黃隊"; default: return id ?? "未選隊伍"; }
        }

        private static string ShortMemberID(string id) => string.IsNullOrEmpty(id) ? "" : id.Substring(0, Math.Min(8, id.Length));
        private bool MenuAvailable => !Flow.Busy && !Flow.Recovery.Pending;

        private void MenuNotice(VisualElement parent)
        {
            string message = Message(Flow.Notice);
            if (!string.IsNullOrEmpty(message)) parent.Add(Text(message, "status"));
        }

        private bool MenuCatalog(VisualElement parent)
        {
            if (Flow.Content != null) return true;
            parent.Add(Text("角色資料尚未載入。", "hint"));
            parent.Add(Button(Flow.Busy ? "正在載入角色…" : "重新載入角色資料", () => Flow.ReloadContent(), "secondary small-button", MenuAvailable));
            return false;
        }

        private void BuildLogin(VisualElement parent, VisualElement dock)
        {
            parent.Add(Text("歡迎來到競技場", "title"));
            parent.Add(Text("直接進入大廳，建立或加入朋友的房間。", "muted"));
            parent.Add(Text("開發測試版", "badge"));
            var guest = Button(Flow.Busy ? "正在進入…" : "訪客遊玩", () => Flow.Guest(), null, MenuAvailable && Flow.Content != null);
            guest.name = "guestButton";
            parent.Add(guest);
            parent.Add(Text("訪客身份僅供本次測試，進度不會永久保存。", "hint"));
            if (Flow.Content == null) MenuCatalog(parent);
            MenuNotice(parent);
        }

        private void BuildLobby(VisualElement parent, VisualElement dock)
        {
            pendingResultLeave = false;
            parent.Add(Text("遊戲大廳", "title"));
            parent.Add(Text("與朋友對戰，或先進入單人練習。", "muted"));
            var tabs = Row();
            tabs.AddToClassList("lobby-tabs");
            var friends = Button("朋友對戰", () => lobbyPractice = false, (lobbyPractice ? "secondary " : "") + "grow");
            friends.name = "friendsTab"; tabs.Add(friends);
            var practice = Button("單人練習", () => lobbyPractice = true, (lobbyPractice ? "" : "secondary ") + "grow");
            practice.name = "practiceTab"; tabs.Add(practice);
            parent.Add(tabs);
            MenuCatalog(parent);
            if (lobbyPractice)
            {
                BuildPracticeSetup(parent, dock);
                MenuNotice(parent);
                return;
            }
            parent.Add(Text("建立房間邀請朋友，或輸入朋友提供的代碼。", "hint"));
            var options = Box("lobby-options");
            parent.Add(options);
            var create = Box("card lobby-option");
            create.Add(Text("建立私人房間", "section-title"));
            var count = Row();
            for (int i = 2; i <= 4; i++)
            {
                int value = i;
                var option = Choice(i + " 隊", i == teamCount, () => teamCount = value);
                option.name = "teamCount-" + i;
                count.Add(option);
            }
            create.Add(count);
            create.Add(Button("建立房間", () => Flow.Create(teamCount), null, MenuAvailable && Flow.Content != null));
            options.Add(create);
            var join = Box("card lobby-option");
            join.Add(Text("加入朋友的房間", "section-title"));
            var field = Field("房間代碼", code, value => code = value, 12);
            field.AddToClassList("room-code-input");
            join.Add(field);
#if UNITY_WEBGL && !UNITY_EDITOR
            field.isReadOnly=TouchMode;
            field.RegisterCallback<PointerDownEvent>(evt=>{
                if(!TouchMode&&evt.pointerType!=UnityEngine.UIElements.PointerType.touch)return;
                field.isReadOnly=true;EditRoomCode();evt.StopPropagation();
            },TrickleDown.TrickleDown);
#endif
            var joinButton = Button("加入房間", () => Flow.Join(code), null, MenuAvailable && !string.IsNullOrWhiteSpace(code));
            field.RegisterValueChangedCallback(evt => joinButton.SetEnabled(MenuAvailable && !string.IsNullOrWhiteSpace(evt.newValue)));
            join.Add(joinButton);
            options.Add(join);
            MenuNotice(parent);
            parent.Add(Text(TouchMode?"左側移動 · 右側普攻／技能／衝刺":"右鍵移動／普攻 · Q 技能 · E 衝刺 · 空白回正鏡頭", "hint"));
        }

        private void BuildRoom(VisualElement parent, VisualElement dock)
        {
            var room = Flow.Room;
            if (room == null) { BuildLobby(parent, dock); return; }
            if (pendingResultLeave && !room.started && !Flow.Busy)
            {
                pendingResultLeave = false;
                Flow.Leave();
                MarkDirty();
            }
            if (room.practice)
            {
                parent.Add(Text(room.started ? "單人練習中" : "練習結束", "title"));
                parent.Add(Text(Flow.Recovery.Pending ? "正在恢復練習角色…" : "正在進入練習競技場…", "muted"));
                parent.Add(Button("進入／重連練習", () => Flow.Enter(), null, MenuAvailable && !Flow.Battle.Connected));
                parent.Add(Button("離開練習", () => Flow.Leave(), "secondary", !Flow.Busy));
                MenuNotice(parent);
                return;
            }
            parent.Add(Text(room.started ? room.phase == "loading" ? "準備進入競技場" : "比賽進行中" : "集結隊伍", "title"));
            parent.Add(Text("房間代碼", "hint"));
            var copy = Button(room.code + "   複製", () => CopyRoomCode(room.code), "secondary room-code");
            copy.name = "roomCopyButton"; parent.Add(copy);
            var copyStatus = Text(copiedRoomCode == room.code ? "已複製房間代碼" : "", "hint");
            copyStatus.style.display = copyStatus.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            copyStatus.name = "roomCopyStatus"; parent.Add(copyStatus);
            parent.Add(Text(room.hostID == room.youID ? "你是房主，分享代碼邀請朋友。" : "加入隊伍後，按準備即可等待房主開賽。", "muted"));
            if (room.started)
            {
                parent.Add(Text(room.phase == "loading" ? "你的角色已載入，等待全員就緒…" : Flow.Recovery.Pending ? "連線中斷，正在恢復原角色…" : Flow.Busy ? "正在連線與載入角色…" : "全員載入後，比賽才會開始計時。", "section-title"));
                parent.Add(Text("載入最多等待 45 秒。逾時後會回到房間重新準備。", "hint"));
                parent.Add(Button(Flow.Recovery.Pending ? "正在重新連線…" : "進入／重連對戰", () => Flow.Enter(), null, MenuAvailable && !Flow.Battle.Connected));
                if (Flow.Battle.Connected || Flow.Recovery.Pending) parent.Add(Button("取消連線", () => Flow.StopBattle(), "secondary"));
                MenuNotice(parent);
                return;
            }
            var self = room.members.FirstOrDefault(member => member.userID == room.youID);
            parent.Add(Text("1 選擇隊伍　→　2 選擇角色　→　3 準備開戰", "room-steps"));
            parent.Add(Text("1 · 選擇隊伍", "section-title"));
            var grid = Box("team-grid");
            parent.Add(grid);
            foreach (var team in room.teams)
            {
                var current = team;
                int occupancy = room.members.Count(member => member.teamID == team.teamID);
                var card = Box("team-card");
                Color color = TeamColor(current.teamID);
                card.style.borderTopColor = color;
                var heading = Text(MenuTeamName(current.teamID), "team-name");
                heading.style.color = color; card.Add(heading);
                card.Add(Text(occupancy + " / " + current.capacity + " 位玩家", "hint"));
                var join = Button(self != null && self.teamID == current.teamID ? "已加入" : "加入這一隊", () => Flow.Team(current.teamID), "secondary small-button", MenuAvailable && (self == null || self.teamID != current.teamID) && occupancy < current.capacity);
                join.name = "joinTeam-" + current.teamID; card.Add(join);
                foreach (var member in room.members.Where(member => member.teamID == current.teamID))
                {
                    string name = member.userID == room.youID ? "你" : member.bot ? "練習對手" : "玩家 " + ShortMemberID(member.userID);
                    var row = Row(Text(name + " · " + HeroName(member.heroID) + (member.ready ? " · 已準備" : " · 等待中"), "member-name"));
                    row.AddToClassList("member-row"); card.Add(row);
                }
                if (room.hostID == room.youID)
                {
                    var less = Button("−", () => Flow.Capacity(current.teamID, current.capacity - 1), "secondary small-button", MenuAvailable && current.capacity > Math.Max(1, occupancy));
                    var more = Button("＋", () => Flow.Capacity(current.teamID, current.capacity + 1), "secondary small-button", MenuAvailable && current.capacity < 4);
                    card.Add(Row(Text("隊伍容量", "hint"), less, more));
                }
                grid.Add(card);
            }
            BuildMenuHeroSelection(parent, self?.heroID);
            var footer = Box("footer");
            var actions = Row(); footer.Add(actions);
            bool hasTeam = self != null && !string.IsNullOrEmpty(self.teamID);
            var ready = Button(self?.ready == true ? "取消準備" : "我準備好了", () => Flow.Ready(self?.ready != true), "grow", hasTeam && MenuAvailable && Flow.Content != null);
            ready.name = "roomReadyButton"; actions.Add(ready);
            if (room.hostID == room.youID) actions.Add(Button("開始對戰", () => Flow.Start(), "grow", room.canStart && MenuAvailable && !Flow.Battle.Connected));
            actions.Add(Button("離開房間", () => Flow.Leave(), "secondary small-button", !Flow.Busy));
            footer.Add(Text(!hasTeam ? "先選擇隊伍，再選角色並按準備。" : room.canStart ? "全員準備完成，等待房主開賽。" : self.ready ? "你已準備，等待其他玩家。" : "選好角色後按準備；至少兩隊有人且全員準備即可開賽。", "hint"));
            MenuNotice(footer);
            var management = new Foldout { text = "房間管理", value = roomManagementOpen, name = "roomManagement" };
            management.RegisterValueChangedCallback(evt => { if (evt.target == management) roomManagementOpen = evt.newValue; });
            if (hasTeam) management.Add(Button("離開目前隊伍", () => Flow.Team(""), "secondary small-button", MenuAvailable));
            if (room.hostID == room.youID)
                foreach (var member in room.members.Where(member => member.userID != room.youID && !member.bot))
                {
                    var target = member;
                    string next = NextAvailableTeam(room, target.teamID);
                    management.Add(Button("移動玩家 " + ShortMemberID(target.userID) + " 到下一隊", () => Flow.Assign(target.userID, next), "secondary small-button", MenuAvailable && next != null));
                }
            parent.Add(management); dock.Add(footer);
        }

        private static string NextAvailableTeam(GameRoom room, string current)
        {
            int start = Array.FindIndex(room.teams, team => team.teamID == current);
            for (int offset = 1; offset <= room.teams.Length; offset++)
            {
                var team = room.teams[(start + offset + room.teams.Length) % room.teams.Length];
                if (team.teamID != current && room.members.Count(member => member.teamID == team.teamID) < team.capacity) return team.teamID;
            }
            return null;
        }

        private void BuildMenuHeroSelection(VisualElement parent, string selected)
        {
            parent.Add(Text("2 · 選擇角色", "section-title"));
            if (!MenuCatalog(parent)) return;
            parent.Add(Text("更換角色後，需要重新準備。", "hint"));
            var choices = Box("hero-options");
            foreach (string id in new[] { "tank", "ranger", "mage" })
            {
                string heroID = id;
                var definition = Flow.Content.Hero(id);
                if (definition == null) continue;
                bool chosen = selected == id;
                var card = Box("hero-option" + (chosen ? " selected" : ""));
                var choose = Button((chosen ? "已選擇" : "選擇") + HeroName(id), () => Flow.Hero(heroID), (chosen ? "" : "secondary ") + "small-button", !chosen && MenuAvailable);
                choose.name = "roomHero-" + id; card.Add(choose);
                bool ground = definition.attackKind == "ground", melee = definition.attackKind == "melee";
                card.Add(Text(ground ? "指定落點・範圍火球" : melee ? "耐打・劍盾近戰" : "靈活・弓箭遠程", "hint"));
                var rules = Flow.Content.Rules;
                double range = ground ? definition.castRange : melee ? definition.meleeRange : rules.playerRadius + rules.projectileRadius + .05 + definition.projectileSpeed * definition.projectileLifetimeTicks / rules.tickRate;
                string area = ground ? " · 爆炸半徑 " + definition.impactRadius.ToString("0.#") : melee ? " · 扇形 " + definition.meleeArcDegrees.ToString("0.#") + "°" : "";
                card.Add(Text("普攻 " + definition.basicDamage + " · 距離 " + definition.basicRange.ToString("0.#") + " · CD " + (definition.basicCooldownTicks / (double)rules.tickRate).ToString("0.##") + " 秒", "hint"));
                card.Add(Text("生命 " + definition.maxHP + " · 移速 " + definition.speed.ToString("0.#") +
                    "\n傷害 " + (ground ? definition.groundDamage : melee ? definition.meleeDamage : definition.projectileDamage) +
                    (ground ? " · 施法距離 " : melee ? " · 近戰距離 " : " · 射程 ") + range.ToString("0.#") +
                    "\n技能 CD " + (definition.fireCooldownTicks / (double)rules.tickRate).ToString("0.##") + " 秒" + area, "hint"));
                choices.Add(card);
            }
            parent.Add(choices);
        }

        private void PracticeHeroes(VisualElement parent, string title, bool opponent)
        {
            parent.Add(Text(title, "hint"));
            var row = Row();
            foreach (string id in new[] { "tank", "ranger", "mage" })
            {
                string selected = id;
                var choice = Choice(HeroName(id), (opponent ? bot : hero) == id, () => { if (opponent) bot = selected; else hero = selected; });
                choice.name = (opponent ? "practiceBotHero-" : "practiceHero-") + id;
                choice.SetEnabled(MenuAvailable && Flow.Content?.Hero(id) != null);
                row.Add(choice);
            }
            parent.Add(row);
        }

        private void BuildPracticeSetup(VisualElement parent, VisualElement dock)
        {
            var card = Box("card");
            card.Add(Text("單人練習", "section-title"));
            card.Add(Text("加入練習對手，一個人也能測試近戰、射箭、落火球、碰撞與推擠。", "hint"));
            PracticeHeroes(card, "你的角色", false);
            PracticeHeroes(card, "對手角色", true);
            BuildPracticeControls(card, false);
            var start = Button(Flow.Busy ? "正在準備練習…" : "開始練習", () => Flow.Practice(hero, bot, practiceMode), null, MenuAvailable && Flow.Content != null);
            start.name = "practiceStartButton";
            var footer = Box("footer"); footer.Add(start); dock.Add(footer);
            parent.Add(card);
        }

        private static bool HasPracticeBehavior(string mode, string behavior) => ("+" + mode + "+").Contains("+" + behavior + "+");

        private static string PracticeModeName(string mode)
        {
            string name = HasPracticeBehavior(mode, "capture") ? "佔領據點" : HasPracticeBehavior(mode, "move") ? "巡邏" : "";
            foreach (var behavior in new[] { "attack", "dodge", "kite" })
                if (HasPracticeBehavior(mode, behavior)) name += (name.Length > 0 ? "＋" : "") + (behavior == "attack" ? "攻擊" : behavior == "dodge" ? "閃避" : "保持距離");
            return name.Length > 0 ? name : "站立";
        }

        private void BuildPracticeControls(VisualElement parent, bool live)
        {
            parent.Add(Text("對手行為", "section-title"));
            string selected = live ? Flow.Room?.botMode ?? "idle" : practiceMode;
            string movement = HasPracticeBehavior(selected, "capture") ? "capture" : HasPracticeBehavior(selected, "move") ? "move" : "idle";
            bool attack = HasPracticeBehavior(selected, "attack"), dodge = HasPracticeBehavior(selected, "dodge"), kite = HasPracticeBehavior(selected, "kite");
            var controls = Box("practice-behaviors"); parent.Add(controls);
            Action apply = () => {
                string mode = movement == "idle" ? "" : movement;
                foreach (string value in new[] { attack ? "attack" : "", dodge ? "dodge" : "", kite ? "kite" : "" })
                    if (value.Length > 0) mode += (mode.Length > 0 ? "+" : "") + value;
                if (mode.Length == 0) mode = "idle";
                if (live) { controls.SetEnabled(false); Flow.PracticeMode(mode); MarkDirty(); }
                else practiceMode = mode;
            };
            var row = Row(); controls.Add(row);
            foreach (string value in new[] { "idle", "move", "capture" })
            {
                string option = value;
                var choice = Choice(value == "idle" ? "待命" : value == "move" ? "巡邏" : "搶佔據點", movement == value, () => { movement = option; apply(); });
                choice.name = "practiceMovement-" + value; row.Add(choice);
            }
            var attacking = new Toggle { text = "主動攻擊", value = attack, name = "practiceAttack" };
            attacking.RegisterValueChangedCallback(evt => { attack = evt.newValue; apply(); }); controls.Add(attacking);
            var dodging = new Toggle { text = "閃避子彈與落地火球", value = dodge, name = "practiceDodge" };
            dodging.RegisterValueChangedCallback(evt => { dodge = evt.newValue; apply(); }); controls.Add(dodging);
            var spacing = new Toggle { text = "保持距離（過近時退開）", value = kite, name = "practiceKeepDistance" };
            spacing.RegisterValueChangedCallback(evt => { kite = evt.newValue; apply(); }); controls.Add(spacing);
            controls.SetEnabled(!Flow.Busy);
            parent.Add(Text("搶點＋攻擊會接近據點內的敵人，對手離開後回守；待命＋攻擊會追擊。保持距離會在過近時退開。", "hint"));
            if (live) parent.Add(Text(Flow.Busy ? "正在套用行為…" : "目前：" + PracticeModeName(Flow.Room?.botMode ?? "idle"), "hint"));
        }

        private void BuildResult(VisualElement parent, VisualElement dock)
        {
            var state = Flow.Battle.State.Latest;
            var result = state?.match?.result;
            if (result == null) return;
            var own = state.players.FirstOrDefault(player => player.playerID == Flow.Battle.State.PlayerID);
            string ownTeam = own?.teamID;
            string heading = result.outcome == "draw" ? "勢均力敵" : result.outcome == "win" ? result.winnerTeamID == ownTeam ? "勝利！" : "下次再戰！" : "對戰結束";
            parent.Add(Text(heading, "result-title"));
            parent.Add(Text(result.outcome == "win" ? MenuTeamName(result.winnerTeamID) + "贏得對戰" : "感謝參與這場對戰", "muted"));
            parent.Add(Text(result.reason == "score" ? "率先達到目標分數" : result.reason == "time" ? "比賽時間結束" : result.reason == "forfeit" ? "其他隊伍已離場" : result.reason == "no_teams" ? "無有效隊伍，比賽中止" : "對戰結束", "hint"));
            foreach (var score in state.match.scores ?? Array.Empty<Rumblebound.Gameplay.TeamScore>())
            {
                var row = Row(Text(MenuTeamName(score.teamID) + (score.teamID == ownTeam ? " · 你" : "")), Text((score.scoreTicks / (double)Math.Max(1, Flow.Content?.Rules.tickRate ?? 30)).ToString("F2") + " 分"));
                row.AddToClassList("result-row"); row.style.color = TeamColor(score.teamID); parent.Add(row);
            }
            bool practice = Flow.Room?.practice == true;
            var back = Button(practice ? "返回練習設定" : "返回房間", () => { CloseMenu(); if (practice) { hero = own?.heroID ?? hero; bot = Flow.Room?.members?.FirstOrDefault(member => member.bot)?.heroID ?? bot; practiceMode = Flow.Room?.botMode ?? practiceMode; lobbyPractice = true; Flow.Leave(); } else Flow.ReturnToRoom(); }, "grow", !Flow.Busy);
            var leave = Button(practice ? "離開練習" : "離開房間", () => {
                CloseMenu();
                if (practice) Flow.Leave();
                else { pendingResultLeave = true; if (!Flow.ReturnToRoom()) pendingResultLeave = false; }
            }, "secondary grow", !Flow.Busy);
            parent.Add(Row(back, leave));
            MenuNotice(parent);
        }
    }
}
