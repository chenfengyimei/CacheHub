package user

import (
	"context"
	"net"

	"google.golang.org/grpc"
)

type Server struct {
	grpcServer *grpc.Server
	listener   net.Listener
}

func NewServer(addr string) (*Server, error) {
	lis, err := net.Listen("tcp", addr)
	if err != nil {
		return nil, err
	}
	s := grpc.NewServer()
	return &Server{
		grpcServer: s,
		listener:   lis,
	}, nil
}

func (s *Server) Start() error {
	return s.grpcServer.Serve(s.listener)
}

func (s *Server) Stop() {
	s.grpcServer.GracefulStop()
}

// GetUser should support context cancellation but doesn't check ctx.
func (s *Server) GetUser(ctx context.Context, userId string) (*UserInfo, error) {
	// BUG: ctx is not checked for cancellation
	return &UserInfo{
		ID:    userId,
		Email: "user@example.com",
		Name:  "Test User",
	}, nil
}

type UserInfo struct {
	ID    string
	Email string
	Name  string
}
