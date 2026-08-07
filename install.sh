#!/bin/bash
# CacheHub Installation Script (Bash)

set -e

SKIP_TESTS=false

# Parse arguments
for arg in "$@"; do
    case "$arg" in
        --skip-tests)
            SKIP_TESTS=true
            ;;
        --help|-h)
            echo "Usage: install.sh [--skip-tests]"
            echo "  --skip-tests  Skip test suite (not recommended for production)"
            exit 0
            ;;
    esac
done

echo "CacheHub Installation"
echo "==================="
echo ""

# Check .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET SDK not found. Please install .NET 9 SDK."
    echo "  https://dotnet.microsoft.com/download/dotnet/10.0"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo "[1/4] .NET SDK: $DOTNET_VERSION"

# Build
echo "[2/4] Building CacheHub..."
dotnet build CacheHub.sln -c Release --nologo 2>/dev/null
echo "  Build successful."

# Test
if [ "$SKIP_TESTS" = false ]; then
    echo "[3/4] Running tests..."
    if ! dotnet test CacheHub.sln -c Release --no-build --nologo --verbosity quiet 2>/dev/null; then
        echo "Error: Tests failed. Aborting installation."
        echo "  Use --skip-tests flag to bypass (not recommended for production)."
        exit 1
    fi
    echo "  All tests passed."
else
    echo "[3/4] Skipping tests (--skip-tests flag set)."
fi

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
