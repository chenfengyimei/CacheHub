package handler

import (
	"net/http"
	"net/http/httptest"
	"sync"
	"testing"
	"time"
)

func TestHandlerPool_SubmitAndExecute(t *testing.T) {
	pool := NewHandlerPool(2)
	defer pool.Shutdown()

	var done sync.WaitGroup
	done.Add(1)

	ok := pool.Submit(func() {
		done.Done()
	})
	if !ok {
		t.Fatal("Submit returned false")
	}

	done.Wait()
}

func TestHandlerPool_ShutdownPreventsNewTasks(t *testing.T) {
	pool := NewHandlerPool(1)
	pool.Shutdown()

	ok := pool.Submit(func() {})
	if ok {
		t.Fatal("Submit should return false after shutdown")
	}
}

func TestHandlerPool_HandleHTTP(t *testing.T) {
	pool := NewHandlerPool(2)
	defer pool.Shutdown()

	// Give workers time to start
	time.Sleep(10 * time.Millisecond)

	rec := httptest.NewRecorder()
	req := httptest.NewRequest("GET", "/handle", nil)
	pool.HandleHTTP(rec, req)

	// Wait for async handling
	time.Sleep(50 * time.Millisecond)

	if rec.Code != http.StatusOK {
		t.Errorf("expected 200, got %d", rec.Code)
	}
}
