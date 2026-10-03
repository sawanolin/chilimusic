param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'publish' }
$localSdk = Join-Path $projectRoot '.local\dotnet\dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
if (Test-Path -LiteralPath $localSdk) {
 $env:DOTNET_ROOT = Split-Path -Parent $localSdk
 $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.local\dotnet-home'
 $env:NUGET_PACKAGES = Join-Path $projectRoot '.local\nuget'
}
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'src\Native\mpv-2.dll'))) { & (Join-Path $PSScriptRoot 'setup-native.ps1') }
& $sdk publish (Join-Path $projectRoot 'src\chilimusic.csproj') -c Release -r win-x64 --self-contained false -o $OutputDirectory -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'LICENSE.txt') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $OutputDirectory -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $OutputDirectory -Recurse -Force
