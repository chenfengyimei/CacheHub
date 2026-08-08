package server

import (
	"net/http"
	"sample-go-server/internal/handler"
)

type Server struct {
	httpServer *http.Server
	pool       *handler.HandlerPool
}

func NewServer(addr string, workers int) *Server {
	pool := handler.NewHandlerPool(workers)
	mux := http.NewServeMux()
	mux.HandleFunc("/handle", pool.HandleHTTP)

	return &Server{
		httpServer: &http.Server{
			Addr:    addr,
			Handler: mux,
		},
		pool: pool,
	}
}

func (s *Server) Start() error {
	return s.httpServer.ListenAndServe()
}

func (s *Server) Stop() {
	s.pool.Shutdown()
	s.httpServer.Close()
}
