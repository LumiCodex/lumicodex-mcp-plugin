param(
    [string]$Repo = $(if ($env:LUMICODEX_PLUGIN_REPO) { $env:LUMICODEX_PLUGIN_REPO } else { "lumicodex/lumicodex-mcp-plugin" }),
    [string]$Version = "latest",
    [string]$ApiUrl = $(if ($env:LUMICODEX_API_URL) { $env:LUMICODEX_API_URL } else { "https://api.lumicodex.com/" }),
    [string]$Account = $env:LUMICODEX_ACCOUNT_ID,
    [string]$InstallDir = $(if ($env:LUMICODEX_UPLOAD_INSTALL_DIR) { $env:LUMICODEX_UPLOAD_INSTALL_DIR } else { Join-Path $HOME ".lumicodex\bin" }),
    [switch]$PreferDotnet,
    [switch]$SkipConfigure
)

$ErrorActionPreference = "Stop"

function Get-Rid {
    $arch = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq "Arm64") { "arm64" } else { "x64" }
    if ($IsWindows -or $env:OS -eq "Windows_NT") { return "win-$arch" }
    if ($IsMacOS) { return "osx-$arch" }
    if ($IsLinux) { return "linux-$arch" }
    throw "Unsupported operating system."
}

function Get-ReleaseAsset([string]$Asset, [string]$Output) {
    if ($Version -eq "latest") {
        $url = "https://github.com/$Repo/releases/latest/download/$Asset"
    } else {
        $url = "https://github.com/$Repo/releases/download/$Version/$Asset"
    }
    Invoke-WebRequest -Uri $url -OutFile $Output
}

function Expand-ReleaseAsset([string]$Archive, [string]$Destination) {
    if ($Archive.EndsWith(".zip")) {
        Expand-Archive -Path $Archive -DestinationPath $Destination -Force
        return
    }

    if ($Archive.EndsWith(".tar.gz")) {
        & tar -xzf $Archive -C $Destination
        if ($LASTEXITCODE -ne 0) {
            throw "Could not extract $Archive."
        }
        return
    }

    throw "Unsupported archive format: $Archive"
}

function Set-UploaderExecutable([string]$Directory) {
    if ($IsWindows -or $env:OS -eq "Windows_NT") {
        return
    }

    $uploader = Join-Path $Directory "lumicodex-upload"
    if ((Test-Path $uploader) -and (Get-Command chmod -ErrorAction SilentlyContinue)) {
        & chmod +x $uploader
    }
}

function Get-UploaderPath([string]$Directory) {
    $candidates = @(
        (Join-Path $Directory "lumicodex-upload.exe"),
        (Join-Path $Directory "lumicodex-upload.cmd"),
        (Join-Path $Directory "lumicodex-upload")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "lumicodex-upload was not found in $Directory after extraction."
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("lumicodex-upload-" + [System.Guid]::NewGuid())
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    $rid = Get-Rid
    if (-not $PreferDotnet) {
        $extension = if ($rid.StartsWith("win-")) { "zip" } else { "tar.gz" }
        $asset = "lumicodex-upload-$rid.$extension"
        $archive = Join-Path $temp $asset
        try {
            Get-ReleaseAsset $asset $archive
            Expand-ReleaseAsset $archive $InstallDir
            Set-UploaderExecutable $InstallDir
        } catch {
            Write-Warning "Native uploader asset not found for $rid; falling back to the .NET 10 build."
            $PreferDotnet = $true
        }
    }

    if ($PreferDotnet) {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            throw ".NET 10 is required for the universal uploader asset. Install .NET 10 or use a native release asset."
        }
        $asset = "lumicodex-upload-dotnet10.zip"
        $archive = Join-Path $temp $asset
        Get-ReleaseAsset $asset $archive
        Expand-ReleaseAsset $archive $InstallDir
        Set-UploaderExecutable $InstallDir
    }

    $uploader = Get-UploaderPath $InstallDir

    if (-not $SkipConfigure) {
        if (-not $env:LUMICODEX_API_KEY) {
            $secret = Read-Host -Prompt "LumiCodex API key" -AsSecureString
            $plain = [System.Net.NetworkCredential]::new("", $secret).Password
            $env:LUMICODEX_API_KEY = $plain
        }
        $args = @("configure", "--api-url", $ApiUrl)
        if ($Account) { $args += @("--account", $Account) }
        & $uploader @args
    }

    Write-Host "Installed lumicodex-upload in $InstallDir"
    Write-Host "Add this directory to PATH for future shells if needed: $InstallDir"
} finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}
