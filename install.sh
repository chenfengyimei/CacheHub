#!/bin/bash
# CacheHub Installation Script (Bash)

set -e

echo "CacheHub Installation"
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
echo "[2/4] Building CacheHub..."
dotnet build CacheHub.sln -c Release --nologo 2>/dev/null
echo "  Build successful."

# Test
echo "[3/4] Running tests..."
dotnet test CacheHub.sln -c Release --no-build --nologo --verbosity quiet 2>/dev/null || echo "  Warning: Some tests failed."
echo "  Tests complete."

# Publish
echo "[4/4] Publishing single-file executable..."
PUBLISH_DIR="$(dirname "$0")/publish"
dotnet publish src/CacheHub.Cli/CacheHub.Cli.csproj -c Release -o "$PUBLISH_DIR" --nologo 2>/dev/null

echo "  Published to: $PUBLISH_DIR"
echo ""
echo "To use CacheHub, add to PATH:"
echo "  export PATH=\"\$PATH:$PUBLISH_DIR\""
echo ""
echo "Verify installation:"
echo "  cachehub version"
echo "  cachehub capabilities"
echo "  cachehub integration verify"
echo ""
echo "Installation complete!"
