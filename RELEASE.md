# Release Checklist

Use this after publishing this folder as the `lumicodex-mcp-plugin` repository.

## Before Tagging

1. Sync the uploader source from the backend checkout:

   ```bash
   ./scripts/sync-uploader-source.sh ../LumiCodex/LumiCodexSolution/LumiCodex.Upload
   ```

2. Validate the plugin manifests:

   ```bash
   python3 /path/to/plugin-creator/scripts/validate_plugin.py .
   claude plugin validate .
   ```

3. Build the vendored uploader source:

   ```bash
   dotnet build src/LumiCodex.Upload/LumiCodex.Upload.csproj -c Release
   ```

## Release Assets

The GitHub Actions workflow `.github/workflows/release-uploader.yml` creates:

- `lumicodex-upload-dotnet10.tar.gz`
- `lumicodex-upload-dotnet10.zip`
- `lumicodex-upload-linux-x64.tar.gz`
- `lumicodex-upload-linux-arm64.tar.gz`
- `lumicodex-upload-osx-x64.tar.gz`
- `lumicodex-upload-osx-arm64.tar.gz`
- `lumicodex-upload-win-x64.zip`
- `lumicodex-upload-win-arm64.zip`

Publish a GitHub release from a tag to attach those files automatically. Use
the manual workflow dispatch to test asset generation before creating a public
release.

## Smoke Test

After the release is published, test the install path without exposing a real
API key in shell history:

```bash
./scripts/install-uploader.sh --repo OWNER/REPO --version TAG --account ACCOUNT_ID
lumicodex-upload mcp-headers >/dev/null
```

Then load the plugin in Claude Code:

```bash
claude --plugin-dir .
```

Run `/mcp` and confirm the scoped LumiCodex servers you kept (e.g.
`lumicodex-photos`) are connected.
