# LumiCodex MCP Plugin

This project packages the LumiCodex production MCP endpoint and the
`lumicodex-upload` image uploader for OpenAI Codex and Anthropic Claude Code.

## Key Files

- `.codex-plugin/plugin.json` is the Codex plugin manifest.
- `.claude-plugin/plugin.json` is the Claude Code plugin manifest.
- `.mcp.json` points to `https://api.lumicodex.com/mcp`.
- `skills/lumicodex-album-upload/SKILL.md` describes the album upload workflow.
- `scripts/install-uploader.*` downloads release assets and runs uploader configuration.
- `scripts/publish-uploader.sh` builds release assets from
  `src/LumiCodex.Upload` by default.
- `scripts/sync-uploader-source.sh` refreshes the vendored uploader source from
  the backend checkout.
- `.github/workflows/release-uploader.yml` builds release artifacts after this
  folder is published as its own GitHub repository.
- `src/LumiCodex.Upload` is the vendored standalone uploader source.

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
