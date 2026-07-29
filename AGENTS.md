# LumiCodex MCP Plugin

This project packages the LumiCodex production MCP endpoints and the
`lumicodex-upload` image uploader for OpenAI Codex and Anthropic Claude Code.
The photos endpoint includes album/image `containers_*` tools and collection
`groups_*` tools; user-facing descriptions call backend groups collections.

## Key Files

- `.codex-plugin/plugin.json` is the Codex plugin manifest.
- `.claude-plugin/plugin.json` is the Claude Code plugin manifest.
- `.mcp.json` registers the OAuth-enabled, non-required Codex MCP servers
  (`lumicodex-photos`, `lumicodex-signatures`, `lumicodex-documents`).
- `.mcp.claude.json` registers the same OAuth-enabled servers using Claude
  Code's MCP configuration shape.
- `skills/lumicodex-album-upload/SKILL.md` describes the album upload workflow.
- `scripts/install-uploader.*` downloads release assets and runs uploader configuration.
- `scripts/publish-uploader.sh` builds release assets from
  `src/LumiCodex.Upload` by default.
- `scripts/sync-uploader-source.sh` refreshes the vendored uploader source from
  the backend checkout.
- `.github/workflows/release-uploader.yml` builds release artifacts after this
  folder is published as its own GitHub repository.
- `src/LumiCodex.Upload` is the vendored standalone uploader source.

## Collection Tools

- Collection management and collection share tools are implemented by the
  backend `GroupTools.cs` and exposed through the existing `mcp/photos`
  server; this plugin does not vendor their implementation.
- Update plugin descriptions, prompts, and README when the `groups_*` surface
  materially changes. Do not run the uploader sync script unless
  `LumiCodex.Upload` source or bundled assets changed.
- Read the mirrored workspace `lc-collections` skill for the full backend,
  CoreUI, admin, viewer, authorizer, and rollout contract.

## Uploader Source Contract

- `LumiCodex/LumiCodexSolution/LumiCodex.Upload` is canonical;
  `src/LumiCodex.Upload` must be an exact vendored source copy for releases.
- After backend uploader changes, run `scripts/sync-uploader-source.sh`, review
  the diff, and run uploader tests before packaging. Do not patch only the
  vendored copy.
- The public CLI includes `whoami`, `albums list`, `albums create`, normal
  uploads, document transfer, and `encode ultra`. Keep the packaged skill and
  release assets in sync with `--help` when commands change.
- Ultra conversion depends on libvips; JPEG XL also needs `cjxl` with
  `--override_bitdepth` support. Native codec tools are external dependencies,
  not silently bundled release assets.

## Release Asset Names

- `lumicodex-upload-dotnet10.tar.gz` and `lumicodex-upload-dotnet10.zip` for the
  universal framework-dependent .NET 10 build.
- `lumicodex-upload-linux-x64.tar.gz`
- `lumicodex-upload-linux-arm64.tar.gz`
- `lumicodex-upload-osx-x64.tar.gz`
- `lumicodex-upload-osx-arm64.tar.gz`
- `lumicodex-upload-win-x64.zip`
- `lumicodex-upload-win-arm64.zip`

Native AOT must be built on the target OS family. Use CI matrix jobs for the
full asset set.
