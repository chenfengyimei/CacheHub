package handler

import (
	"net/http"
	"sync"
)

// HandlerPool manages a pool of HTTP handlers.
// BUG: goroutine leak — workers are spawned but never properly cleaned up
// when the pool is shut down.
type HandlerPool struct {
	workers    int
	tasks      chan func()
	wg         sync.WaitGroup
	shutdown   bool
	mu         sync.Mutex
}

func NewHandlerPool(workers int) *HandlerPool {
	p := &HandlerPool{
		workers: workers,
		tasks:   make(chan func(), workers*2),
	}
	p.start()
	return p
}

func (p *HandlerPool) start() {
	for i := 0; i < p.workers; i++ {
		p.wg.Add(1)
		go func() {
			defer p.wg.Done()
			for task := range p.tasks {
				task()
			}
		}()
	}
}

func (p *HandlerPool) Submit(task func()) bool {
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.shutdown {
		return false
	}
	p.tasks <- task
	return true
}

// Shutdown should close the tasks channel and wait for workers to finish.
// BUG: Currently does not close the channel, causing goroutine leak.
func (p *HandlerPool) Shutdown() {
	p.mu.Lock()
	defer p.mu.Unlock()
	p.shutdown = true
	// Missing: close(p.tasks) — this causes goroutine leak
}

func (p *HandlerPool) HandleHTTP(w http.ResponseWriter, r *http.Request) {
	p.Submit(func() {
		w.WriteHeader(http.StatusOK)
		w.Write([]byte("handled"))
	})
}
