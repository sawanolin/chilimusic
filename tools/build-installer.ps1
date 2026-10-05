param([string]$SourceDirectory, [string]$OutputDirectory, [string]$InnoSetupPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$SourceDirectory) { $SourceDirectory = Join-Path $projectRoot 'dist' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'release' }
$SourceDirectory = (Resolve-Path -LiteralPath $SourceDirectory).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
if (!$InnoSetupPath) {
    $candidates = @((Join-Path $projectRoot '.local\inno\ISCC.exe'), (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'), (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'))
    $InnoSetupPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$InnoSetupPath) { $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue; if ($compiler) { $InnoSetupPath = $compiler.Source } }
}
if (!$InnoSetupPath -or !(Test-Path -LiteralPath $InnoSetupPath)) { throw '请安装 Inno Setup 7，或通过 -InnoSetupPath 指定 ISCC.exe。' }
$compilerVersion = (& $InnoSetupPath '--version' | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $compilerVersion -notmatch '^(\d+)\.\d+\.\d+$' -or [int]$Matches[1] -lt 7) { throw '安装包需要 Inno Setup 7 或更新版本。' }
$expected = @('chilimusic.exe','chilimusic.dll','chilimusic.deps.json','chilimusic.runtimeconfig.json','Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll','TagLibSharp.dll','QRCoder.dll','mpv-2.dll','vulkan-1.dll','README.md','CHANGELOG.md','LICENSE.txt','docs/images/main-server.png','docs/images/taskbar-playing.png','licenses/MiSans-NOTICE.txt','licenses/mpv-Copyright.txt','licenses/mpv-GPL-2.0.txt','licenses/mpv-NOTICE.txt','licenses/TagLibSharp-LGPL-2.1.txt','licenses/TagLibSharp-NOTICE.txt','licenses/QRCoder-MIT.txt','licenses/NeteaseCloudMusicApi-MIT.txt','licenses/VulkanRT-License.txt')
$actual = @(Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File | ForEach-Object { $_.FullName.Substring($SourceDirectory.Length + 1).Replace('\','/') })
if (@(Compare-Object $expected $actual).Count) { throw '发行目录文件不符合安装包清单，请重新构建并检查。' }
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src\chilimusic.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号无效' }
$assemblyVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $SourceDirectory 'chilimusic.dll')).ProductVersion.Split('+')[0]
if ($assemblyVersion -ne $version) { throw '发行目录与源码版本不一致，请重新构建。' }
& $InnoSetupPath '/Qp' "/DAppVersion=$version" "/DSourceDir=$SourceDirectory" "/DOutputDir=$OutputDirectory" (Join-Path $projectRoot 'installer\chilimusic.iss')
if ($LASTEXITCODE -ne 0) { throw '安装包构建失败' }
$setup = Join-Path $OutputDirectory "chilimusic-$version-setup-x64.exe"
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'), "$hash  chilimusic-$version-setup-x64.exe`n", [Text.UTF8Encoding]::new($false))
Write-Output "安装包：$setup"
Write-Output "SHA256：$hash"
