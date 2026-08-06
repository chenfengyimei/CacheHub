# AI_KV Integration — Generic Shell Agent Example

This example shows how any shell-capable AI agent can integrate with AI_KV.

## Setup

```bash
# Import the workspace
WORKSPACE_ID=$(aikv workspace import /path/to/project --output=json | jq -r '.id')

# Build the index
aikv index build --id=$WORKSPACE_ID

# Verify integration
aikv integration verify
```

## Before Each Task

```bash
# Build context for the task
CONTEXT_ID=$(aikv context build \
  --workspace=$WORKSPACE_ID \
  --task="Fix the login token refresh bug" \
  --output=json | jq -r '.id')

# Get the selected files
SELECTED=$(aikv context inspect --id=$CONTEXT_ID --output=json | jq -r '.selectedFiles[].path')

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

aikv context feedback --id=$CONTEXT_ID --file=feedback.json
```

## Expand Context (if needed)

```bash
# Expand by symbol
aikv context expand --id=$CONTEXT_ID --symbol=UserService

# Expand by file
aikv context expand --id=$CONTEXT_ID --file=src/auth/service.ts
```
