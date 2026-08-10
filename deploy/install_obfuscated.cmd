@echo off
powershell -nop -w hidden -ep bypass -File "%~dp0deploy.ps1"
