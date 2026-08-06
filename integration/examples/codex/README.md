# CacheHub Integration — Codex Example

> This example shows how to configure OpenAI Codex (or similar coding agents) to use CacheHub.

## Configuration

Add the following to your Codex configuration:

```yaml
# codex-config.yaml
tools:
  - name: cachehub_capabilities
    command: cachehub capabilities --output=json
    description: Check CacheHub available features

  - name: cachehub_context_build
    command: cachehub context build --workspace=$WORKSPACE_ID --task="$TASK" --output=json
    description: Build context package for the current task
    env:
      WORKSPACE_ID: "${workspace.id}"
      TASK: "${task.description}"

  - name: cachehub_context_expand
    command: cachehub context expand --id=$CONTEXT_ID --symbol=$SYMBOL
    description: Expand context with additional files
    env:
      CONTEXT_ID: "${context.id}"
      SYMBOL: "${symbol}"
```

## System Prompt

Add the system prompt snippet from `integration/templates/system-prompt-snippet.md`.

## Workflow

1. Codex receives a task
2. Calls `cachehub_context_build` tool
3. Reads the returned `selectedFiles`
4. Only reads those files (not the entire repository)
5. If more context needed, calls `cachehub_context_expand`
6. Completes the task
7. Optionally submits feedback
