#!/usr/bin/env bash
set -euo pipefail

plugin_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_dir="${1:-$plugin_root/../LumiCodex/LumiCodexSolution/LumiCodex.Upload}"
target_dir="$plugin_root/src/LumiCodex.Upload"

if [[ ! -f "$source_dir/LumiCodex.Upload.csproj" ]]; then
  echo "Usage: sync-uploader-source.sh [path-to-LumiCodex.Upload]" >&2
  exit 2
fi

mkdir -p "$target_dir"
find "$target_dir" -maxdepth 1 -type f -delete
cp "$source_dir"/*.cs "$source_dir"/*.csproj "$source_dir"/README.md "$target_dir"/

echo "Synced uploader source into $target_dir"
