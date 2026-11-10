package shared

import "testing"

func TestPracticeModeAllowlist(t *testing.T) {
	for _, mode := range []string{"idle", "move", "attack", "dodge", "attack+dodge", "move+attack", "move+dodge", "move+attack+dodge", "capture", "capture+attack", "capture+dodge", "capture+attack+dodge"} {
		if !IsPracticeMode(mode) {
			t.Fatalf("missing mode %q", mode)
		}
		if mode != "idle" {
			behavior, ok := PracticeBehaviors(mode + "+kite")
			if !ok || behavior&PracticeKeepDistance == 0 {
				t.Fatalf("missing spacing combination %q", mode)
			}
		}
	}
	if behavior, ok := PracticeBehaviors("kite"); !ok || behavior != PracticeKeepDistance {
		t.Fatal("missing standalone spacing")
	}
	for _, mode := range []string{"", "Idle", "patrol", " attack", "attack ", "shoot", "../attack", "capture+move", "attack+capture", "capture+attack+attack", "idle+dodge", "capture+dodge+attack", "capture+heal", "idle+kite", "kite+kite", "kite+attack", "capture+kite+dodge", "capture+attack+dodge+kite+kite"} {
		if IsPracticeMode(mode) {
			t.Fatalf("unexpected mode %q", mode)
		}
	}
}
