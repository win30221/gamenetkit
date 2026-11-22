package main

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"os"
	"regexp"
)

const protocolVersion = 3
const deckSize = 8

type cardDefinition struct {
	ID               string  `json:"id"`
	Name             string  `json:"name"`
	Description      string  `json:"description"`
	Cost             float64 `json:"cost"`
	Count            int     `json:"count"`
	HP               int     `json:"hp"`
	Damage           int     `json:"damage"`
	Cooldown         int     `json:"cooldown"`
	WindupTicks      int     `json:"windupTicks"`
	DeployTicks      int     `json:"deployTicks"`
	Speed            float64 `json:"speed"`
	Range            float64 `json:"range"`
	Aggro            float64 `json:"aggro"`
	Type             string  `json:"type"`
	Target           string  `json:"target"`
	Splash           float64 `json:"splash"`
	ChargeDistance   float64 `json:"chargeDistance"`
	ChargeMultiplier float64 `json:"chargeMultiplier"`
	Radius           float64 `json:"radius"`
	Slow             float64 `json:"slow"`
	TowerScale       float64 `json:"towerScale"`
	DelayTicks       int     `json:"delayTicks"`
	DurationTicks    int     `json:"durationTicks"`
}
type rules struct {
	ID                 string                    `json:"id"`
	Schema             int                       `json:"schema"`
	TowerHP            int                       `json:"towerHP"`
	KeepHP             int                       `json:"keepHP"`
	TowerRange         float64                   `json:"towerRange"`
	KeepRange          float64                   `json:"keepRange"`
	TowerDamage        int                       `json:"towerDamage"`
	TowerCooldownTicks int                       `json:"towerCooldownTicks"`
	Duration           int                       `json:"duration"`
	Cards              []cardDefinition          `json:"cards"`
	Hash               string                    `json:"-"`
	ByID               map[string]cardDefinition `json:"-"`
}

func loadRules(path string) (rules, error) {
	var r rules
	data, err := os.ReadFile(path)
	if err != nil {
		return r, err
	}
	if err = json.Unmarshal(data, &r); err != nil {
		return r, err
	}
	if r.ID == "" || r.Schema != protocolVersion || len(r.Cards) < deckSize || r.Duration != 180 || r.TowerHP <= 0 || r.KeepHP <= 0 || r.TowerDamage < 0 || r.TowerCooldownTicks < 1 {
		return r, errors.New("invalid rules")
	}
	if !finite(r.TowerRange) || r.TowerRange <= 0 || !finite(r.KeepRange) || r.KeepRange <= 0 {
		return r, errors.New("towerRange and keepRange must be positive finite distances")
	}
	r.ByID = make(map[string]cardDefinition)
	validID := regexp.MustCompile(`^[a-z][a-z0-9_]{0,23}$`)
	for _, c := range r.Cards {
		if !validID.MatchString(c.ID) || c.Name == "" || !finite(c.Cost) || c.Cost <= 0 || c.Cost > 10 || (c.Target != "any" && c.Target != "buildings") {
			return r, errors.New("invalid card")
		}
		if c.Type == "troop" {
			if c.DeployTicks < 0 || c.DeployTicks > 10*tickRate {
				return r, errors.New("invalid troop deployment time")
			}
			if (c.Count != 1 && c.Count != 3) || c.HP <= 0 || c.Damage <= 0 || c.Cooldown < 1 || c.WindupTicks < 1 || c.WindupTicks >= c.Cooldown || !finite(c.Speed) || c.Speed <= 0 || !finite(c.Range) || c.Range <= 0 || !finite(c.Aggro) || c.Aggro < c.Range || !finite(c.Splash) || c.Splash < 0 || c.Splash > 5 || !finite(c.ChargeDistance) || c.ChargeDistance < 0 || !finite(c.ChargeMultiplier) || c.ChargeMultiplier < 0 || c.ChargeMultiplier > 4 || (c.ChargeDistance > 0 && c.ChargeMultiplier < 1) {
				return r, errors.New("invalid troop")
			}
		} else if c.Type == "spell" {
			if c.Count != 0 || !finite(c.Radius) || c.Radius <= 0 || c.Radius > 5 || c.Damage < 0 || !finite(c.TowerScale) || c.TowerScale < 0 || c.TowerScale > 1 || !finite(c.Slow) || c.Slow < 0 || c.Slow >= 1 || (c.Damage == 0 && c.Slow == 0) || c.DelayTicks < 0 || c.DelayTicks > 90 || c.DurationTicks < 1 || c.DurationTicks > 300 {
				return r, errors.New("invalid spell")
			}
		} else {
			return r, errors.New("unsupported card type")
		}
		if _, ok := r.ByID[c.ID]; ok {
			return r, errors.New("duplicate card")
		}
		r.ByID[c.ID] = c
	}
	r.Duration *= tickRate
	h := sha256.Sum256(data)
	r.Hash = hex.EncodeToString(h[:])
	return r, nil
}

func (r rules) deck(ids []string) ([deckSize]string, bool) {
	var deck [deckSize]string
	if ids == nil {
		for i := range deck {
			deck[i] = r.Cards[i].ID
		}
		return deck, true
	}
	if len(ids) != len(deck) {
		return deck, false
	}
	seen := make(map[string]bool, deckSize)
	for i, id := range ids {
		if _, ok := r.ByID[id]; !ok || seen[id] {
			return deck, false
		}
		seen[id] = true
		deck[i] = id
	}
	return deck, true
}

func formation(seat int, x, z float64, count int) []point {
	if count == 1 {
		return []point{{x, z}}
	}
	direction := 1.0
	if seat == 1 {
		direction = -1
	}
	return []point{{x - .48, z - .30*direction}, {x + .48, z - .30*direction}, {x, z + .50*direction}}
}
