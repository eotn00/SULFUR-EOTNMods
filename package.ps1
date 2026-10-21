$ErrorActionPreference = 'Stop'
$stage = Join-Path $PSScriptRoot ('build\package-' + [Guid]::NewGuid().ToString('N'))
$plugins = Join-Path $stage 'BepInEx\plugins'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'plugins\更rpg的火湖.dll') -Destination (Join-Path $plugins '更RPG的火湖.dll')
Copy-Item -LiteralPath "$PSScriptRoot\MoreRPGSulfur-3.0.1-README.txt" -Destination "$stage\README.txt"
$release = Join-Path $PSScriptRoot 'releases'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$archive = Join-Path $release 'MoreRPGSulfur-3.0.1.zip'
Compress-Archive -Path "$stage\BepInEx","$stage\README.txt" -DestinationPath $archive -Force
$files = @(Get-ChildItem "$PSScriptRoot\plugins" -Filter '*.dll') + @(Get-ChildItem $release -Filter '*.zip')
$hashes = foreach($file in $files) {
    $relative = $file.FullName.Substring($PSScriptRoot.Length + 1).Replace('\','/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$hashes | Set-Content -LiteralPath "$release\SHA256SUMS.txt" -Encoding utf8
Write-Output $archive
