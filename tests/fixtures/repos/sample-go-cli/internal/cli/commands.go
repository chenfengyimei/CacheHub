package cli

import (
	"context"
	"flag"
	"fmt"
	"os"

	"sample-go-cli/internal/walker"
)

type Commands struct {
	walker *walker.FileWalker
}

func New(w *walker.FileWalker) *Commands {
	return &Commands{walker: w}
}

func (c *Commands) Run(args []string) int {
	if len(args) < 1 {
		fmt.Fprintln(os.Stderr, "Usage: sample-go-cli <command> [options]")
		fmt.Fprintln(os.Stderr, "Commands: walk, help")
		return 1
	}

	switch args[0] {
	case "walk":
		return c.walkCommand(args[1:])
	case "help":
		fmt.Println("Commands: walk, help")
		return 0
	default:
		fmt.Fprintf(os.Stderr, "Unknown command: %s\n", args[0])
		return 1
	}
}

func (c *Commands) walkCommand(args []string) int {
	fs := flag.NewFlagSet("walk", flag.ExitOnError)
	root := fs.String("root", ".", "Root directory to walk")
	exclude := fs.String("exclude", "", "Directory to exclude")
	fs.Parse(args)

	w := walker.NewFileWalker(*root, *exclude)
	count := 0

	ctx := context.Background()
	err := w.Walk(ctx, func(path string, info os.FileInfo) error {
		count++
		fmt.Println(path)
		return nil
	})

	if err != nil {
		fmt.Fprintf(os.Stderr, "Walk error: %v\n", err)
		return 1
	}

	fmt.Printf("\nTotal files: %d\n", count)
	return 0
}
