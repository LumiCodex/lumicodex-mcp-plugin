---
name: lumicodex-setup
description: Configure LumiCodex MCP OAuth for Codex or Claude Code and install the API-key-backed local uploader when needed. Use when the plugin needs browser login, an MCP scope is disconnected, Codex startup previously failed without LUMICODEX_API_KEY, or local photo/document transfer needs uploader credentials.
---

# LumiCodex Setup

Use this workflow when the user asks to install, configure, repair, or verify the LumiCodex MCP plugin or uploader.

## Inputs

- MCP browser authentication needs only the user's LumiCodex login.
- Uploader installation needs `ACCOUNT_ID` and a LumiCodex API key. Never ask the user to paste the key into chat.
- Infer the plugin root path from the current working directory when possible.

## Workflow

1. Mount only the MCP scopes the user needs: `lumicodex-photos`, `lumicodex-signatures`, and/or `lumicodex-documents`.

2. For Codex, inspect auth status and start browser authorization for each needed scope:

   ```bash
   codex mcp list
   codex mcp login lumicodex-photos
   ```

   The bundled Codex servers are non-required. Missing authentication must not prevent Codex from starting.

3. For Claude Code, ask the user to open `/mcp`, select each needed LumiCodex server, and complete the browser login. OAuth tokens are stored and refreshed by the client.

4. Only install the uploader when the task needs local photo or document transfer. First check whether it is already available:

   ```bash
   lumicodex-upload --help
   ```

5. If the uploader is missing, run the plugin installer from the plugin root:

   ```bash
   ./scripts/install-uploader.sh --account ACCOUNT_ID
   ```

   On Windows PowerShell:

   ```powershell
   .\scripts\install-uploader.ps1 -Account ACCOUNT_ID
   ```

6. If the user has not exported `LUMICODEX_API_KEY`, have them enter it at the installer's masked prompt. Do not print the key, write it into files, or include it in command text shown in chat.

7. Verify the uploader credential without exposing the secret:

   ```bash
   lumicodex-upload mcp-headers >/dev/null
   ```

## Operating Rules

- Never echo or log the API key.
- Do not require `LUMICODEX_API_KEY` for MCP. OAuth with PKCE is the default interactive flow.
- API-key MCP headers remain an automation fallback and belong in private user configuration, never the checked-in plugin files.
- Prefer `https://api.lumicodex.com/` for uploader configuration. MCP is served per domain at `https://api.lumicodex.com/mcp/photos`, `/mcp/signatures`, and `/mcp/documents`; mount only the scopes the user needs.
- If a native uploader release is unavailable for the user's platform, use the universal .NET 10 asset and tell the user that .NET 10 must be installed.
- If Linux credential storage fails, tell the user to install libsecret's `secret-tool` or keep `LUMICODEX_API_KEY` in their shell environment.
