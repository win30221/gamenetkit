package main

import (
	"bufio"
	"bytes"
	"context"
	"crypto/sha256"
	"crypto/subtle"
	"crypto/tls"
	"crypto/x509"
	"encoding/base64"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"net/url"
	"os"
	"sync"
	"time"
	"unicode/utf8"

	webtransport "github.com/quic-go/webtransport-go"
)

const maxMessage = 32 * 1024
const maxLine = 4*((maxMessage+2)/3) + 4

func decode(text string) ([]byte, error) {
	data, err := base64.StdEncoding.DecodeString(text)
	if err != nil || len(data) > maxMessage || !utf8.Valid(data) {
		return nil, errors.New("invalid_text")
	}
	return data, nil
}

func pinnedTLS(hash string) (*tls.Config, error) {
	pin, err := hex.DecodeString(hash)
	if err != nil || len(pin) != sha256.Size {
		return nil, errors.New("invalid_certificate_hash")
	}
	return &tls.Config{
		MinVersion: tls.VersionTLS13,
		// Trust is an exact, explicitly supplied leaf pin rather than system roots.
		InsecureSkipVerify: true,
		VerifyPeerCertificate: func(raw [][]byte, _ [][]*x509.Certificate) error {
			if len(raw) == 0 {
				return errors.New("missing_certificate")
			}
			digest := sha256.Sum256(raw[0])
			if subtle.ConstantTimeCompare(digest[:], pin) != 1 {
				return errors.New("certificate_mismatch")
			}
			cert, err := x509.ParseCertificate(raw[0])
			if err != nil {
				return err
			}
			now := time.Now()
			if now.Before(cert.NotBefore) || now.After(cert.NotAfter) {
				return errors.New("certificate_expired")
			}
			return nil
		},
	}, nil
}

func run(parent context.Context, input io.Reader, output io.Writer) error {
	ctx, cancel := context.WithCancel(parent)
	defer cancel()
	scanner := bufio.NewScanner(input)
	scanner.Buffer(make([]byte, 4096), maxLine+1)
	line := func() (string, error) {
		if !scanner.Scan() {
			return "", io.ErrUnexpectedEOF
		}
		return scanner.Text(), nil
	}
	version, err := line()
	if err != nil || version != "RFWT1" {
		return errors.New("invalid_handshake")
	}
	address64, err := line()
	if err != nil {
		return err
	}
	addressBytes, err := decode(address64)
	if err != nil {
		return err
	}
	address, err := url.Parse(string(addressBytes))
	if err != nil || address.Scheme != "https" || address.Host == "" || address.User != nil || address.Fragment != "" {
		return errors.New("invalid_url")
	}
	hash, err := line()
	if err != nil {
		return err
	}
	tlsConfig, err := pinnedTLS(hash)
	if err != nil {
		return err
	}
	hello64, err := line()
	if err != nil {
		return err
	}
	hello, err := decode(hello64)
	if err != nil || bytes.ContainsAny(hello, "\r\n") {
		return errors.New("invalid_hello")
	}
	transport := &webtransport.Transport{TLSClientConfig: tlsConfig}
	defer transport.Close()
	connect, stop := context.WithTimeout(ctx, 8*time.Second)
	defer stop()
	response, session, err := transport.Dial(connect, address.String(), nil)
	if err != nil {
		if response != nil && response.Body != nil {
			response.Body.Close()
		}
		return errors.New("connection_closed")
	}
	defer session.CloseWithError(0, "")
	stream, err := session.OpenStreamSync(connect)
	if err != nil {
		return err
	}
	stream.SetWriteDeadline(time.Now().Add(8 * time.Second))
	if _, err = stream.Write(append(hello, '\n')); err != nil {
		return err
	}
	stream.SetWriteDeadline(time.Time{})
	var writing sync.Mutex
	emit := func(data []byte) error {
		if len(data) > maxMessage || !utf8.Valid(data) {
			return errors.New("invalid_server_message")
		}
		writing.Lock()
		defer writing.Unlock()
		_, err := fmt.Fprintln(output, "M "+base64.StdEncoding.EncodeToString(data))
		return err
	}
	failures := make(chan error, 3)
	go func() {
		for scanner.Scan() {
			text := scanner.Text()
			if len(text) < 2 || text[:2] != "D " {
				failures <- errors.New("invalid_command")
				return
			}
			data, err := decode(text[2:])
			if err == nil {
				err = session.SendDatagram(data)
			}
			if err != nil {
				failures <- errors.New("send_failed")
				return
			}
		}
		failures <- scanner.Err() // stdin EOF is an orderly parent shutdown
	}()
	go func() {
		reader := bufio.NewReaderSize(stream, maxMessage+2)
		count := 0
		for {
			data, err := reader.ReadSlice('\n')
			if err != nil {
				if err == io.EOF && len(data) == 0 && count > 0 {
					return // reliable welcome stream may finish while datagrams continue
				}
				failures <- errors.New("invalid_server_message")
				return
			}
			if err = emit(data[:len(data)-1]); err != nil {
				failures <- err
				return
			}
			count++
		}
	}()
	go func() {
		for {
			data, err := session.ReceiveDatagram(ctx)
			if err == nil {
				err = emit(data)
			}
			if err != nil {
				failures <- err
				return
			}
		}
	}()
	select {
	case err := <-failures:
		return err
	case <-ctx.Done():
		return ctx.Err()
	}
}

func main() {
	if err := run(context.Background(), os.Stdin, os.Stdout); err != nil {
		// Never echo URLs, credentials, application payloads or TLS internals.
		fmt.Fprintln(os.Stdout, "E connection_closed")
		os.Exit(1)
	}
}
