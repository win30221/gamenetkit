package main

import "math"

// buffStat identifies independent, multiplicative combat attributes. New
// abilities can reuse these stats without adding fields to unit or match.
type buffStat string

const (
	buffMoveSpeed   buffStat = "move_speed"
	buffAttackSpeed buffStat = "attack_speed"
	buffDamageDealt buffStat = "damage_dealt"
	buffDamageTaken buffStat = "damage_taken"
)

type buffScope uint8

const (
	buffTimed buffScope = iota // persists until expiry, even if its source dies
	buffZone                   // removed immediately if no longer in the zone
)

// A stable key allows independent sources and abilities to refresh without
// duplicating entries. Scope distinguishes timed effects from area effects.
type buffKey struct {
	Stat     buffStat
	SourceID uint64
	Kind     string
	Scope    buffScope
}

type buffEffect struct {
	Multiplier float64
	ExpiresAt  uint64 // exclusive authoritative simulation tick
}

type buffSet struct {
	active map[buffKey]buffEffect
}

func (b *buffSet) apply(key buffKey, multiplier float64, expiresAt, tick uint64) {
	if multiplier <= 0 || math.IsNaN(multiplier) || math.IsInf(multiplier, 0) || expiresAt <= tick {
		return
	}
	if b.active == nil {
		b.active = make(map[buffKey]buffEffect)
	}
	b.active[key] = buffEffect{Multiplier: multiplier, ExpiresAt: expiresAt}
}

func (b *buffSet) expire(tick uint64) {
	for key, effect := range b.active {
		if tick >= effect.ExpiresAt {
			delete(b.active, key)
		}
	}
}

func (b *buffSet) clearScope(scope buffScope) {
	for key := range b.active {
		if key.Scope == scope {
			delete(b.active, key)
		}
	}
}

// Strongest increase and strongest decrease combine multiplicatively.
// Same-direction effects never stack; weaker effects remain eligible if the
// strongest expires. Different stats are resolved independently.
func (b *buffSet) strongest(stat buffStat, tick uint64) (increase, decrease float64) {
	increase, decrease = 1, 1
	for key, effect := range b.active {
		if key.Stat != stat || tick >= effect.ExpiresAt {
			continue
		}
		increase = max(increase, effect.Multiplier)
		decrease = min(decrease, effect.Multiplier)
	}
	return
}

func (b *buffSet) factor(stat buffStat, tick uint64) float64 {
	increase, decrease := b.strongest(stat, tick)
	return increase * decrease
}

func (b *buffSet) hasPenalty(stat buffStat, tick uint64) bool {
	_, decrease := b.strongest(stat, tick)
	return decrease < 1
}

func scaledDamage(amount int, multiplier float64) int {
	return int(math.Round(float64(amount) * multiplier))
}
