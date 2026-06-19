$ErrorActionPreference = "Stop"
$apiUrl = if ($env:LUMICODEX_API_URL) { $env:LUMICODEX_API_URL } else { "https://api.lumicodex.com/" }

if ($env:LUMICODEX_API_KEY) {
    @{ "x-api-key" = $env:LUMICODEX_API_KEY } | ConvertTo-Json -Compress
    exit 0
}

$commands = @(
    "lumicodex-upload",
    (Join-Path $HOME ".lumicodex\bin\lumicodex-upload.exe"),
    (Join-Path $HOME ".lumicodex\bin\lumicodex-upload.cmd")
)

foreach ($command in $commands) {
    if (Get-Command $command -ErrorAction SilentlyContinue) {
        & $command mcp-headers --api-url $apiUrl
        exit $LASTEXITCODE
    }
}

Write-Error "lumicodex-upload is not installed and LUMICODEX_API_KEY is not set."
