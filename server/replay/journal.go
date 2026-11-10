// Package replay records opaque simulation events as a checksummed JSONL file.
// A completed file has a mandatory footer. Crashes, queue overflow and storage
// errors leave a .partial file which verification will never accept as complete.
package replay

import (
	"bufio"
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"hash"
	"io"
	"os"
	"path/filepath"
	"strings"
	"sync"

	"github.com/win30221/gamenetkit/server/netcore"
)

const Version = 1

type Limits struct {
	Queue, RecordBytes int
	FileBytes          int64
	Entries            uint64
}

func DefaultLimits() Limits {
	return Limits{Queue: 1024, RecordBytes: 64 * 1024, FileBytes: 256 * 1024 * 1024, Entries: 1000000}
}
func (l Limits) valid() bool {
	return l.Queue > 0 && l.Queue <= 4096 && l.RecordBytes >= 1024 && l.RecordBytes <= 1024*1024 && l.FileBytes >= int64(l.RecordBytes) && l.FileBytes <= 1024*1024*1024 && l.Entries > 0 && l.Entries <= 1000000
}

type record struct {
	Type   string                 `json:"type"`
	Header *netcore.ReplayHeader  `json:"header,omitempty"`
	Entry  *netcore.ReplayEntry   `json:"entry,omitempty"`
	Footer *netcore.ReplaySummary `json:"footer,omitempty"`
}
type queuedRecord struct {
	data []byte
	tick uint64
	kind string
}
type Journal struct {
	mu            sync.Mutex
	closed        bool
	err           error
	path, partial string
	limits        Limits
	queue         chan queuedRecord
	done          chan struct{}
	next          uint64
	bytes         int64
}

func validHash(s string) bool {
	b, e := hex.DecodeString(s)
	return e == nil && len(b) == sha256.Size && s == hex.EncodeToString(b)
}
func validHeader(h netcore.ReplayHeader) bool {
	return h.Version == Version && len(h.Application) > 0 && len(h.Application) <= 128 && len(h.Model) > 0 && len(h.Model) <= 128 && json.Valid(h.Definition) && validHash(h.InitialHash)
}
func line(v record, limit int) ([]byte, error) {
	b, e := json.Marshal(v)
	if e != nil {
		return nil, e
	}
	if len(b)+1 > limit {
		return nil, errors.New("replay_record_limit")
	}
	return append(b, '\n'), nil
}

