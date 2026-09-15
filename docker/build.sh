#!/usr/bin/env bash
# Build the engine image with whichever container CLI is installed (Podman preferred).
set -euo pipefail
cd "$(dirname "$0")/.."
CLI="${OPENPYSTRUCT_CONTAINER_CLI:-$(command -v podman || command -v docker)}"
TAG="${1:-openpystruct}"
FILE="${2:-docker/Dockerfile}"
echo "building $TAG with $CLI from $FILE"
"$CLI" build -t "$TAG" -f "$FILE" .
"$CLI" run --rm "$TAG" info
