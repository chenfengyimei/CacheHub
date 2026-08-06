# CacheHub Integration — Generic Shell Agent Example

This example shows how any shell-capable AI agent can integrate with CacheHub.

## Setup

```bash
# Import the workspace
WORKSPACE_ID=$(cachehub workspace import /path/to/project --output=json | jq -r '.id')

# Build the index
cachehub index build --id=$WORKSPACE_ID

# Verify integration
cachehub integration verify
```

## Before Each Task

```bash
# Build context for the task
CONTEXT_ID=$(cachehub context build \
  --workspace=$WORKSPACE_ID \
  --task="Fix the login token refresh bug" \
  --output=json | jq -r '.id')

# Get the selected files
SELECTED=$(cachehub context inspect --id=$CONTEXT_ID --output=json | jq -r '.selectedFiles[].path')

# Read only the selected files
for file in $SELECTED; do
  cat "$file"
done
```

## After Task Completion

```bash
# Submit feedback
cat > feedback.json << 'EOF'
{
  "context_package_id": "'$CONTEXT_ID'",
  "client_id": "shell-agent",
  "files_actually_read": [],
  "task_completed": true,
  "missing_context_reported": false,
  "total_workflow_input_tokens": 0
}
EOF

cachehub context feedback --id=$CONTEXT_ID --file=feedback.json
```

## Expand Context (if needed)

```bash
# Expand by symbol
cachehub context expand --id=$CONTEXT_ID --symbol=UserService

# Expand by file
cachehub context expand --id=$CONTEXT_ID --file=src/auth/service.ts
```
