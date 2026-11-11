package main

import (
	"path/filepath"
	"testing"

	"google.golang.org/protobuf/proto"
)

func TestFixturePresenceAndRoundtrip(t *testing.T) {
	path := filepath.Join(t.TempDir(), "fixture.bin")
	if err := run(path, ""); err != nil {
		t.Fatal(err)
	}
	if err := run("", path); err != nil {
		t.Fatal(err)
	}
	m := fixture()
	m.OptionalValue = nil
	data, _ := proto.Marshal(m)
	if err := verify(data); err == nil {
		t.Fatal("absent optional zero accepted")
	}
}
