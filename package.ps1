$ErrorActionPreference = 'Stop'
$stage = Join-Path $PSScriptRoot 'build\package'
$plugins = Join-Path $stage 'BepInEx\plugins'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
foreach($name in @('更rpg的火湖.dll','SaveCompatibilityFix.dll','ChurchStashCrafting.dll')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "plugins\$name") -Destination $plugins
}
Copy-Item -LiteralPath "$PSScriptRoot\README.md" -Destination "$stage\README.md"
$release = Join-Path $PSScriptRoot 'releases'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$archive = Join-Path $release 'SULFUR-EOTNMods-2026.10.20.zip'
Compress-Archive -Path "$stage\BepInEx","$stage\README.md" -DestinationPath $archive -Force
$files = @(Get-ChildItem "$PSScriptRoot\plugins" -Filter '*.dll') + @(Get-Item -LiteralPath $archive)
$hashes = foreach($file in $files) {
    $relative = $file.FullName.Substring($PSScriptRoot.Length + 1).Replace('\','/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$hashes | Set-Content -LiteralPath "$release\SHA256SUMS.txt" -Encoding ascii
Write-Output $archive
