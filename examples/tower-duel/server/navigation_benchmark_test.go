package main

import (
	"testing"
	"time"
)

func BenchmarkCrowdedNavigation(b *testing.B) {
	var slowest time.Duration
	for range b.N {
		b.StopTimer()
		m, err := loadMap("../protocol/map.json")
		if err != nil {
			b.Fatal(err)
		}
		w := newMatch("benchmark", "BENCH1", m, defaultRules())
		w.Phase = "playing"
		w.Rules.TowerDamage = 0
		for key, card := range w.Rules.ByID {
			card.Damage = 0
			w.Rules.ByID[key] = card
		}
		for seat := range 2 {
			count, action := 0, uint64(0)
			for _, z := range []float64{-13, -11, -9, -7, -5, -3} {
				if seat == 1 {
					z = -z
				}
				for _, x := range []float64{-8, -6, -4, -2, 0, 2, 4, 6, 8} {
					if count == maxUnits/2 {
						break
					}
					kind := "swords"
					if count+3 > maxUnits/2 {
						kind = "guard"
					}
					w.Energy[seat] = 10
					action++
					w.Hands[seat] = [4]string{kind, "archer", "bomber", "frost"}
					if a := w.deploy(seat, w.ID, action, kind, x, z); a.Accepted {
						count += a.Count
					}
				}
			}
		}
		if len(w.Units) != maxUnits {
			b.Fatalf("got %d units", len(w.Units))
		}
		b.StartTimer()
		for range 300 {
			start := time.Now()
			w.step()
			if elapsed := time.Since(start); elapsed > slowest {
				slowest = elapsed
			}
		}
	}
	b.ReportMetric(float64(slowest.Nanoseconds())/1e6, "max-ms/tick")
}
