package shared

import "testing"

func TestHeroCatalogKeepsRangerBaselineAndTankTradeoffs(t *testing.T) {
	c := Rules()
	seen := map[string]bool{}
	for _, hero := range c.Heroes {
		if hero.HeroID == "" || seen[hero.HeroID] || hero.MaxHP <= 0 || hero.Speed <= 0 ||
			(hero.AttackKind == "ranged" && (hero.ProjectileDamage <= 0 || hero.ProjectileSpeed <= 0 || hero.ProjectileLifetimeTicks == 0)) ||
			hero.FireCooldownTicks == 0 || hero.DashSpeed <= 0 || hero.DashCooldownTicks == 0 {
			t.Fatalf("invalid hero definition: %+v", hero)
		}
		seen[hero.HeroID] = true
	}
	r, ok := c.Hero("ranger")
	if !ok || r.MaxHP != c.MaxHP || r.Speed != c.Speed || r.ProjectileDamage != c.ProjectileDamage ||
		r.ProjectileSpeed != c.ProjectileSpeed || r.ProjectileLifetimeTicks != c.ProjectileLifetimeTicks ||
		r.FireCooldownTicks != c.FireCooldownTicks || r.DashSpeed != c.DashSpeed || r.DashCooldownTicks != c.DashCooldownTicks {
		t.Fatal("ranger no longer matches the existing baseline")
	}
	tank, ok := c.Hero("tank")
	if !ok || tank.MaxHP <= r.MaxHP || tank.Speed >= r.Speed || tank.FireCooldownTicks <= r.FireCooldownTicks ||
		tank.ProjectileSpeed*float64(tank.ProjectileLifetimeTicks) >= r.ProjectileSpeed*float64(r.ProjectileLifetimeTicks) {
		t.Fatal("tank lost its durable, slower, short-range tradeoff")
	}
	if _, ok := c.Hero("unknown"); ok {
		t.Fatal("unknown hero accepted")
	}
}
