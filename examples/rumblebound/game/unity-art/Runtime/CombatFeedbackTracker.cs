using Rumblebound.Gameplay;
using System;
using System.Collections.Generic;

namespace Rumblebound.Presentation
{
    // Snapshot baselines prevent a reconnect or stalled receive queue from
    // replaying old shots. Impact IDs are deduplicated by ProjectilePresentation.
    public sealed class CombatFeedbackTracker
    {
        private readonly Dictionary<string, long> fireTicks = new Dictionary<string, long>();
        private readonly List<PlayerState> shots = new List<PlayerState>(8);
        private readonly List<CombatEvent> meleeEvents = new List<CombatEvent>(32);
        private readonly Dictionary<string, long> seenMelee = new Dictionary<string, long>();
        private readonly List<string> expiredMelee = new List<string>();
        private long previousTick, silentThroughTick;
        private double previousTime;
        private string matchID;
        private bool initialized;
        public IReadOnlyList<PlayerState> Shots => shots;
        public IReadOnlyList<CombatEvent> MeleeEvents => meleeEvents;

        public void Accept(Snapshot snapshot, double now, int tickRate, Rules rules = null)
        {
            shots.Clear();
            meleeEvents.Clear();
            bool fresh = initialized && snapshot.matchID == matchID && snapshot.type != "welcome" && snapshot.type != "start" &&
                snapshot.tick > previousTick && snapshot.tick - previousTick <= Math.Max(1, tickRate / 2) &&
                now >= previousTime && now - previousTime <= .5;
            if (!fresh) { silentThroughTick = snapshot.tick; seenMelee.Clear(); }
            if (snapshot.players != null)
                foreach (var player in snapshot.players)
                {
                    if (player == null || string.IsNullOrEmpty(player.playerID)) continue;
                    if (fresh && player.online && !player.left && player.hp > 0 &&
                        fireTicks.TryGetValue(player.playerID, out long previous) &&
                        player.fireReadyTick > previous && player.fireReadyTick > snapshot.tick &&
                        (rules == null || HeroRules.Find(rules, player.heroID)?.attackKind == "ranged"))
                        shots.Add(player);
                }
            fireTicks.Clear();
            if (snapshot.players != null)
                foreach (var player in snapshot.players)
                    if (player != null && !string.IsNullOrEmpty(player.playerID)) fireTicks[player.playerID] = player.fireReadyTick;
            // Events survive several snapshots so a lost snapshot cannot lose a
            // hit. Deduplicate their IDs, while a new/stalled stream establishes
            // a silent baseline instead of replaying retained melee attacks.
            expiredMelee.Clear();
            foreach (var seen in seenMelee)
                if (snapshot.tick - seen.Value > 6) expiredMelee.Add(seen.Key);
            foreach (string id in expiredMelee) seenMelee.Remove(id);
            if (snapshot.combatEvents != null)
                foreach (var combat in snapshot.combatEvents)
                {
                    if (combat == null || string.IsNullOrEmpty(combat.id) || string.IsNullOrEmpty(combat.ownerID) ||
                        (combat.kind != "melee_swing" && combat.kind != "melee_hit" && combat.kind != "basic_swing" && combat.kind != "basic_hit") ||
                        combat.tick > snapshot.tick || snapshot.tick - combat.tick > 6 ||
                        double.IsNaN(combat.x) || double.IsInfinity(combat.x) ||
                        double.IsNaN(combat.z) || double.IsInfinity(combat.z) ||
                        seenMelee.ContainsKey(combat.id) || seenMelee.Count >= 64) continue;
                    seenMelee[combat.id] = combat.tick;
                    if (fresh && combat.tick > silentThroughTick) meleeEvents.Add(combat);
                }
            previousTick = snapshot.tick; previousTime = now; matchID = snapshot.matchID; initialized = true;
        }

        public bool CanPresent(ProjectileEnd ending)
        {
            return initialized && ending != null && ending.tick > silentThroughTick &&
                (ending.kind == "wall" || ending.kind == "boundary" || ending.kind == "player");
        }

        public void Clear()
        {
            initialized = false; shots.Clear(); fireTicks.Clear(); meleeEvents.Clear(); seenMelee.Clear(); expiredMelee.Clear();
            matchID = null; previousTick = silentThroughTick = 0;
        }
    }
}
