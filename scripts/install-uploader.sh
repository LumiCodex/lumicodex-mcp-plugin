#!/usr/bin/env bash
set -euo pipefail

repo="${LUMICODEX_PLUGIN_REPO:-lumicodex/lumicodex-mcp-plugin}"
version="latest"
api_url="${LUMICODEX_API_URL:-https://api.lumicodex.com/}"
account_id="${LUMICODEX_ACCOUNT_ID:-}"
install_dir="${LUMICODEX_UPLOAD_INSTALL_DIR:-$HOME/.lumicodex/bin}"
skip_configure=0
prefer_dotnet=0

usage() {
  cat <<'USAGE'
Install the LumiCodex uploader and optionally configure credentials.

Usage:
  install-uploader.sh [--repo OWNER/REPO] [--version TAG] [--api-url URL]
                      [--account ACCOUNT_ID] [--install-dir DIR]
                      [--prefer-dotnet] [--skip-configure]

Environment:
  LUMICODEX_API_KEY        API key used during configure; otherwise prompted.
  LUMICODEX_PLUGIN_REPO    Release repository. Default: lumicodex/lumicodex-mcp-plugin
  LUMICODEX_ACCOUNT_ID     Default account id to save.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo) repo="$2"; shift 2 ;;
    --version) version="$2"; shift 2 ;;
    --api-url) api_url="$2"; shift 2 ;;
    --account) account_id="$2"; shift 2 ;;
    --install-dir) install_dir="$2"; shift 2 ;;
    --prefer-dotnet) prefer_dotnet=1; shift ;;
    --skip-configure) skip_configure=1; shift ;;
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

download_asset() {
  local asset="$1" output="$2" url
  if [[ "$version" == "latest" ]]; then
    url="https://github.com/$repo/releases/latest/download/$asset"
  else
    url="https://github.com/$repo/releases/download/$version/$asset"
  fi
  curl -fL "$url" -o "$output"
}

extract_archive() {
  local archive="$1" destination="$2"
  mkdir -p "$destination"
  case "$archive" in
    *.zip) unzip -oq "$archive" -d "$destination" ;;
    *.tar.gz) tar -xzf "$archive" -C "$destination" ;;
    *) echo "Unsupported archive: $archive" >&2; exit 1 ;;
  esac
}

rid="$(detect_rid)"
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

mkdir -p "$install_dir"

if [[ "$prefer_dotnet" -eq 0 ]]; then
  native_asset="lumicodex-upload-$rid"
  [[ "$rid" == win-* ]] && native_asset="$native_asset.zip" || native_asset="$native_asset.tar.gz"
  if download_asset "$native_asset" "$work_dir/$native_asset"; then
    extract_archive "$work_dir/$native_asset" "$work_dir/native"
    cp -R "$work_dir/native/." "$install_dir/"
    chmod +x "$install_dir/lumicodex-upload" 2>/dev/null || true
  else
    echo "Native uploader asset not found for $rid; falling back to the .NET 10 build." >&2
    prefer_dotnet=1
  fi
fi

if [[ "$prefer_dotnet" -eq 1 ]]; then
  if ! command -v dotnet >/dev/null 2>&1; then
    echo ".NET 10 is required for the universal uploader asset. Install .NET 10 or use a native release asset." >&2
    exit 1
  fi
  dotnet_asset="lumicodex-upload-dotnet10.tar.gz"
  download_asset "$dotnet_asset" "$work_dir/$dotnet_asset"
  extract_archive "$work_dir/$dotnet_asset" "$work_dir/dotnet"
  cp -R "$work_dir/dotnet/." "$install_dir/"
  chmod +x "$install_dir/lumicodex-upload"
fi

if [[ ":$PATH:" != *":$install_dir:"* ]]; then
  echo "Add this directory to PATH for future shells: $install_dir"
fi

if [[ "$skip_configure" -eq 0 ]]; then
  if [[ -z "${LUMICODEX_API_KEY:-}" ]]; then
    read -rsp "LumiCodex API key: " LUMICODEX_API_KEY
    export LUMICODEX_API_KEY
    echo
  fi
  args=(configure --api-url "$api_url")
  [[ -n "$account_id" ]] && args+=(--account "$account_id")
  "$install_dir/lumicodex-upload" "${args[@]}"
fi

echo "Installed lumicodex-upload in $install_dir"
