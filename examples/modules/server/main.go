package main

import (
	"flag"
	"fmt"
	"os"
)

func main() {
	selected := flag.String("lesson", "all", "all, history-lagcomp, interest, codec, replay")
	flag.Parse()
	lessons := []struct {
		name string
		run  func() error
	}{{"history-lagcomp", historyLesson}, {"interest", interestLesson}, {"codec", codecLesson}, {"replay", replayLesson}}
	found := false
	for _, lesson := range lessons {
		if *selected != "all" && *selected != lesson.name {
			continue
		}
		found = true
		if err := lesson.run(); err != nil {
			fmt.Fprintln(os.Stderr, lesson.name+":", err)
			os.Exit(1)
		}
		fmt.Println("RF_SERVER_LESSON PASS", lesson.name)
	}
	if !found {
		fmt.Fprintln(os.Stderr, "unknown lesson:", *selected)
		os.Exit(2)
	}
}

func require(ok bool, message string) error {
	if !ok {
		return fmt.Errorf("%s", message)
	}
	return nil
}
