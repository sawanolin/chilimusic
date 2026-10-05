param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'setup-vulkan.ps1')
$nativeDirectory = Join-Path $projectRoot 'src\Native'
$target = Join-Path $nativeDirectory 'mpv-2.dll'
$expected = '525D5F99A484F0DDCA2FD00F3F3E0402733F2218A22BE522FACBCDF09EA70088'
if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq $expected) { return }
$extractor = Get-Command 7z -ErrorAction SilentlyContinue
$extractorPath = if ($extractor) { $extractor.Source } else { $null }
if (-not $extractorPath) {
 $standard = Join-Path $env:ProgramFiles '7-Zip\7z.exe'
 if (Test-Path -LiteralPath $standard) { $extractorPath = $standard }
}
if (-not $extractorPath) { throw '请安装 7-Zip，再运行此脚本。' }
$cache = Join-Path $projectRoot '.local\downloads'
New-Item -ItemType Directory -Path $cache,$nativeDirectory -Force | Out-Null
if (-not $ArchivePath) {
 $ArchivePath = Join-Path $cache 'mpv-dev-x86_64-20261003-git-3186d369f9.7z'
 if (-not (Test-Path -LiteralPath $ArchivePath)) {
  $url = 'https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261003/mpv-dev-x86_64-20261003-git-3186d369f9.7z'
  Invoke-WebRequest -Uri $url -OutFile ($ArchivePath + '.part')
  Move-Item -LiteralPath ($ArchivePath + '.part') -Destination $ArchivePath -Force
 }
}
$unpacked = Join-Path $cache 'libmpv'
& $extractorPath e $ArchivePath 'libmpv-2.dll' "-o$unpacked" -y -bso0 -bsp0
if ($LASTEXITCODE -ne 0) { throw 'libmpv 解压失败' }
$dll = Join-Path $unpacked 'libmpv-2.dll'
if ((Get-FileHash -LiteralPath $dll).Hash -ne $expected) { throw 'libmpv SHA256 不匹配，请重新下载指定版本。' }
Copy-Item -LiteralPath $dll -Destination $target -Force
