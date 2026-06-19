# LumiCodex MCP Plugin

This plugin connects OpenAI Codex and Anthropic Claude Code to the LumiCodex
production MCP endpoint:

```text
https://api.lumicodex.com/mcp
```

It also packages installer and release scripts for `lumicodex-upload`, the
local image uploader used by agents when they need to upload files from your
machine into a LumiCodex album.

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

The plugin `.mcp.json` uses the production HTTP MCP server with the `x-api-key`
header.

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

Inside Claude Code, run `/mcp` to confirm the `lumicodex` server is connected.

## Upload Workflow

After the plugin and uploader are configured, ask the agent to create or find an
album and upload a local folder. The packaged `lumicodex-album-upload` skill
instructs the agent to:

1. Use the LumiCodex MCP server to find or create the album.
2. Capture the returned container id.
3. Run `lumicodex-upload --container CONTAINER_ID --recursive --publish PHOTO_PATH`.

The uploader supports `.jpg`, `.jpeg`, `.jfif`, `.png`, `.webp`, `.jxl`,
`.tif`, `.tiff`, `.jp2`, `.avif`, and `.bmp`.

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
