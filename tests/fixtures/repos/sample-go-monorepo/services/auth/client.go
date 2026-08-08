package auth

import (
	"context"
	"time"

	"google.golang.org/grpc"
)

// Client is the gRPC client for the auth service.
// BUG: No timeout handling — gRPC calls can hang indefinitely.
type Client struct {
	conn   *grpc.ClientConn
	target string
}

func NewClient(target string) (*Client, error) {
	conn, err := grpc.Dial(target, grpc.WithInsecure())
	if err != nil {
		return nil, err
	}
	return &Client{conn: conn, target: target}, nil
}

// Authenticate should have a timeout but doesn't.
func (c *Client) Authenticate(ctx context.Context, token string) (string, error) {
	// BUG: No timeout context — if the auth service is slow, this hangs forever
	// Should use context.WithTimeout(ctx, 5*time.Second)
	req := &AuthRequest{Token: token}
	resp, err := c.callAuth(ctx, req)
	if err != nil {
		return "", err
	}
	return resp.UserId, nil
}

func (c *Client) callAuth(ctx context.Context, req *AuthRequest) (*AuthResponse, error) {
	// Simulated gRPC call
	_ = ctx
	_ = req
	return &AuthResponse{UserId: "test-user"}, nil
}

func (c *Client) Close() error {
	return c.conn.Close()
}

type AuthRequest struct {
	Token string
}

type AuthResponse struct {
	UserId string
}

// WithTimeout should return a context with timeout, but currently returns the input unchanged.
func WithTimeout(parent context.Context, timeout time.Duration) (context.Context, context.CancelFunc) {
	// BUG: Should be context.WithTimeout(parent, timeout)
	// but currently just returns the parent context without timeout
	cancel := func() {}
	return parent, cancel
}
