# LumiCodex Upload

Cross-platform image uploader intended for direct use and narrow command
allowlisting by coding agents.

## Configuration

```bash
lumicodex-upload configure \
  --api-url https://api.lumicodex.com/ \
  --account ACCOUNT_ID
```

The command validates the API key and stores it in Windows Credential Manager,
macOS Keychain, or Linux Secret Service. On Linux, the `secret-tool` executable
provided by libsecret is required.

`LUMICODEX_API_KEY` takes precedence over the saved credential. The API URL and
account can be supplied through `--api-url` / `--account` or
`LUMICODEX_API_URL` / `LUMICODEX_ACCOUNT_ID`.

Remote API URLs must use HTTPS. Plain HTTP is accepted only for loopback
development URLs.

## MCP header helper

Agents that support MCP dynamic headers can reuse the configured credential:

```bash
lumicodex-upload mcp-headers
```

The command prints a compact JSON object containing the `x-api-key` header. It
reads `LUMICODEX_API_KEY` first, then the operating-system credential saved by
`lumicodex-upload configure`.

## Upload

Inputs may be individual image files, folders, or a mixture of both. Folders
are scanned at their top level by default.

```bash
lumicodex-upload \
  --container CONTAINER_ID \
  --publish \
  photo1.jpg photo2.jpg
```

Upload every supported image directly inside a folder:

```bash
lumicodex-upload \
  --container CONTAINER_ID \
  --publish \
  ./photos
```

Include nested folders:

```bash
lumicodex-upload \
  --container CONTAINER_ID \
  --recursive \
  --publish \
  ./photos
```

Supported images are selected by extension. Unsupported files are skipped and
reported. Files discovered inside each folder are processed in deterministic
path order, and a file supplied more than once is uploaded only once.

Large selections are prepared in batches of 500 images. Processing status is
read from the authoritative ingest/item records before optional publication.

## Documents

The `documents` subcommands move files for the signatures and documents MCP
tools without round-tripping bytes through the agent as base64.

Upload local documents into ephemeral storage:

```bash
lumicodex-upload documents upload contract.pdf addendum.docx
```

Files of any type are accepted (folders are scanned at their top level, or
recursively with `--recursive`). Each file is sent directly to a presigned URL.
A JSON array of `{ id, name }` is printed to stdout; progress goes to stderr.
Feed each `id` into a documents tool as `Source.documentId`, or call
`documents_get_download_url` to obtain a URL for `envelopes_add_document`.

Download processed results back to disk:

```bash
lumicodex-upload documents download --out ./merged.pdf "PRESIGNED_URL"
```

The URLs are the presigned download URLs returned by the documents/signatures
tools (for example `GenerateUrlOutput.url` or `envelopes_get_downloads`).
`--out` accepts a directory (the default is the current directory) or, for a
single URL, an explicit file path. When no name is forced, it is taken from the
response `Content-Disposition`, then the URL, then the content type. Downloads
use only the presigned URL and need no API key.

## Publishing

Portable framework-dependent output:

```bash
dotnet publish LumiCodex.Upload/LumiCodex.Upload.csproj \
  -c Release --self-contained false
```

Native AOT output:

```bash
dotnet publish LumiCodex.Upload/LumiCodex.Upload.csproj \
  -c Release -r linux-x64 --self-contained true -p:PublishAot=true
```

Use the same command with `win-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`.
Native AOT does not support cross-OS publishing, so Windows artifacts must be
built on Windows, Linux artifacts on Linux, and macOS artifacts on macOS.
