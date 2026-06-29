# LumiCodex MCP Plugin

This plugin connects OpenAI Codex and Anthropic Claude Code to the LumiCodex
production MCP endpoints. Tools are split by domain so you can mount only what
you need — one endpoint or several side by side:

```text
https://api.lumicodex.com/mcp/photos       # album / image management
https://api.lumicodex.com/mcp/signatures   # e-signature envelopes
https://api.lumicodex.com/mcp/documents    # document processing
```

The bundled `.mcp.json` registers all three as independent servers
(`lumicodex-photos`, `lumicodex-signatures`, `lumicodex-documents`). Remove any
you do not need so agents only see the relevant tools.

It also packages installer and release scripts for `lumicodex-upload`, the
local uploader used by agents to move files between your machine and LumiCodex:
images into an album, and documents into ephemeral storage for the signatures
and documents tools (plus downloading processed results back to disk).

The standalone uploader source is included under `src/LumiCodex.Upload` so this
plugin repository can build release assets after it is published.

## What Users Need

- A LumiCodex account id.
- A LumiCodex API key.
- `curl`, `tar`, and `unzip` on macOS/Linux, or PowerShell on Windows.
- Linux only: `secret-tool` from libsecret if you want the uploader to store
  the API key in the OS credential store.

## Install The Uploader

After this repository is public and has release assets, users can install the
best native uploader for their platform:

```bash
./scripts/install-uploader.sh --repo OWNER/REPO --account ACCOUNT_ID
```

PowerShell:

```powershell
.\scripts\install-uploader.ps1 -Repo OWNER/REPO -Account ACCOUNT_ID
```

The installer downloads a native AOT asset when one exists. If no native asset
matches the platform, it falls back to the universal .NET 10 asset and requires
`dotnet` on `PATH`.

Set `LUMICODEX_API_KEY` before running the installer to avoid an interactive
prompt:

```bash
LUMICODEX_API_KEY=... ./scripts/install-uploader.sh --repo OWNER/REPO --account ACCOUNT_ID
```

The installer runs:

```bash
lumicodex-upload configure --api-url https://api.lumicodex.com/ --account ACCOUNT_ID
```

That validates the API key and stores it in the OS credential store.

## MCP Authentication

The plugin `.mcp.json` uses the production HTTP MCP servers with the `x-api-key`
header. All scoped endpoints share the same API key.

Claude Code can use `headersHelper` to read the API key from either
`LUMICODEX_API_KEY` or the credential saved by `lumicodex-upload configure`.

Codex-compatible clients can use the `headers` entry with
`LUMICODEX_API_KEY`. If your Codex environment does not expand environment
variables in MCP headers, set the API key in the MCP configuration supported by
that client.

## Local Claude Code Test

From the parent folder:

```bash
claude --plugin-dir ./lumicodex-mcp-plugin
```

Inside Claude Code, run `/mcp` to confirm the scoped servers you kept (for
example `lumicodex-photos`) are connected.

## Upload Workflow

After the plugin and uploader are configured, ask the agent to create or find an
album and upload a local folder. The packaged `lumicodex-album-upload` skill
instructs the agent to:

1. Use the LumiCodex photos MCP server (`lumicodex-photos`) to find or create the album.
2. Capture the returned container id.
3. Run `lumicodex-upload --container CONTAINER_ID --recursive --publish PHOTO_PATH`.

The uploader supports `.jpg`, `.jpeg`, `.jfif`, `.png`, `.webp`, `.jxl`,
`.tif`, `.tiff`, `.jp2`, `.avif`, and `.bmp`.

## Document Workflow

For the signatures and documents tools, the `lumicodex-document-transfer` skill
moves document bytes with the uploader instead of base64 through the agent:

1. `lumicodex-upload documents upload contract.pdf` → JSON `{ id, name }` per file.
2. Use each `id` as `Source.documentId` for a documents tool, or fetch a URL via
   `documents_get_download_url` for `envelopes_add_document`.
3. Save a processed result with
   `lumicodex-upload documents download --out ./result.pdf "PRESIGNED_URL"`.

Document downloads use only the presigned URL and need no API key; uploads need
an API key with `Process` permission.

## Build Release Assets

From this plugin repository:

```bash
./scripts/publish-uploader.sh --rid linux-x64
```

The script builds from `./src` by default. Use `--source` only when syncing or
testing against a neighboring backend checkout.

Build only the universal framework-dependent .NET 10 assets:

```bash
./scripts/publish-uploader.sh --only-universal
```

Build only a native AOT asset:

```bash
./scripts/publish-uploader.sh --no-universal --rid linux-x64
```

Native AOT builds must run on the target OS family. The included GitHub Actions
workflow builds release assets for Linux, macOS, and Windows x64/arm64 runners
and attaches them to a GitHub release when a release is published.

## Sync Uploader Source

When `LumiCodex.Upload` changes in the backend solution, refresh the vendored
copy before publishing the plugin repository:

```bash
./scripts/sync-uploader-source.sh ../LumiCodex/LumiCodexSolution/LumiCodex.Upload
```

See `RELEASE.md` for the release checklist and asset list.
