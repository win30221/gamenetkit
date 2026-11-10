package main

import (
	"bufio"
	"context"
	"encoding/base64"
	"fmt"
	"github.com/win30221/gamenetkit/native/webtransport/internal/fixture"
	"io"
	"strings"
	"testing"
	"time"
)

func TestPinnedHTTP3StreamsDatagramsAndParentEOF(t *testing.T) {
	server, err := fixture.New()
	if err != nil {
		t.Fatal(err)
	}
	defer server.Close()
	in, write := io.Pipe()
	read, out := io.Pipe()
	defer in.Close()
	defer write.Close()
	defer read.Close()
	defer out.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	done := make(chan error, 1)
	go func() { done <- run(ctx, in, out) }()
	go func() {
		fmt.Fprintf(write, "RFWT1\n%s\n%s\n%s\n", base64.StdEncoding.EncodeToString([]byte(server.URL)), server.Hash, base64.StdEncoding.EncodeToString([]byte("hello")))
	}()
	scanner := bufio.NewScanner(read)
	if !scanner.Scan() {
		t.Fatal("missing reliable welcome")
	}
	data, err := decode(strings.TrimPrefix(scanner.Text(), "M "))
	if err != nil || string(data) != "reliable:hello" {
		t.Fatalf("welcome %q %v", data, err)
	}
	go fmt.Fprintln(write, "D "+base64.StdEncoding.EncodeToString([]byte("datagram 雪")))
	if !scanner.Scan() {
		t.Fatal("missing datagram")
	}
	data, err = decode(strings.TrimPrefix(scanner.Text(), "M "))
	if err != nil || string(data) != "datagram 雪" {
		t.Fatalf("datagram %q %v", data, err)
	}
	write.Close()
	select {
	case err := <-done:
		if err != nil {
			t.Fatal(err)
		}
	case <-ctx.Done():
		t.Fatal("parent EOF did not terminate")
	}
}

func TestPinAndTextValidation(t *testing.T) {
	if _, err := pinnedTLS("bad"); err == nil {
		t.Fatal("invalid pin")
	}
	config, err := pinnedTLS(strings.Repeat("00", 32))
	if err != nil {
		t.Fatal(err)
	}
	server, err := fixture.New()
	if err != nil {
		t.Fatal(err)
	}
	defer server.Close()
	if err = config.VerifyPeerCertificate([][]byte{[]byte("not the pinned cert")}, nil); err == nil {
		t.Fatal("wrong pin accepted")
	}
	for _, text := range []string{base64.StdEncoding.EncodeToString([]byte(strings.Repeat("x", maxMessage+1))), "!", base64.StdEncoding.EncodeToString([]byte{0xff})} {
		if _, err = decode(text); err == nil {
			t.Fatal("invalid helper text")
		}
	}
	hello := "RFWT1\n" + base64.StdEncoding.EncodeToString([]byte(server.URL)) + "\n" + strings.Repeat("00", 32) + "\n" + base64.StdEncoding.EncodeToString([]byte("hello")) + "\n"
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err = run(ctx, strings.NewReader(hello), io.Discard); err == nil {
		t.Fatal("wrong TLS pin accepted")
	}
}
