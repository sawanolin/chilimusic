@echo off
setlocal
set "DOTNET_ROOT=%~dp0.local\dotnet"
start "" "%~dp0dist\chilimusic.exe" %*
