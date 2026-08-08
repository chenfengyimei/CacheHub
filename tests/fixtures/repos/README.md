# Benchmark Matrix Fixture Repositories

These are small, self-contained sample repositories used as fixtures for the CacheHub Benchmark Matrix.

Each fixture contains:
- Real source files with intentional bugs (for agent benchmark tasks)
- Test files (for `--real-test` verification)
- Language-appropriate config (package.json, go.mod, Cargo.toml, etc.)

## Repositories

| Directory | Language | Description |
|-----------|----------|-------------|
| sample-ts-auth | TypeScript | Express auth service with token refresh bug |
| sample-ts-react | TypeScript | React dashboard needing error boundary |
| sample-ts-api | TypeScript | Express API needing logging middleware |
| sample-ts-monorepo | TypeScript | Monorepo with shared types package export issue |
| sample-py-api | Python | FastAPI user repository needing retry decorator |
| sample-py-django | Python | Django serializer dropping nullable fields |
| sample-py-ml | Python | ML data pipeline needing validation step |
| sample-go-server | Go | HTTP server with goroutine leak in handler pool |
| sample-go-cli | Go | CLI tool with file walker lacking context cancellation |
| sample-go-monorepo | Go | Microservices with gRPC timeout handling bug |
| sample-rust-cli | Rust | CLI with config parser that panics on unknown keys |
| sample-rust-server | Rust | Server with broken connection pooling |
| sample-monorepo-fullstack | TypeScript | Full-stack monorepo (frontend + backend + shared) |
