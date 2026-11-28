@echo off
rem Starts the Shiftline dev menu (tools\dev.ps1). Double click it, or make a desktop shortcut to this file.
rem Arguments are passed on, for example: dev.cmd -Run start
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" %*
if not "%~1"=="" pause
