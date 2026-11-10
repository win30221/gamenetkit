package launcher

import (
	"bufio"
	"context"
	"errors"
	"flag"
	"log/slog"
	"net"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"syscall"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/devcert"
	"github.com/win30221/gamenetkit/examples/rumblebound/server/battle"
)

func run(ctx context.Context, environment, address, origins, replay, tlsAddress, certDirectory string) error {
	host, _, err := net.SplitHostPort(address)
	if err != nil || net.ParseIP(host) == nil || !net.ParseIP(host).IsLoopback() {
		return errors.New("development HTTP requires a literal loopback address")
	}
	if err := validateEnvironment(environment, origins, tlsAddress); err != nil {
		return err
	}
	app, err := battle.New(ctx, battle.Options{Origins: strings.Split(origins, ","), DevelopmentKey: os.Getenv("RB_DEV_KEY"), ReplayDirectory: replay, GoogleClientID: os.Getenv("RB_GOOGLE_CLIENT_ID")})
	if err != nil {
		return err
	}
	defer app.Close()
	listenerErrors := make(chan error, 2)
	if tlsAddress != "" {
		host, _, err := net.SplitHostPort(tlsAddress)
		if err != nil || net.ParseIP(host) == nil || !net.ParseIP(host).IsLoopback() {
			return errors.New("development WebTransport requires a literal loopback address")
		}
		if _, err = devcert.Generate(certDirectory); err != nil {
			return err
		}
		wt, err := app.WebTransport(tlsAddress, filepath.Join(certDirectory, "cert.pem"), filepath.Join(certDirectory, "key.pem"), "https://"+tlsAddress+"/wt")
		if err != nil {
			return err
		}
		defer wt.Close()
		go func() { listenerErrors <- wt.ListenAndServe() }()
	}
	handler := app.Handler
	if environment == "vm" {
		handler = proxyRequests(handler)
	}
	server := &http.Server{Addr: address, Handler: handler, ReadHeaderTimeout: 5 * time.Second, IdleTimeout: 30 * time.Second, MaxHeaderBytes: 8192}
	stopped := make(chan struct{})
	defer close(stopped)
	go func() {
		select {
		case <-ctx.Done():
			shutdown, stop := context.WithTimeout(context.Background(), 5*time.Second)
			defer stop()
			if server.Shutdown(shutdown) != nil {
				server.Close()
			}
		case <-stopped:
		}
	}()
	slog.Info("Rumblebound remake battle API", "address", address)
	go func() { listenerErrors <- server.ListenAndServe() }()
	select {
	case <-ctx.Done():
		shutdown, stop := context.WithTimeout(context.Background(), 5*time.Second)
		defer stop()
		return server.Shutdown(shutdown)
	case err = <-listenerErrors:
		server.Close()
	}
	if errors.Is(err, http.ErrServerClosed) {
		return nil
	}
	return err
}
func Main() {
	environment := flag.String("environment", "local", "local or vm (loopback API behind the HTTPS proxy)")
	address := flag.String("http", "127.0.0.1:18179", "private development API address")
	origins := flag.String("origins", "http://127.0.0.1:18093,http://localhost:18093", "exact public browser origins")
	replay := flag.String("replay-dir", "", "optional match journal directory")
	tlsAddress := flag.String("wt", "", "optional loopback HTTP/3 address, e.g. 127.0.0.1:18493")
	certDirectory := flag.String("cert-dir", ".local/tls", "development certificate directory; refreshed on WebTransport startup")
	stopOnStdin := flag.Bool("stop-on-stdin", false, "owned launcher requests graceful shutdown with stop or EOF on stdin")
	flag.Parse()
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	if *stopOnStdin {
		go func() {
			reader := bufio.NewScanner(os.Stdin)
			reader.Buffer(make([]byte, 64), 64)
			for reader.Scan() {
				if reader.Text() == "stop" {
					break
				}
			}
			stop()
		}()
	}
	if err := run(ctx, *environment, *address, *origins, *replay, *tlsAddress, *certDirectory); err != nil {
		slog.Error("battle API stopped", "error", err)
		os.Exit(1)
	}
}
