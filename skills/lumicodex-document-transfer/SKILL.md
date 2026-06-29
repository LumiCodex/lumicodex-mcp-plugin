---
name: lumicodex-document-transfer
description: Move documents between the local machine and LumiCodex for the signatures and documents MCP tools using lumicodex-upload. Use when a user wants to send a local PDF/DOCX into an e-signature envelope or a document-processing tool, or to download a processed result (generated/merged/archived document, signed envelope document, or proof) back to disk.
---

# LumiCodex Document Transfer

Use this workflow whenever a documents (`lumicodex-documents`) or signatures
(`lumicodex-signatures`) MCP tool needs a local file as input, or produces a
result that should be saved locally. Transfer bytes with `lumicodex-upload`
instead of passing base64 through the conversation — base64 is only acceptable
for very small files.

## When To Use

- The user references a local document path (e.g. `./contract.pdf`) to sign,
  OCR, mail-merge, generate from, archive, or extract metadata from.
- A tool returned a presigned download URL (for example `GenerateUrlOutput.url`,
  or the URLs from `envelopes_get_downloads`) and the user wants the file saved.

## Upload Local Documents (input to the tools)

1. Verify the uploader is available (`lumicodex-upload --help`; otherwise try
   `$HOME/.lumicodex/bin/lumicodex-upload`).
2. Upload the file(s):

   ```bash
   lumicodex-upload documents upload contract.pdf
   ```

   Add `--account ACCOUNT_ID` only when the account is not configured or
   inferable. stdout is a JSON array of `{ id, name }`; capture each `id`
   (an `ephemeral-...` document id).
3. Use the id with the relevant tool:
   - Documents tools accept the id directly as `Source.documentId`
     (e.g. `documents_ocr`, `documents_archive_pdf`, `documents_mail_merge`,
     `documents_generate`, `documents_signature_locations`, `documents_metadata`).
   - For `envelopes_add_document`, the tool wants a URL: call
     `documents_get_download_url` with the id to get a presigned URL, then pass
     it as `DocumentCreationData.URL`.

## Download Results (output from the tools)

1. Take the presigned URL from the tool result.
2. Save it:

   ```bash
   lumicodex-upload documents download --out ./result.pdf "PRESIGNED_URL"
   ```

   `--out` may be a directory (default: current directory) or, for a single
   URL, a full file path. Without a forced name the file is named from the
   response `Content-Disposition`, then the URL, then the content type. stdout
   is a JSON array of `{ name, path }`.

## Operating Rules

- Never print, log, or paste the LumiCodex API key.
- Prefer base64 tool parameters only for tiny inline content; use the uploader
  for real document files so bytes never pass through the conversation.
- Uploading requires the API key to have `Process` permission on the account.
  Downloads use only the presigned URL and need no API key.
- Parse the JSON on stdout; informational/progress text is written to stderr.
