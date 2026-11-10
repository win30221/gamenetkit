package transport

import (
	"context"
	"errors"
	"github.com/coder/websocket"
	"io"
	"time"
)

type Wire interface {
	ReadHello(context.Context) ([]byte, error)
	ReadInput(context.Context) ([]byte, error)
	Write(context.Context, []byte, bool) error
	Reject(context.Context, []byte) error
	Close()
}
type Limits struct{ Input, Datagram, Snapshot int }

func (l Limits) Reliable(size int, requested bool) (bool, error) {
	if l.Datagram <= 0 || l.Snapshot < l.Datagram {
		return false, errors.New("invalid_wire_limits")
	}
	if size > l.Snapshot {
		return false, errors.New("snapshot_exceeds_stream_budget")
	}
	return requested || size > l.Datagram, nil
}

type WebSocket struct{ Conn *websocket.Conn }

func (w *WebSocket) ReadHello(ctx context.Context) ([]byte, error) { return w.ReadInput(ctx) }
func (w *WebSocket) ReadInput(ctx context.Context) ([]byte, error) {
	kind, data, err := w.Conn.Read(ctx)
	if err == nil && kind != websocket.MessageText {
		return nil, errors.New("text_required")
	}
	return data, err
}
func (w *WebSocket) Write(ctx context.Context, data []byte, _ bool) error {
	return w.Conn.Write(ctx, websocket.MessageText, data)
}
func (w *WebSocket) Reject(ctx context.Context, data []byte) error { return w.Write(ctx, data, true) }
func (w *WebSocket) Close()                                        { _ = w.Conn.CloseNow() }

func BoundedDatagram(ctx context.Context, abort func(), send func() error) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	stop := context.AfterFunc(ctx, abort)
	err := send()
	stop()
	if ctx.Err() != nil {
		return ctx.Err()
	}
	return err
}

type DeadlineWriter interface {
	SetWriteDeadline(time.Time) error
	Write([]byte) (int, error)
}

func WriteLine(ctx context.Context, stream DeadlineWriter, data []byte) error {
	deadline, ok := ctx.Deadline()
	if !ok {
		deadline = time.Now().Add(time.Second)
	}
	if err := stream.SetWriteDeadline(deadline); err != nil {
		return err
	}
	payload := append(append([]byte(nil), data...), '\n')
	n, err := stream.Write(payload)
	if err == nil && n != len(payload) {
		return io.ErrShortWrite
	}
	return err
}
