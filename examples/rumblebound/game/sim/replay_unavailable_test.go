//go:build rf_no_replay

package sim

import (
	"context"
	"testing"
)

func TestReplayRemovedHasExplicitDiagnosticAndDisabledFallback(t *testing.T) {
	if _, e := WithReplayDirectory(context.Background(), t.TempDir()); e == nil {
		t.Fatal("missing recorder silently accepted")
	}
	ctx, e := WithReplayDirectory(context.Background(), "")
	if e != nil {
		t.Fatal(e)
	}
	w := New()
	sink, e := w.startReplay(ctx)
	if sink != nil || e != nil {
		t.Fatal("disabled recorder requires optional package")
	}
	if r := VerifyReplay("unused"); r.Error != "replay_module_not_installed" {
		t.Fatal(r)
	}
}
