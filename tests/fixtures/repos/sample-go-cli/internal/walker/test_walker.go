package walker

import (
	"context"
	"os"
	"path/filepath"
	"testing"
)

func TestFileWalker_Walk(t *testing.T) {
	tmpDir := t.TempDir()
	// Create test files
	os.WriteFile(filepath.Join(tmpDir, "a.txt"), []byte("a"), 0644)
	os.Mkdir(filepath.Join(tmpDir, "sub"), 0755)
	os.WriteFile(filepath.Join(tmpDir, "sub", "b.txt"), []byte("b"), 0644)

	w := NewFileWalker(tmpDir, "sub")
	count := 0
	err := w.Walk(context.Background(), func(path string, info os.FileInfo) error {
		count++
		return nil
	})

	if err != nil {
		t.Fatalf("Walk failed: %v", err)
	}
	if count != 1 {
		t.Errorf("Expected 1 file (sub excluded), got %d", count)
	}
}

func TestFileWalker_NoExclude(t *testing.T) {
	tmpDir := t.TempDir()
	os.WriteFile(filepath.Join(tmpDir, "a.txt"), []byte("a"), 0644)
	os.Mkdir(filepath.Join(tmpDir, "sub"), 0755)
	os.WriteFile(filepath.Join(tmpDir, "sub", "b.txt"), []byte("b"), 0644)

	w := NewFileWalker(tmpDir)
	count := 0
	err := w.Walk(context.Background(), func(path string, info os.FileInfo) error {
		count++
		return nil
	})

	if err != nil {
		t.Fatalf("Walk failed: %v", err)
	}
	if count != 2 {
		t.Errorf("Expected 2 files, got %d", count)
	}
}
