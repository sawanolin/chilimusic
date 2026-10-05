$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $projectRoot 'src\Native\vulkan-1.dll'
$license = Join-Path $projectRoot 'licenses\VulkanRT-License.txt'
$expected = 'CD862090370454630B31B174E3D4EB474FDA38EA034998D1FE1767B0C99A8696'
if ((Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $license) -and (Get-FileHash -LiteralPath $target).Hash -eq $expected) { return }
$cache = Join-Path $projectRoot '.local\downloads\vulkan'
New-Item -ItemType Directory -Path $cache,(Split-Path -Parent $target) -Force | Out-Null
$archive = Join-Path $cache 'VulkanRT-X64-1.4.357.0-Components.zip'
if (!(Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest 'https://vulkan.lunarg.com/sdk/download/1.4.357.0/windows/VulkanRT-X64-1.4.357.0-Components.zip' -OutFile ($archive + '.part')
    Move-Item -LiteralPath ($archive + '.part') -Destination $archive -Force
}
if ((Get-FileHash -LiteralPath $archive).Hash -ne 'A14672EFED15AAFC7F5A16572D35CD3A3416EADF670AEEE3CDF50EE32D5FBF83') { throw 'Vulkan runtime archive SHA256 mismatch' }
Expand-Archive -LiteralPath $archive -DestinationPath $cache -Force
$components = Join-Path $cache 'VulkanRT-X64-1.4.357.0-Components'
$dll = Join-Path $components 'x64\vulkan-1.dll'
if ((Get-FileHash -LiteralPath $dll).Hash -ne $expected) { throw 'Vulkan loader SHA256 mismatch' }
$signature = Get-AuthenticodeSignature -LiteralPath $dll
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'LunarG') { throw 'Vulkan loader signature invalid' }
Copy-Item -LiteralPath $dll -Destination $target -Force
Copy-Item -LiteralPath (Join-Path $components 'VulkanRT-License.txt') -Destination $license -Force
