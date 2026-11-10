package shared

import (
	"math"
	"reflect"
	"testing"
)

func TestRulesValidationRejectsUnsafeDefinitions(t *testing.T) {
	if err := ValidateConfig(Rules()); err != nil {
		t.Fatalf("embedded rules: %v", err)
	}
	for name, change := range map[string]func(*Config){
		"zero tick rate":        func(c *Config) { c.TickRate = 0 },
		"zero snapshot rate":    func(c *Config) { c.SnapshotRate = 0 },
		"snapshot above tick":   func(c *Config) { c.SnapshotRate = c.TickRate + 1 },
		"nondivisible snapshot": func(c *Config) { c.SnapshotRate = 7 },
		"zero rotation":         func(c *Config) { c.PointRotationTicks = 0 },
		"warning underflow":     func(c *Config) { c.PointWarningTicks = c.PointRotationTicks },
		"empty points":          func(c *Config) { c.ControlPoints = nil },
		"nonfinite point":       func(c *Config) { c.ControlPoints[0].X = math.NaN() },
		"point outside arena":   func(c *Config) { c.ControlPoints[0].Z = c.ArenaHalfSize },
		"nonfinite speed":       func(c *Config) { c.Speed = math.Inf(1) },
		"oversized roster":      func(c *Config) { c.MaxPlayers = 9 },
		"oversized projectiles": func(c *Config) { c.MaxProjectiles = 25 },
		"empty heroes":          func(c *Config) { c.Heroes = nil },
		"duplicate hero":        func(c *Config) { c.Heroes[1].HeroID = c.Heroes[0].HeroID },
		"invalid hero speed":    func(c *Config) { c.Heroes[0].Speed = math.NaN() },
		"zero hero cooldown":    func(c *Config) { c.Heroes[0].FireCooldownTicks = 0 },
		"long attack stop":      func(c *Config) { c.Heroes[0].AttackStopTicks = c.Heroes[0].FireCooldownTicks + 1 },
		"invalid attack kind":   func(c *Config) { c.Heroes[1].AttackKind = "unknown" },
		"zero melee range":      func(c *Config) { c.Heroes[1].MeleeRange = 0 },
		"nonfinite melee arc":   func(c *Config) { c.Heroes[1].MeleeArcDegrees = math.NaN() },
		"wide melee arc":        func(c *Config) { c.Heroes[1].MeleeArcDegrees = 181 },
		"zero melee damage":     func(c *Config) { c.Heroes[1].MeleeDamage = 0 },
		"short hero dash":       func(c *Config) { c.Heroes[0].DashCooldownTicks = c.DashTicks - 1 },
		"zero mass":             func(c *Config) { c.Collision.BodyMass = 0 },
		"unbounded iterations":  func(c *Config) { c.Collision.Iterations = 33 },
		"empty content":         func(c *Config) { c.Content = "" },
	} {
		t.Run(name, func(t *testing.T) {
			c := Rules()
			change(&c)
			if ValidateConfig(c) == nil {
				t.Fatal("unsafe rules accepted")
			}
		})
	}
}

func TestCachedDefinitionsAreIndependentAcrossConcurrentWorlds(t *testing.T) {
	wantRules, wantMap := Rules(), ArenaMap()
	for i := 0; i < 16; i++ {
		t.Run(string(rune('a'+i)), func(t *testing.T) {
			t.Parallel()
			c, m := Rules(), ArenaMap()
			c.Heroes[0].MaxHP++
			c.ControlPoints[0].X++
			m.Obstacles[0].X++
			m.ControlPoints[0].Z++
			m.SpawnLayouts[0].Anchors[0].X++
			if !reflect.DeepEqual(Rules(), wantRules) || !reflect.DeepEqual(ArenaMap(), wantMap) {
				t.Fatal("caller mutated the cached definition")
			}
		})
	}
}
