#!/bin/bash
# AI_KV Installation Script (Bash)

set -e

echo "AI_KV Installation"
echo "==================="
echo ""

# Check .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET SDK not found. Please install .NET 9 SDK."
    echo "  https://dotnet.microsoft.com/download/dotnet/9.0"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo "[1/4] .NET SDK: $DOTNET_VERSION"

# Build
echo "[2/4] Building AI_KV..."
dotnet build AI_KV.sln -c Release --nologo 2>/dev/null
echo "  Build successful."

# Test
echo "[3/4] Running tests..."
dotnet test AI_KV.sln -c Release --no-build --nologo --verbosity quiet 2>/dev/null || echo "  Warning: Some tests failed."
echo "  Tests complete."

# Publish
echo "[4/4] Publishing single-file executable..."
PUBLISH_DIR="$(dirname "$0")/publish"
dotnet publish src/AiKv.Cli/AiKv.Cli.csproj -c Release -o "$PUBLISH_DIR" --nologo 2>/dev/null

echo "  Published to: $PUBLISH_DIR"
echo ""
echo "To use AI_KV, add to PATH:"
echo "  export PATH=\"\$PATH:$PUBLISH_DIR\""
echo ""
echo "Verify installation:"
echo "  aikv version"
echo "  aikv capabilities"
echo "  aikv integration verify"
echo ""
echo "Installation complete!"
