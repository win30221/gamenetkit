using System;
using UnityEngine;
using Rumblebound.Gameplay;
namespace Rumblebound.Presentation
{
    public static class HeroRules
    {
        public static bool IsGround(HeroDefinition hero) { return hero?.attackKind == "ground"; }
        public static bool IsMelee(HeroDefinition hero) { return hero?.attackKind == "melee"; }

        public static double RangedReach(Rules rules, HeroDefinition hero)
        { return rules.playerRadius + rules.projectileRadius + .05 + hero.projectileSpeed * hero.projectileLifetimeTicks / rules.tickRate; }

        public static HeroDefinition Find(Rules rules, string heroID)
        {
            if (rules?.heroes == null) return null;
            foreach (var hero in rules.heroes) if (hero != null && hero.heroID == heroID) return hero;
            return null;
        }

        public static Rules ForHero(Rules source, string heroID)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = JsonUtility.FromJson<Rules>(JsonUtility.ToJson(source));
            var hero = Find(source, heroID);
            if (hero == null) return copy;
            copy.maxHP = hero.maxHP; copy.speed = hero.speed;
            copy.projectileDamage = hero.projectileDamage; copy.projectileSpeed = hero.projectileSpeed;
            copy.projectileLifetimeTicks = hero.projectileLifetimeTicks; copy.fireCooldownTicks = hero.fireCooldownTicks;
            copy.dashSpeed = hero.dashSpeed; copy.dashCooldownTicks = hero.dashCooldownTicks;
            return copy;
        }
    }

}
