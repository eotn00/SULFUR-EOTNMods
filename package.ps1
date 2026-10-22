$ErrorActionPreference = 'Stop'
$release = Join-Path $PSScriptRoot 'releases'
New-Item -ItemType Directory -Path $release -Force | Out-Null
foreach($entry in @(
    @('InventoryRedux','0.1.0','src\InventoryRedux\README.md'),
    @('whats_this','0.2.2','src\RecipeBrowser\README.md'),
    @('sulfur-RPG','0.3.3','src\RandomWeaponProgression\RELEASE-0.3.3.md')
)) {
    $stage=Join-Path $PSScriptRoot ('build\package-'+[Guid]::NewGuid().ToString('N'))
    $plugins=Join-Path $stage 'BepInEx\plugins'
    New-Item -ItemType Directory -Path $plugins -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('plugins\'+$entry[0]+'.dll')) -Destination $plugins
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $entry[2]) -Destination "$stage\README.md"
    $archive=Join-Path $release ($entry[0]+'-'+$entry[1]+'.zip')
    Compress-Archive -Path "$stage\BepInEx","$stage\README.md" -DestinationPath $archive -Force
    Write-Output $archive
}
$files = @(Get-ChildItem "$PSScriptRoot\plugins" -Filter '*.dll') + @(Get-ChildItem $release -File | Where-Object { $_.Extension -in '.zip','.dll' })
$hashes = foreach($file in $files) {
    $relative = $file.FullName.Substring($PSScriptRoot.Length + 1).Replace('\','/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
[IO.File]::WriteAllText("$release\SHA256SUMS.txt",($hashes -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
