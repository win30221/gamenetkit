// Command replaydemo records the live queued engine for offline replay verification.
package main

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/protocol"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

func run(args []string, stdout, stderr io.Writer) int {
	flags := flag.NewFlagSet("replaydemo", flag.ContinueOnError)
	flags.SetOutput(stderr)
	directory := flags.String("dir", "artifacts/replays", "directory for the recorded journal")
	seconds := flags.Int("seconds", 5, "recording duration in seconds (1-30)")
	if err := flags.Parse(args); err != nil {
		return 2
	}
	if *directory == "" || *seconds < 1 || *seconds > 30 || flags.NArg() != 0 {
		fmt.Fprintln(stderr, "usage: replaydemo -dir DIRECTORY [-seconds 1..30]")
		return 2
	}
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	ctx, err := sim.WithReplayDirectory(ctx, *directory)
	if err != nil {
		fmt.Fprintln(stderr, err)
		return 1
	}
	// Export a complete map and two actors so the same journal can exercise
	// Unity playback, including its map hash, match and roster validation.
	arena := shared.ArenaMap()
	arena.MapID = "replay-demo"
	arena.Obstacles = arena.Obstacles[:1]
	for i := range arena.SpawnLayouts {
		if arena.SpawnLayouts[i].TeamCount == 2 {
			arena.SpawnLayouts[i].Anchors = []shared.Point{{X: -2}, {X: 2}}
		}
	}
	const user = "replay-demo-player"
	const observer = "replay-demo-observer"
	engine, err := sim.NewMatchEngineWithMap(ctx, "replay-demo", map[string]string{user: "blue", observer: "red"}, map[string]string{user: "ranger", observer: "mage"}, arena)
	if err != nil {
		fmt.Fprintln(stderr, err)
		return 1
	}
	defer func() { cancel(); <-engine.Done }()
	status := engine.ReplayStatus()
	if !status.Enabled || status.Error != "" {
		fmt.Fprintln(stderr, "recording unavailable:", status.Error)
		return 1
	}
	deadline, stop := context.WithTimeout(context.Background(), time.Duration(*seconds+5)*time.Second)
	defer stop()
	connection, err := engine.Attach(deadline, user, false)
	if err != nil {
		fmt.Fprintln(stderr, err)
		return 1
	}
	go func() {
		for range connection.Updates {
		}
	}()
	other, err := engine.Attach(deadline, observer, false)
	if err != nil {
		fmt.Fprintln(stderr, err)
		return 1
	}
	go func() {
		for range other.Updates {
		}
	}()
	generation := connection.Welcome.Generation
	engine.Loaded(user, generation)
	engine.Loaded(observer, other.Welcome.Generation)
	select {
	case <-engine.Started:
	case <-deadline.Done():
		fmt.Fprintln(stderr, deadline.Err())
		return 1
	}
	engine.Input(user, generation, protocol.Client{Generation: generation, Seq: 1, X: 1, Z: .25})
	timer := time.NewTimer(time.Duration(*seconds) * time.Second)
	defer timer.Stop()
	select {
	case <-timer.C:
	case <-deadline.Done():
		fmt.Fprintln(stderr, deadline.Err())
		return 1
	case <-engine.Done:
		fmt.Fprintln(stderr, "engine stopped before the recording finished")
		return 1
	}
	engine.Disconnect(user, generation)
	engine.Disconnect(observer, other.Welcome.Generation)
	// Inspect is an owner-queue barrier: the input and disconnect must execute
	// before cancellation closes the asynchronous journal writer.
	if _, err = engine.InspectPlayer(deadline, user); err != nil {
		fmt.Fprintln(stderr, err)
		return 1
	}
	cancel()
	<-engine.Done
	status = engine.ReplayStatus()
	if !status.Complete || status.Error != "" {
		fmt.Fprintln(stderr, "recording incomplete:", status.Error)
		return 1
	}
	report := sim.VerifyReplay(status.Path)
	result := struct {
		File   string           `json:"file"`
		Report sim.ReplayReport `json:"report"`
	}{status.Path, report}
	if err = json.NewEncoder(stdout).Encode(result); err != nil {
		fmt.Fprintln(stderr, err)
		return 1
	}
	if report.Status != "PASS" {
		return 1
	}
	return 0
}

func main() { os.Exit(run(os.Args[1:], os.Stdout, os.Stderr)) }