// New creates a new recording, never overwriting an existing file. The parent
// directory is provisioned by the application. Only Close publishes finalPath.
func New(finalPath string, h netcore.ReplayHeader, l Limits) (*Journal, error) {
	if !l.valid() || !validHeader(h) {
		return nil, errors.New("invalid_replay_configuration")
	}
	path, e := filepath.Abs(finalPath)
	if e != nil {
		return nil, e
	}
	if strings.HasSuffix(path, ".partial") {
		return nil, errors.New("invalid_replay_destination")
	}
	if _, e = os.Lstat(path); !errors.Is(e, os.ErrNotExist) {
		if e == nil {
			e = os.ErrExist
		}
		return nil, e
	}
	b, e := line(record{Type: "header", Header: &h}, l.RecordBytes)
	if e != nil {
		return nil, e
	}
	if int64(len(b))+1024 > l.FileBytes {
		return nil, errors.New("replay_capacity_exceeded")
	}
	f, e := os.OpenFile(path+".partial", os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
	if e != nil {
		return nil, e
	}
	j := &Journal{path: path, partial: path + ".partial", limits: l, queue: make(chan queuedRecord, l.Queue), done: make(chan struct{}), bytes: int64(len(b))}
	go j.write(f, b)
	return j, nil
}
func (j *Journal) Path() string { return j.path }
func (j *Journal) Err() error   { j.mu.Lock(); defer j.mu.Unlock(); return j.err }
func (j *Journal) fail(e error) {
	j.mu.Lock()
	defer j.mu.Unlock()
	if j.err == nil {
		j.err = e
	}
}
func (j *Journal) Append(entry netcore.ReplayEntry) bool {
	j.mu.Lock()
	defer j.mu.Unlock()
	if j.closed || j.err != nil {
		return false
	}
	entry.Sequence = j.next + 1
	if entry.Kind == "" || len(entry.Kind) > 64 || !validHash(entry.StateHash) || len(entry.Payload) > 0 && !json.Valid(entry.Payload) {
		j.err = errors.New("invalid_replay_entry")
		return false
	}
	b, e := line(record{Type: "event", Entry: &entry}, j.limits.RecordBytes)
	if e != nil {
		j.err = e
		return false
	}
	// Reserve a bounded footer independently of the event payload size.
	if j.next >= j.limits.Entries || j.bytes+int64(len(b))+1024 > j.limits.FileBytes {
		j.err = errors.New("replay_capacity_exceeded")
		return false
	}
	select {
	case j.queue <- queuedRecord{data: b, tick: entry.Tick, kind: entry.Kind}:
		j.next++
		j.bytes += int64(len(b))
		return true
	default:
		j.err = errors.New("replay_queue_overflow")
		return false
	}
}
func (j *Journal) Close() error {
	j.mu.Lock()
	if !j.closed {
		j.closed = true
		close(j.queue)
	}
	j.mu.Unlock()
	<-j.done
	return j.Err()
}
func (j *Journal) write(f *os.File, header []byte) {
	defer close(j.done)
	defer f.Close()
	w := bufio.NewWriterSize(f, 64*1024)
	digest := sha256.New()
	summary := netcore.ReplaySummary{}
	write := func(b []byte) bool {
		if _, e := w.Write(b); e != nil {
			j.fail(e)
			return false
		}
		_, _ = digest.Write(b)
		return true
	}
	ok := write(header)
	for queued := range j.queue {
		if !ok || j.Err() != nil {
			continue
		}
		if !write(queued.data) {
			ok = false
			continue
		}
		summary.Entries++
		summary.LastTick = queued.tick
		if queued.kind == "step" {
			summary.Steps++
		}
	}
	if j.Err() != nil || !ok {
		return
	}
	summary.Digest = hex.EncodeToString(digest.Sum(nil))
	footer, e := line(record{Type: "footer", Footer: &summary}, j.limits.RecordBytes)
	if e == nil {
		_, e = w.Write(footer)
	}
	if e == nil {
		e = w.Flush()
	}
	if e == nil {
		e = f.Sync()
	}
	if e == nil {
		e = f.Close()
	}
	// A same-directory hard link publishes atomically without clobbering a file
	// created by somebody else. Unsupported filesystems fail explicitly.
	if e == nil {
		e = os.Link(j.partial, j.path)
	}
	if e == nil {
		e = os.Remove(j.partial)
	}
	if e != nil {
		j.fail(e)
	}
}

func strict(data []byte, v any) error {
	d := json.NewDecoder(bytes.NewReader(data))
	d.DisallowUnknownFields()
	if e := d.Decode(v); e != nil {
		return e
	}
	if d.Decode(new(any)) != io.EOF {
		return errors.New("trailing_replay_data")
	}
	return nil
}

// VerifyFile streams at most the configured file/record/count budgets. Apply
// callbacks must target an isolated simulation: integrity is established only
// after the footer, not while processing an untrusted prefix.
func VerifyFile(path string, l Limits, start func(netcore.ReplayHeader) error, apply func(netcore.ReplayEntry) error) (netcore.ReplaySummary, error) {
	var summary netcore.ReplaySummary
	if !l.valid() || start == nil || apply == nil {
		return summary, errors.New("invalid_replay_configuration")
	}
	if strings.HasSuffix(path, ".partial") {
		return summary, errors.New("incomplete_replay_partial_file")
	}
	f, e := os.Open(path)
	if e != nil {
		return summary, e
	}
	defer f.Close()
	s, e := f.Stat()
	if e != nil {
		return summary, e
	}
	if !s.Mode().IsRegular() || s.Size() > l.FileBytes {
		return summary, errors.New("replay_file_limit")
	}
	reader := bufio.NewReaderSize(io.LimitReader(f, l.FileBytes+1), l.RecordBytes)
	digest := sha256.New()
	headerSeen, footerSeen := false, false
	var total int64
	for {
		b, err := reader.ReadSlice('\n')
		if err == io.EOF && len(b) == 0 {
			break
		}
		if err != nil {
			return summary, fmt.Errorf("incomplete_replay: %w", err)
		}
		total += int64(len(b))
		if len(b) > l.RecordBytes || total > l.FileBytes {
			return summary, errors.New("replay_file_limit")
		}
		var rec record
		if e = strict(b, &rec); e != nil {
			return summary, fmt.Errorf("invalid_replay_record: %w", e)
		}
		if footerSeen {
			return summary, errors.New("trailing_replay_record")
		}
		switch rec.Type {
		case "header":
			if headerSeen || rec.Header == nil || rec.Entry != nil || rec.Footer != nil || !validHeader(*rec.Header) {
				return summary, errors.New("invalid_replay_header")
			}
			headerSeen = true
			if e = start(*rec.Header); e != nil {
				return summary, e
			}
			hashLine(digest, b)
		case "event":
			if !headerSeen || rec.Entry == nil || rec.Header != nil || rec.Footer != nil || summary.Entries >= l.Entries || rec.Entry.Sequence != summary.Entries+1 || !validHash(rec.Entry.StateHash) || rec.Entry.Kind == "" || len(rec.Entry.Kind) > 64 {
				return summary, errors.New("invalid_replay_sequence")
			}
			if e = apply(*rec.Entry); e != nil {
				return summary, e
			}
			summary.Entries++
			summary.LastTick = rec.Entry.Tick
			if rec.Entry.Kind == "step" {
				summary.Steps++
			}
			hashLine(digest, b)
		case "footer":
			summary.Digest = hex.EncodeToString(digest.Sum(nil))
			if !headerSeen || rec.Footer == nil || rec.Header != nil || rec.Entry != nil || *rec.Footer != summary {
				return summary, errors.New("replay_integrity_mismatch")
			}
			footerSeen = true
		default:
			return summary, errors.New("unknown_replay_record")
		}
	}
	if !footerSeen {
		return summary, errors.New("incomplete_replay_missing_footer")
	}
	return summary, nil
}
func hashLine(h hash.Hash, b []byte) { _, _ = h.Write(b) }
