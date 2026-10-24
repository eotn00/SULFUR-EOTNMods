param([string]$PublishDirectory=(Join-Path $PSScriptRoot 'releases'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $PublishDirectory -Force | Out-Null
foreach($entry in @(
    @('InventoryRedux.dll','InventoryRedux-0.1.5.zip','src\InventoryRedux\RELEASE-0.1.5.md'),
    @('TraderFramework.dll','TraderFramework-0.1.4.zip','src\TraderFramework\RELEASE-0.1.4.md'),
    @('InventoryRedux.RummageCompat.dll','InventoryRedux-Rummage-Compatibility-0.1.6.zip','src\RummageInventoryReduxCompat\README.md')
)) {
    $archive=Join-Path $PublishDirectory $entry[1]
    if(Test-Path -LiteralPath $archive){throw "Archive already exists: $archive"}
    $stage=Join-Path $PSScriptRoot ('build\package-'+[Guid]::NewGuid().ToString('N'))
    $plugins=Join-Path $stage 'BepInEx\plugins'
    if($entry[0] -eq 'InventoryRedux.RummageCompat.dll'){$plugins=Join-Path $plugins 'InventoryReduxRummageCompatibility'}
    New-Item -ItemType Directory -Path $plugins -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('plugins\'+$entry[0])) -Destination $plugins
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $entry[2]) -Destination (Join-Path $stage 'README.md')
    [IO.Compression.ZipFile]::CreateFromDirectory($stage,$archive,[IO.Compression.CompressionLevel]::Optimal,$false)
    Write-Output $archive
}
