#!/usr/bin/env bash

plugin_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
default_source="$plugin_root/src"
if [[ ! -f "$default_source/LumiCodex.Upload/LumiCodex.Upload.csproj" ]]; then
  default_source="$plugin_root/../LumiCodex/LumiCodexSolution"
fi
source_root="$(cd "$default_source" && pwd)"
output_root="$plugin_root/dist"
rids=()
build_universal=1
build_native=1

usage() {
  cat <<'USAGE'
Publish LumiCodex uploader release assets.

Usage:
  publish-uploader.sh [--source PATH] [--output DIR] [--rid RID]...
                      [--only-universal] [--no-universal]

Examples:
  ./scripts/publish-uploader.sh --rid linux-x64 --rid linux-arm64
  ./scripts/publish-uploader.sh --source ./src --rid linux-x64
  ./scripts/publish-uploader.sh --source ../LumiCodex/LumiCodexSolution --rid osx-arm64
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source) source_root="$(cd "$2" && pwd)"; shift 2 ;;
    --output) output_root="$(mkdir -p "$2" && cd "$2" && pwd)"; shift 2 ;;
    --rid) rids+=("$2"); shift 2 ;;
    --only-universal) build_native=0; shift ;;
    --no-universal) build_universal=0; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 2 ;;
  esac
done

detect_rid() {
  local os arch
  os="$(uname -s)"
  arch="$(uname -m)"
  case "$os" in
    Linux) os="linux" ;;
    Darwin) os="osx" ;;
    MINGW*|MSYS*|CYGWIN*) os="win" ;;
    *) echo "Unsupported OS: $os" >&2; exit 1 ;;
  esac
  case "$arch" in
    x86_64|amd64) arch="x64" ;;
    arm64|aarch64) arch="arm64" ;;
    *) echo "Unsupported architecture: $arch" >&2; exit 1 ;;
  esac
  printf '%s-%s' "$os" "$arch"
}

archive_dir() {
  local source_dir="$1" asset_base="$2"
  if [[ "$asset_base" == *win-* ]]; then
    if command -v zip >/dev/null 2>&1; then
      (cd "$source_dir" && zip -qr "$output_root/$asset_base.zip" .) || exit 1
    else
      powershell -NoProfile -Command "Compress-Archive -Path '$source_dir/*' -DestinationPath '$output_root/$asset_base.zip' -Force" || exit 1
    fi
  else
    tar -C "$source_dir" -czf "$output_root/$asset_base.tar.gz" . || exit 1
  fi
}

archive_universal_dir() {
  local source_dir="$1"
  tar -C "$source_dir" -czf "$output_root/lumicodex-upload-dotnet10.tar.gz" . || exit 1
  if command -v zip >/dev/null 2>&1; then
    (cd "$source_dir" && zip -qr "$output_root/lumicodex-upload-dotnet10.zip" .) || exit 1
  fi
}

if [[ -f "$source_root/LumiCodex.Upload/LumiCodex.Upload.csproj" ]]; then
  project="$source_root/LumiCodex.Upload/LumiCodex.Upload.csproj"
elif [[ -f "$source_root/LumiCodex.Upload.csproj" ]]; then
  project="$source_root/LumiCodex.Upload.csproj"
else
  echo "Could not find LumiCodex.Upload.csproj under $source_root" >&2
  exit 1
fi
mkdir -p "$output_root"
work_root="${TMPDIR:-/tmp}/lcup-$(date +%s%N)-${RANDOM:-0}"
if [[ -e "$work_root" ]]; then
  echo "Temporary publish directory already exists: $work_root" >&2
  exit 1
fi
trap 'rm -rf "$work_root"' EXIT
project_dir="$(cd "$(dirname "$project")" && pwd)"

if [[ "$build_universal" -eq 1 ]]; then
  universal_dir="$work_root/lumicodex-upload-dotnet10"
  mkdir -p "$universal_dir"
  dotnet publish "$project" -c Release --self-contained false -p:UseAppHost=false -o "$universal_dir" || exit 1
  cat > "$universal_dir/lumicodex-upload" <<'WRAPPER'
#!/usr/bin/env sh
exec dotnet "$(dirname "$0")/lumicodex-upload.dll" "$@"
WRAPPER
  chmod +x "$universal_dir/lumicodex-upload"
  cat > "$universal_dir/lumicodex-upload.cmd" <<'WRAPPER'
@echo off
dotnet "%~dp0lumicodex-upload.dll" %*
WRAPPER
  archive_universal_dir "$universal_dir"
fi

if [[ "$build_native" -eq 0 ]]; then
  echo "Release assets written to $output_root"
  exit 0
fi

if [[ "${#rids[@]}" -eq 0 ]]; then
  rids=("$(detect_rid)")
fi

for rid in "${rids[@]}"; do
  dotnet publish "$project" -c Release -r "$rid" --self-contained true -p:PublishAot=true -p:IlcGenerateStackTraceData=false || exit 1
  native_dir="$project_dir/bin/Release/net10.0/$rid/publish"
  archive_dir "$native_dir" "lumicodex-upload-$rid"
done

echo "Release assets written to $output_root"
