# LumiCodex MCP Plugin

This plugin connects OpenAI Codex and Anthropic Claude Code to the LumiCodex
production MCP endpoints. Tools are split by domain so you can mount the
endpoints you need, from one endpoint to all three:

```text
https://api.lumicodex.com/mcp/photos       # album, collection, and image management
https://api.lumicodex.com/mcp/signatures   # e-signature envelopes
https://api.lumicodex.com/mcp/documents    # document processing
```

The bundled MCP configuration registers all three as independent servers
(`lumicodex-photos`, `lumicodex-signatures`, `lumicodex-documents`). Remove any
you do not need so agents only see the relevant tools.

It also packages installer and release scripts for `lumicodex-upload`, the
local uploader used by agents to move files between your machine and LumiCodex:
images into an album, and documents into ephemeral storage for the signatures
and documents tools (plus downloading processed results back to disk).

The standalone uploader source is included under `src/LumiCodex.Upload` so this
plugin repository can build release assets after it is published.

## What Users Need

- A LumiCodex account for MCP browser authentication.
- An account id and API key only when installing the local uploader.
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

The production HTTP MCP servers use OAuth 2.1 authorization-code flow with PKCE.
No `LUMICODEX_API_KEY` export is required. After installing the plugin,
authorize the scopes you want and approve each connection in the browser.

For Codex, authenticate each scope when you need it:

```bash
codex mcp login lumicodex-photos
```

The Codex entries are marked non-required, so a missing login or LumiCodex
endpoint outage does not abort Codex startup. Claude Code users
can run `/mcp`, select a LumiCodex server, and complete the browser login.

API-key authentication remains supported by the server for automation and the
uploader. To use it for MCP instead of OAuth, add an `x-api-key` header through
the client's private user configuration; do not commit the key to this plugin.

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

## Collection Workflow

The photos MCP server also exposes `groups_*` tools for photographer-facing
collections. Agents can list/create collections, update their metadata and
access, add or remove albums, publish collection JSON, delete while keeping or
deleting member albums, and create/list/revoke metered collection share links.

Adding an album or changing collection access physically moves media and
changes private CDN paths. The tool descriptions call out that consequence;
republish affected albums and the collection before distributing embeds or
links.

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
