package walker

import (
	"context"
	"os"
	"path/filepath"
)

// FileWalker traverses directories and yields file paths.
// BUG: Does not support context cancellation — long walks can't be aborted.
type FileWalker struct {
	root    string
	exclude []string
}

func NewFileWalker(root string, exclude ...string) *FileWalker {
	return &FileWalker{
		root:    root,
		exclude: exclude,
	}
}

// Walk traverses the directory tree and calls fn for each file.
// BUG: ctx is ignored — no cancellation support.
func (w *FileWalker) Walk(ctx context.Context, fn func(path string, info os.FileInfo) error) error {
	return filepath.Walk(w.root, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return err
		}
		if info.IsDir() {
			for _, ex := range w.exclude {
				if info.Name() == ex {
					return filepath.SkipDir
				}
			}
			return nil
		}
		return fn(path, info)
	})
}

// WalkWithCancel should respect context cancellation but currently doesn't.
func (w *FileWalker) WalkWithCancel(ctx context.Context, fn func(path string, info os.FileInfo) error) error {
	// BUG: ctx is not checked — walks continue even after cancellation
	return w.Walk(ctx, fn)
}
