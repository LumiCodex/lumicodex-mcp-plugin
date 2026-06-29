---
name: lumicodex-setup
description: Install and configure the LumiCodex uploader and MCP authentication for Codex or Claude Code. Use when a user needs to provide a LumiCodex account id and API key or make the plugin work after installation.
---

# LumiCodex Setup

Use this workflow when the user asks to install, configure, repair, or verify the LumiCodex MCP plugin or uploader.

## Required Inputs

- `ACCOUNT_ID`: the user's LumiCodex account id.
- A LumiCodex API key. Do not ask the user to paste it into chat.
- The plugin root path if it cannot be inferred from the current working directory.

## Workflow

1. Verify whether `lumicodex-upload` is already available:

   ```bash
   lumicodex-upload --help
   ```

2. If the uploader is missing, run the plugin installer from the plugin root:

   ```bash
   ./scripts/install-uploader.sh --account ACCOUNT_ID
   ```

   On Windows PowerShell:

   ```powershell
   .\scripts\install-uploader.ps1 -Account ACCOUNT_ID
   ```

3. If the user has not exported `LUMICODEX_API_KEY`, let the installer prompt for it. Do not print the key, write it into files, or include it in command text shown in chat.

4. Verify the uploader can provide MCP headers without exposing the secret:

   ```bash
   lumicodex-upload mcp-headers >/dev/null
   ```

5. For Claude Code, ask the user to run `/mcp` after plugin load and confirm the scoped LumiCodex servers they kept (e.g. `lumicodex-photos`, `lumicodex-signatures`, `lumicodex-documents`) are connected.

6. For Codex-compatible clients, verify that `LUMICODEX_API_KEY` is available to the MCP environment if the client does not support `headersHelper`.

## Operating Rules

- Never echo or log the API key.
- Prefer `https://api.lumicodex.com/` for uploader configuration. MCP is served per domain at `https://api.lumicodex.com/mcp/photos`, `/mcp/signatures`, and `/mcp/documents`; mount only the scopes the user needs.
- If a native uploader release is unavailable for the user's platform, use the universal .NET 10 asset and tell the user that .NET 10 must be installed.
- If Linux credential storage fails, tell the user to install libsecret's `secret-tool` or keep `LUMICODEX_API_KEY` in their shell environment.
