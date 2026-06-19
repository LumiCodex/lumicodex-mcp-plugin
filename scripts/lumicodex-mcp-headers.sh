#!/usr/bin/env bash
set -euo pipefail

api_url="${LUMICODEX_API_URL:-https://api.lumicodex.com/}"

json_escape() {
  local value="$1"
  value=${value//\\/\\\\}
  value=${value//\"/\\\"}
  value=${value//$'\n'/\\n}
  value=${value//$'\r'/\\r}
  value=${value//$'\t'/\\t}
  printf '%s' "$value"
}

if [[ -n "${LUMICODEX_API_KEY:-}" ]]; then
  printf '{"x-api-key":"%s"}\n' "$(json_escape "$LUMICODEX_API_KEY")"
  exit 0
fi

if command -v lumicodex-upload >/dev/null 2>&1; then
  exec lumicodex-upload mcp-headers --api-url "$api_url"
fi

if [[ -x "$HOME/.lumicodex/bin/lumicodex-upload" ]]; then
  exec "$HOME/.lumicodex/bin/lumicodex-upload" mcp-headers --api-url "$api_url"
fi

echo "lumicodex-upload is not installed and LUMICODEX_API_KEY is not set." >&2
exit 1
