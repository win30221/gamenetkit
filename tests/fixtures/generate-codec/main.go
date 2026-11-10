// Command generate-codec creates the Go encoding vectors consumed by Unity SDK
// tests and the C# codec lesson. Run from the repository root with:
//
//	go -C ./server run ../tests/fixtures/generate-codec/main.go
//
// This is a test-data generator, not a game server.
package main

import (
	"encoding/json"
	"flag"
	"github.com/win30221/gamenetkit/server/codec"
	"github.com/win30221/gamenetkit/server/netcore"
	"os"
	"strings"
)

func main() {
	out := flag.String("out", "../tests/fixtures/codec.json", "output JSON path, relative to the current working directory")
	flag.Parse()
	e := codec.New()
	var cases []struct {
		Frame    netcore.EncodedFrame `json:"frame"`
		Expected string               `json:"expected"`
	}
	for i, text := range []string{`{"objects":[{"id":"one","x":1.001}],"label":"框架測試"}`, `{"objects":[{"id":"one","x":1.002}],"label":"框架測試"}`, `{"objects":[],"label":"框架測試"}`, strings.Repeat("ABCD", 4000), strings.Repeat("ABCD", 3999) + "TAIL"} {
		ack := uint64(i)
		f, err := e.Encode([]byte(text), ack)
		if err != nil {
			panic(err)
		}
		cases = append(cases, struct {
			Frame    netcore.EncodedFrame `json:"frame"`
			Expected string               `json:"expected"`
		}{f, text})
	}
	data, _ := json.MarshalIndent(map[string]any{"cases": cases}, "", "  ")
	if err := os.WriteFile(*out, data, 0644); err != nil {
		panic(err)
	}
}
