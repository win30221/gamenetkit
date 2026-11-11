// compatibility exchanges semantic fixtures with the official C# runtime.
package main

import (
	"errors"
	"flag"
	"fmt"
	"math"
	"os"

	pb "github.com/win30221/gamenetkit/examples/protobuf/server/gen"
	"google.golang.org/protobuf/proto"
)

func fixture() *pb.CompatibilityProbe {
	return &pb.CompatibilityProbe{
		OptionalValue: proto.Uint64(0),
		Values:        []uint64{9007199254740993, math.MaxUint64},
		Body:          &pb.CompatibilityProbe_Number{Number: math.MaxUint64},
	}
}

func verify(data []byte) error {
	m := new(pb.CompatibilityProbe)
	if err := proto.Unmarshal(data, m); err != nil {
		return err
	}
	_, numberBody := m.Body.(*pb.CompatibilityProbe_Number)
	if m.OptionalValue == nil || m.GetOptionalValue() != 0 || len(m.Values) != 2 ||
		m.Values[0] != 9007199254740993 || m.Values[1] != math.MaxUint64 || !numberBody || m.GetNumber() != math.MaxUint64 {
		return fmt.Errorf("fixture semantic mismatch: %v", m)
	}
	return nil
}

func run(writePath, readPath string) error {
	if (writePath == "") == (readPath == "") {
		return errors.New("provide exactly one of -write or -read")
	}
	if writePath != "" {
		data, err := proto.Marshal(fixture())
		if err != nil {
			return err
		}
		return os.WriteFile(writePath, data, 0600)
	}
	data, err := os.ReadFile(readPath)
	if err != nil {
		return err
	}
	return verify(data)
}

func main() {
	writePath := flag.String("write", "", "write Go fixture bytes to this file")
	readPath := flag.String("read", "", "verify fixture bytes produced by C#")
	flag.Parse()
	if err := run(*writePath, *readPath); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
