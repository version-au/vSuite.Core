@echo off
rem Inno Setup's sign tool (an app's build.cmd passes /Svsuite=...sign.cmd $f): signs the setup and its uninstaller with sign.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sign.ps1" -Require %*
exit /b %errorlevel%
