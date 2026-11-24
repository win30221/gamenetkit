package main

import "testing"

func playableUnitCard(t *testing.T, config rules, hand [4]string) string {
	t.Helper()
	for _, id := range hand {
		card := config.ByID[id]
		if card.Count > 0 && card.HP > 0 && card.Cost <= 5 {
			return id
		}
	}
	t.Fatal("no deployable unit in hand")
	return ""
}

func TestStartShufflesSelectedDecks(t *testing.T) {
	r := defaultRules()
	m := testMap(t)
	deck, _ := r.deck(nil)
	var first [deckSize]string
	changed := false
	for n := 0; n < 32; n++ {
		w := newMatch("shuffle", "room", m, r)
		for seat := range w.Players {
			w.Players[seat].Connected = true
			w.setDeck(seat, deck)
		}
		w.ready(0)
		w.ready(1)
		for seat := range w.Players {
			var dealt [deckSize]string
			copy(dealt[:4], w.Hands[seat][:])
			copy(dealt[4:], w.Queues[seat][:])
			if n == 0 && seat == 0 {
				first = dealt
			} else if dealt != first {
				changed = true
			}
			allowed := make(map[string]bool, deckSize)
			for _, id := range deck {
				allowed[id] = true
			}
			for _, id := range dealt {
				if !allowed[id] {
					t.Fatalf("invalid shuffled deck: %v", dealt)
				}
				delete(allowed, id)
			}
			if w.Decks[seat] != deck {
				t.Fatal("selected deck was mutated")
			}
		}
		hands, queues := w.Hands, w.Queues
		w.ready(0)
		w.ready(1)
		if w.Hands != hands || w.Queues != queues {
			t.Fatal("duplicate ready reshuffled an existing match")
		}
	}
	if !changed {
		t.Fatal("all opening orders were identical")
	}
}
