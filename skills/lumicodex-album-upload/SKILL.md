---
name: lumicodex-album-upload
description: Create or find LumiCodex albums with the LumiCodex MCP server and upload local image files through lumicodex-upload. Use when a user asks to put local photos into a LumiCodex album/container.
---

# LumiCodex Album Upload

Use this workflow when the user wants to create, find, update, publish, or upload images to a LumiCodex album.

## Required Inputs

- A local file or folder path containing images.
- The target album name or container id.
- A LumiCodex account id only when the account cannot be inferred from the configured API key or uploader configuration.

## Workflow

1. Use the configured `lumicodex` MCP server to list or create the target album/container.
2. Capture the returned container id exactly. Do not substitute the album name for the id.
3. Verify the uploader is available:

   ```bash
   lumicodex-upload --help
   ```

   If it is not on `PATH`, try `$HOME/.lumicodex/bin/lumicodex-upload`.

4. Upload images with the returned container id:

   ```bash
   lumicodex-upload --container CONTAINER_ID --recursive --publish PHOTO_PATH
   ```

   Add `--account ACCOUNT_ID` only when the user supplied an account id or the uploader has no configured default.

## Operating Rules

- Never print, log, or paste the LumiCodex API key.
- Prefer the production API URL `https://api.lumicodex.com/` unless the user explicitly gives another URL.
- Use `--recursive` for folders unless the user asks to upload only the top level.
- Use `--publish` when the user asks for a public/published album. Omit it when they ask to upload without publishing.
- If `lumicodex-upload` reports that no API key is available, tell the user to run the plugin installer or `lumicodex-upload configure`.
