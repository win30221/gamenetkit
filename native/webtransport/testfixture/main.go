package main

import (
	"encoding/json"
	"github.com/win30221/gamenetkit/native/webtransport/internal/fixture"
	"io"
	"os"
)

func main() {
	server, err := fixture.New()
	if err != nil {
		os.Exit(1)
	}
	defer server.Close()
	json.NewEncoder(os.Stdout).Encode(server)
	io.Copy(io.Discard, os.Stdin)
}
