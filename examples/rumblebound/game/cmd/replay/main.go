// Command replay verifies a recorded simulation and optionally exports a Unity clip.
package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

func run(args []string, stdout, stderr io.Writer) int {
	flags := flag.NewFlagSet("replay", flag.ContinueOnError)
	flags.SetOutput(stderr)
	file := flags.String("file", "", "recorded .replay.jsonl file to verify")
	export := flags.String("export", "", "optional output path for a verified Unity playback clip")
	if err := flags.Parse(args); err != nil {
		return 2
	}
	if *file == "" || flags.NArg() != 0 {
		fmt.Fprintln(stderr, "usage: replay -file recording.replay.jsonl [-export playback.json]")
		return 2
	}
	var report sim.ReplayReport
	if *export == "" {
		report = sim.VerifyReplay(*file)
	} else {
		report = sim.ExportPlayback(*file, *export)
	}
	if err := json.NewEncoder(stdout).Encode(report); err != nil {
		fmt.Fprintln(stderr, err)
		return 1
	}
	if report.Status != "PASS" {
		return 1
	}
	return 0
}

func main() { os.Exit(run(os.Args[1:], os.Stdout, os.Stderr)) }
